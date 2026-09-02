using System.Windows;
using System.Windows.Controls;
using SSPen.Annotation;
using Xunit;

using static SSPen.Tests.StaThread;
namespace SSPen.Tests;

/// <summary>
/// <see cref="DrawingGestureController"/>의 증인 (46단계, ARCH-2, R2, fix 57b043d). 컨트롤러 없이 캔버스·상태·커밋 델리게이트·배지
/// 델리게이트만으로 획·도형·표의 시작/이동/업/폐기와 "소비 여부" 반환값을 고정한다. 컨트롤러를 거치는 사다리 순서·캡처 해제는
/// <see cref="SurfaceEntryPointTests"/>/<see cref="SurfaceTableGestureTests"/>/<see cref="SurfaceCancelOrderTests"/>가 그대로 증인이다.
/// <c>UIElement</c> 생성이 <c>InputManager</c>를 초기화하므로 STA에서 돈다 (창도 <c>Application</c>도 없다).
/// </summary>
public class DrawingGestureControllerTests
{
    private sealed class Rig
    {
        public Canvas Canvas { get; } = new();
        public AppState State { get; } = new();
        public List<(AnnotationElement Element, bool Fade)> Commits { get; } = [];
        public List<TableBadgeHint?> Badges { get; } = [];
        public DrawingGestureController Drawing { get; }

        public Rig() => Drawing = new DrawingGestureController(
            Canvas, State, (element, fade) => Commits.Add((element, fade)), hint => Badges.Add(hint));
    }

    private const float Pressure = StrokeGeometry.DefaultPressure;

    // ---- 획 ----

    [Fact]
    public void StartStroke_AddsOnePreviewPath_AndMoveAppendsPoints() => RunSta(() =>
    {
        var r = new Rig();

        r.Drawing.StartStroke(new Point(10, 10), ToolKind.Pen, Pressure);

        Assert.Single(r.Canvas.Children);
        Assert.True(r.Drawing.Active);
        Assert.Single(r.Drawing.ActiveStrokePoints!);

        Assert.True(r.Drawing.Move(new Point(30, 30), shift: false, Pressure));
        Assert.Equal(2, r.Drawing.ActiveStrokePoints!.Count);
        Assert.Empty(r.Commits); // 이동은 커밋이 아니다
    });

    [Fact]
    public void Move_WithoutGesture_ReturnsFalse_AndTouchesNothing() => RunSta(() =>
    {
        var r = new Rig();

        Assert.False(r.Drawing.Move(new Point(30, 30), shift: false, Pressure));
        Assert.False(r.Drawing.Active);
        Assert.Empty(r.Canvas.Children);
        Assert.Empty(r.Commits);
        Assert.Empty(r.Badges);
    });

    [Fact]
    public void Up_Stroke_RemovesPreview_CommitsStrokeElement_WithFadeFrozenAtStart() => RunSta(() =>
    {
        var r = new Rig();
        r.State.ActiveTool = ToolKind.Pen;
        r.State.FadingInk = true;
        r.Drawing.StartStroke(new Point(10, 10), ToolKind.Pen, Pressure);
        r.Drawing.Move(new Point(60, 40), shift: false, Pressure);
        r.State.FadingInk = false; // 드래그 중 토글 — 시작 시점 스냅샷이 이긴다

        Assert.True(r.Drawing.Up(new Point(60, 40), shift: false));

        Assert.Empty(r.Canvas.Children);
        Assert.False(r.Drawing.Active);
        var (element, fade) = Assert.Single(r.Commits);
        Assert.IsType<StrokeElement>(element);
        Assert.True(fade);
    });

    [Fact]
    public void Up_WithoutGesture_ReturnsFalse() => RunSta(() =>
    {
        var r = new Rig();

        Assert.False(r.Drawing.Up(new Point(5, 5), shift: false));
        Assert.Empty(r.Commits);
    });

    // ---- 도형 ----

