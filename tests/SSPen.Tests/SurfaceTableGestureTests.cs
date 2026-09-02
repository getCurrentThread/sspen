using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using SSPen.Annotation;
using Xunit;
using static SSPen.Tests.StaThread;

namespace SSPen.Tests;

/// <summary>
/// 표(Table) 도구 제스처의 컨트롤러 수준 특성화 (리팩터링 18단계, R2/R8/ARCH-2).
///
/// 837853a/948b037이 넣은 표 경로는 이 파일 이전에는 컨트롤러 수준 증인이 없었다. 여기서는 <b>오늘의 동작</b>을
/// 그대로 고정한다 — 3px 커밋 임계(도형과 같은 값, Today), 행·열 1..10 클램프, 미리보기 Path 1 + HUD 배지 Border 1,
/// 취소 = 폐기(원장 항목 없음), 시작 시점 페이딩 스냅샷, 그리고 fix(표)가 정한 "행·열은 확정 시점에 1회만
/// AppState에 쓴다". 뒤따르는 22~24단계(TableGestureRules·Wheel shift 인자·setTableBadge 이음매)는 이 파일이
/// 초록인 채로 지나가야 한다.
///
/// 정직한 표기:
/// - <c>Wheel</c>의 표 분기는 아직 <c>KeyboardState.Shift</c>(GetAsyncKeyState)를 직접 읽는다 (D3 위반, 23단계가
///   고친다). 그래서 이 파일은 Shift가 눌리지 않은 상태를 전제로 <b>행(rows)</b>만 단언한다 — 실행 중 Shift를 누르고
///   있으면 열이 바뀌어 빨갛다. 23단계에서 <c>Wheel_ShiftDuringTableDrag_ChangesColumnsNotRows</c>가 붙는다.
/// - <c>OnKeyDown</c>(방향키) 어댑터는 <c>KeyEventArgs</c>가 <c>PresentationSource</c>를 요구해 헤드리스로 구동할 수
///   없다. 23단계의 Point-free 진입점 <c>AdjustTable</c>이 생긴 뒤 <c>AdjustTable_WithoutTableDrag_ReturnsFalse</c>로 붙는다.
/// - 비인터랙티브 전환은 창(<c>ContentSurfaceWindow.ApplyState</c>)이 <c>CancelActiveInput</c>을 동기로 부르는 것이
///   계약이다. 창이 없는 이 하네스는 그 호출을 테스트가 대신 한다.
/// - WPF Geometry 류는 MTA에서도 만들어지지만(2026-09-02 실측) 이 파일은 Canvas·Path·Border를 다루므로 STA다.
/// </summary>
public class SurfaceTableGestureTests
{
    private static readonly DateTime FixedNow = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly Point Start = new(10, 10);
    private static readonly Point Far = new(110, 90);

    [Fact]
    public void PointerDown_TableTool_ReturnsTrue_AddsPreviewPathAndBadgeBorder()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Table;

            Assert.True(h.Controller.PointerDown(Start, shift: false));