    [Fact]
    public void Up_ShapeUnderThreshold_RemovesPreview_NoCommit_StillConsumed() => RunSta(() =>
    {
        var r = new Rig();
        r.Drawing.StartShape(ShapeKind.Rectangle, new Point(10, 10), ToolKind.Rectangle);
        Assert.Single(r.Canvas.Children);

        Assert.True(r.Drawing.Up(new Point(11, 11), shift: false)); // 폐기도 소비다

        Assert.Empty(r.Canvas.Children);
        Assert.Empty(r.Commits);
        Assert.False(r.Drawing.Active);
    });

    [Theory]
    [InlineData(ShapeKind.Line)]
    [InlineData(ShapeKind.Arrow)]
    [InlineData(ShapeKind.Rectangle)]
    [InlineData(ShapeKind.Ellipse)]
    public void Up_ShapeOverThreshold_CommitsShapeElement_OfStartedKind(ShapeKind kind) => RunSta(() =>
    {
        var r = new Rig();
        r.Drawing.StartShape(kind, new Point(10, 10), ToolKind.Rectangle);
        r.Drawing.Move(new Point(80, 50), shift: false, Pressure);

        Assert.True(r.Drawing.Up(new Point(80, 50), shift: false));

        var shape = Assert.IsType<ShapeElement>(Assert.Single(r.Commits).Element);
        Assert.Equal(kind, shape.Kind);
        Assert.Equal(new Point(10, 10), shape.Start);
        Assert.Equal(new Point(80, 50), shape.End);
        Assert.Empty(r.Canvas.Children);
    });

    // ---- 표 ----

    [Fact]
    public void StartTable_PushesBadgeOnce_WithStateSize_AndMoveRepushesAtPointer() => RunSta(() =>
    {
        var r = new Rig();
        r.State.TableRows = 2;
        r.State.TableColumns = 5;

        r.Drawing.StartTable(new Point(0, 0), ToolKind.Table);

        Assert.True(r.Drawing.TableActive);
        var start = Assert.Single(r.Badges);
        Assert.Equal(new TableBadgeHint(new Point(0, 0), new TableSize(2, 5)), start);

        Assert.True(r.Drawing.Move(new Point(30, 40), shift: false, Pressure));
        Assert.Equal(new Point(30, 40), r.Badges[^1]!.Value.Anchor);
    });

    [Fact]
    public void AdjustTable_DuringDrag_DoesNotTouchState_UntilCommitWritesOnce() => RunSta(() =>
    {
        var r = new Rig();
        r.State.TableRows = 2;
        r.State.TableColumns = 3;
        r.Drawing.StartTable(new Point(0, 0), ToolKind.Table);
        int changed = 0;
        r.State.Changed += () => changed++;

        Assert.True(r.Drawing.AdjustTable(TableAxis.Rows, +1));
        Assert.True(r.Drawing.AdjustTable(TableAxis.Rows, +1));
        Assert.Equal(0, changed); // fix 57b043d: 노치마다 AppState를 쓰지 않는다
        Assert.Equal(new TableSize(4, 3), r.Badges[^1]!.Value.Size);

        Assert.True(r.Drawing.Up(new Point(200, 100), shift: false));

        var table = Assert.IsType<TableElement>(Assert.Single(r.Commits).Element);
        Assert.Equal(4, table.Rows);
        Assert.Equal(3, table.Columns);
        Assert.Equal(4, r.State.TableRows);
        Assert.Equal(3, r.State.TableColumns);
        Assert.Null(r.Badges[^1]); // 커밋 = 배지 소멸
        Assert.Empty(r.Canvas.Children);
    });

    [Fact]
    public void Up_TableUnderThreshold_Discards_DoesNotWriteState() => RunSta(() =>
    {
        var r = new Rig();
        r.State.TableRows = 2;
        r.Drawing.StartTable(new Point(0, 0), ToolKind.Table);
        r.Drawing.AdjustTable(TableAxis.Rows, +1);

        Assert.True(r.Drawing.Up(new Point(1, 1), shift: false));

        Assert.Empty(r.Commits);
        Assert.Equal(2, r.State.TableRows); // 폐기된 드래그의 행·열은 기억하지 않는다
        Assert.Null(r.Badges[^1]);
        Assert.Empty(r.Canvas.Children);
    });

    [Fact]
    public void Wheel_DuringTable_ShiftAdjustsColumns_AnchorIsWheelPosition() => RunSta(() =>
    {
        var r = new Rig();
        r.State.TableRows = 2;
        r.State.TableColumns = 3;
        r.Drawing.StartTable(new Point(0, 0), ToolKind.Table);

        Assert.True(r.Drawing.Wheel(new Point(50, 60), +1, shift: true));

        Assert.Equal(new TableBadgeHint(new Point(50, 60), new TableSize(2, 4)), r.Badges[^1]);

        Assert.True(r.Drawing.Wheel(new Point(70, 80), -1, shift: false));
        Assert.Equal(new TableBadgeHint(new Point(70, 80), new TableSize(1, 4)), r.Badges[^1]);
    });

    [Fact]
    public void Wheel_And_AdjustTable_WithoutTable_ReturnFalse() => RunSta(() =>
    {
        var r = new Rig();
        r.Drawing.StartStroke(new Point(0, 0), ToolKind.Pen, Pressure); // 획 중에도 표 분기는 닫혀 있다

        Assert.False(r.Drawing.TableActive);
        Assert.False(r.Drawing.Wheel(new Point(5, 5), +1, shift: false));
        Assert.False(r.Drawing.AdjustTable(TableAxis.Rows, +1));
        Assert.Empty(r.Badges);
    });

    [Fact]
    public void AdjustTable_UsesLastMovePosition_AsBadgeAnchor() => RunSta(() =>
    {
        var r = new Rig();
        r.Drawing.StartTable(new Point(0, 0), ToolKind.Table);
        r.Drawing.Move(new Point(30, 40), shift: false, Pressure);

        Assert.True(r.Drawing.AdjustTable(TableAxis.Columns, -1));

        Assert.Equal(new Point(30, 40), r.Badges[^1]!.Value.Anchor); // 방향키는 마지막 이동 위치가 기준점
    });

    // ---- 폐기 ----

    [Fact]
    public void DiscardAll_RemovesEveryPreview_NeverCommits_PushesNullBadge() => RunSta(() =>
    {
        var r = new Rig();

        r.Drawing.StartTable(new Point(0, 0), ToolKind.Table);
        r.Drawing.Move(new Point(90, 90), shift: false, Pressure);
        r.Drawing.DiscardAll();
        Assert.Empty(r.Canvas.Children);
        Assert.Null(r.Badges[^1]);
        Assert.False(r.Drawing.TableActive);

        r.Drawing.StartStroke(new Point(0, 0), ToolKind.Pen, Pressure);
        r.Drawing.DiscardAll();
        Assert.Empty(r.Canvas.Children);
        Assert.Null(r.Drawing.ActiveStrokePoints);

        r.Drawing.StartShape(ShapeKind.Ellipse, new Point(0, 0), ToolKind.Ellipse);
        r.Drawing.DiscardAll();
        Assert.Empty(r.Canvas.Children);

        Assert.False(r.Drawing.Active);
        Assert.Empty(r.Commits);
    });

    /// <summary>표가 없어도 폐기는 배지 null 힌트를 민다 — 소멸 신호가 한 곳에서 나가는 오늘의 규약 (창은 null을 멱등 처리한다).</summary>
    [Fact]
    public void DiscardAll_WhenIdle_StillPushesOneNullBadge_Today() => RunSta(() =>
    {
        var r = new Rig();

        r.Drawing.DiscardAll();

        Assert.Single(r.Badges);
        Assert.Null(r.Badges[0]);
        Assert.Empty(r.Commits);
    });
}