            Assert.Single(h.Canvas.Children.OfType<Path>());
            Assert.Single(h.Canvas.Children.OfType<Border>());
            Assert.Equal(2, h.Canvas.Children.Count);
        });
    }

    [Fact]
    public void PointerUp_TableDragBeyondThreshold_CommitsTableElementWithStateRowsColumns()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Table;
            h.State.TableRows = 4;
            h.State.TableColumns = 2;

            h.Controller.PointerDown(Start, shift: false);
            h.Controller.PointerMove(Far, shift: false, leftPressed: true);
            h.Controller.PointerUp(Far, shift: false);

            var table = Assert.IsType<TableElement>(Assert.Single(h.Document.Elements));
            Assert.Equal(4, table.Rows);
            Assert.Equal(2, table.Columns);
            Assert.Equal(Start, table.Start);
            Assert.Equal(Far, table.End);
            Assert.Equal(1, h.Ledger.Count);
            // 미리보기 Path와 배지 Border는 커밋과 함께 캔버스에서 사라진다 (커밋된 시각물은 창이 붙인다).
            Assert.Empty(h.Canvas.Children);
        });
    }

    [Fact]
    public void PointerUp_TableClickUnderThreshold_CommitsNothingAndClearsPreview()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Table;

            h.Controller.PointerDown(Start, shift: false);
            h.Controller.PointerUp(new Point(Start.X + 2, Start.Y), shift: false);

            Assert.Empty(h.Document.Elements);
            Assert.Equal(0, h.Ledger.Count);
            Assert.Empty(h.Canvas.Children);
        });
    }

    /// <summary>
    /// 커밋 임계는 오늘 표(CommitTable 리터럴 3)와 도형(<see cref="ShapeGestureRules.ShouldCommit"/>)이 같은 값이다.
    /// 경계 3px에서 커밋되는 부호(<c>&lt; 3</c>이 폐기 = <c>&gt;= 3</c>이 커밋)를 잠근다 — 23단계가 리터럴을
    /// ShouldCommit으로 바꿀 때 부호가 뒤집히면 여기가 빨갛다.
    /// </summary>
    [Fact]
    public void PointerUp_TableExactlyThreePixels_Commits_Today()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Table;

            h.Controller.PointerDown(Start, shift: false);
            h.Controller.PointerUp(new Point(Start.X + 3, Start.Y), shift: false);

            Assert.Single(h.Document.Elements);
            Assert.True(ShapeGestureRules.ShouldCommit(Start, new Point(Start.X + 3, Start.Y)));
            Assert.False(ShapeGestureRules.ShouldCommit(Start, new Point(Start.X + 2, Start.Y)));
        });
    }

    [Fact]
    public void Wheel_DuringTableDrag_ReturnsTrueAndIncrementsRows()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Table;

            h.Controller.PointerDown(Start, shift: false);
            Assert.True(h.Controller.Wheel(Far, +1));
            h.Controller.PointerUp(Far, shift: false);

            var table = Assert.IsType<TableElement>(Assert.Single(h.Document.Elements));
            Assert.Equal(4, table.Rows);
            Assert.Equal(3, table.Columns);
        });
    }

    [Fact]
    public void Wheel_DuringTableDrag_ClampsRowsAtTen()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Table;

            h.Controller.PointerDown(Start, shift: false);
            Assert.True(h.Controller.Wheel(Far, +20));
            h.Controller.PointerUp(Far, shift: false);

            Assert.Equal(10, Assert.IsType<TableElement>(Assert.Single(h.Document.Elements)).Rows);
        });
    }

    [Fact]
    public void Wheel_DuringTableDrag_ClampsRowsAtOne()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Table;

            h.Controller.PointerDown(Start, shift: false);
            Assert.True(h.Controller.Wheel(Far, -20));
            h.Controller.PointerUp(Far, shift: false);

            Assert.Equal(1, Assert.IsType<TableElement>(Assert.Single(h.Document.Elements)).Rows);
        });
    }

    /// <summary>
    /// fix(표): 노치마다 AppState를 쓰면 Changed가 z-밴드 재적용·전 서피스 ApplyState·설정 저장 예약을 매번 돌린다.
    /// 드래그 중에는 0회, 확정 시점에 바뀐 축만큼(여기서는 행 1회) 발화한다.
    /// </summary>
    [Fact]
    public void Wheel_DuringTableDrag_DoesNotRaiseChanged_CommitWritesRowsOnce()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Table;
            int changed = 0;
            h.State.Changed += () => changed++;

            h.Controller.PointerDown(Start, shift: false);
            h.Controller.Wheel(Far, +1);
            h.Controller.Wheel(Far, +1);
            Assert.Equal(0, changed);
            Assert.Equal(3, h.State.TableRows);

            h.Controller.PointerUp(Far, shift: false);

            Assert.Equal(1, changed);
            Assert.Equal(5, h.State.TableRows);
            Assert.Equal(3, h.State.TableColumns);
        });
    }

    [Fact]
    public void Cancel_DuringTableDrag_DiscardsPreviewAndBadge_NoLedgerEntry_NoStateWrite()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Table;

            h.Controller.PointerDown(Start, shift: false);
            h.Controller.Wheel(Far, +1);
            h.Controller.PointerMove(Far, shift: false, leftPressed: true);
            h.Controller.CancelActiveInput();

            Assert.Empty(h.Canvas.Children);
            Assert.Empty(h.Document.Elements);
            Assert.Equal(0, h.Ledger.Count);
            Assert.Equal(3, h.State.TableRows);
            Assert.Equal(1, h.ReleaseCaptureCalls);
        });
    }

    /// <summary>
    /// 표 휠 분기는 라우터의 비인터랙티브 가드보다 <b>앞</b>에서 선점한다 (SurfaceInputController.Wheel 머리).
    /// 그래도 "비인터랙티브인데 표 미리보기가 살아 있는" 상태는 지속되지 않는다 — 창의 ApplyState가
    /// 전환 즉시 CancelActiveInput을 동기로 부르기 때문이다. 여기서는 창 역할을 테스트가 대신해
    /// 그 순서 뒤에는 휠이 라우터로 떨어져 false가 됨을 고정한다 (라우터 표를 바꾸지 않는 근거, 23단계).
    /// </summary>
    [Fact]
    public void Wheel_AfterNonInteractiveTransitionAndCancel_ReturnsFalse_PreviewDiscarded()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Table;
            h.Controller.PointerDown(Start, shift: false);
            Assert.Equal(2, h.Canvas.Children.Count);

            h.State.ClickThrough = true; // → ActiveTool=None, IsInteractive=false (창은 여기서 ApplyState를 돈다)
            Assert.False(h.State.IsInteractive);
            h.Controller.CancelActiveInput();

            Assert.False(h.Controller.Wheel(Far, +1));
            Assert.Empty(h.Canvas.Children);
            Assert.Empty(h.Document.Elements);
        });
    }

    [Fact]
    public void PointerUp_FadingTable_SchedulesFadeFromInjectedClock()
    {
        RunSta(() =>
        {
            var h = new Harness(() => FixedNow);
            h.State.ActiveTool = ToolKind.Table;
            h.State.FadingInk = true;
            h.Fading.Duration = TimeSpan.FromSeconds(5);

            h.Controller.PointerDown(Start, shift: false);
            // 시작 시점 스냅샷: 드래그 중 토글을 꺼도 이 표는 페이딩이다.
            h.State.FadingInk = false;
            h.Controller.PointerUp(Far, shift: false);

            Assert.Single(h.Document.Elements);
            Assert.Empty(h.Fading.Core.Due(FixedNow.AddSeconds(4.9)));
            Assert.Single(h.Fading.Core.Due(FixedNow.AddSeconds(5)));
        });
    }

    [Fact]
    public void PointerMove_DuringTableDrag_KeepsExactlyOnePreviewAndOneBadge()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Table;

            h.Controller.PointerDown(Start, shift: false);
            h.Controller.PointerMove(new Point(40, 40), shift: false, leftPressed: true);
            h.Controller.PointerMove(Far, shift: false, leftPressed: true);
            h.Controller.Wheel(Far, +1);

            Assert.Single(h.Canvas.Children.OfType<Path>());
            Assert.Single(h.Canvas.Children.OfType<Border>());
        });
    }

    /// <summary>캔버스 미측정 — 표 제스처는 핸들 히트가 없어 경계 값이 무의미하다. 시계만 주입한다.</summary>
    private sealed class Harness(Func<DateTime>? now = null)
        : SurfaceHarness(new SurfaceHarnessOptions { Now = now });
}
