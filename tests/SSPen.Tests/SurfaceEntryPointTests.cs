using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

using static SSPen.Tests.StaThread;
namespace SSPen.Tests;

/// <summary>
/// <see cref="SurfaceInputController"/>의 <c>Point</c> 진입점 검증 (ARCH-2, R7, WI-16, SEL-LIM-5).
///
/// 이 스위트가 존재할 수 있는 이유는 진입점이 WPF 이벤트 인자 대신 <c>Point</c>/<c>bool</c>을 받고,
/// 시계를 <see cref="SurfaceInputSeams"/>로 주입받기 때문이다. 검증 대상은 <b>Handled 판정</b>
/// (반환값)과 그 판정이 갈릴 때 실제로 달라지는 상태다.
///
/// 헤드리스 한계 두 가지를 지킨다:
/// - <c>inkCanvas.ActualWidth/Height</c>가 measure/arrange 없이 0이라 <c>SurfaceBounds</c>가
///   <c>Rect.Empty</c>가 아닌 <c>(0,0,0,0)</c>이 된다. <see cref="TransformMath.HitHandle"/>은
///   <c>Rect.Empty</c>일 때만 회전 핸들 클램프를 건너뛰므로, 이 스위트는 <b>어떤 핸들도 잡지 않는다</b>.
/// - <c>DispatcherTimer</c>는 디스패처 루프 없는 STA 쓰레드에서 틱하지 않으므로 휠 유휴 확정은
///   관측하지 않는다. 주입 시계의 도달 가능한 증인은 페이드 예약 경로 하나다.
///
/// <c>UIElement</c> 생성이 <c>InputManager</c>를 초기화하므로 본문은 STA 쓰레드에서 돈다
/// (창도 <c>Application</c>도 만들지 않으므로 여전히 헤드리스 안전하다).
/// </summary>
public class SurfaceEntryPointTests
{
    private static readonly DateTime FixedNow = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PointerDown_NotInteractive_ReturnsFalse()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Pen;
            h.State.SurfacesVisible = false;

            Assert.False(h.Controller.PointerDown(new Point(10, 10), shift: false));
            Assert.Empty(h.Canvas.Children);
        });
    }

    [Fact]
    public void PointerDown_PenTool_ReturnsTrue()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Pen;

            Assert.True(h.Controller.PointerDown(new Point(10, 10), shift: false));
            Assert.Single(h.Canvas.Children); // 진행 중 획의 미리보기 폴리라인
        });
    }

    [Fact]
    public void PointerDown_TextBoxOpenAndClickOutside_CommitsTextAndReturnsFalse()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Text;
            h.Controller.PointerDown(new Point(10, 10), shift: false, overActiveEditor: false);
            OpenTextBox(h).Text = "가나";

            // 바깥 클릭은 텍스트를 확정하되 **소비하지 않는다** (Round 13).
            bool handled = h.Controller.PointerDown(new Point(400, 400), shift: false, overActiveEditor: false);

            Assert.False(handled);
            Assert.Empty(h.Canvas.Children.OfType<TextBox>());
            Assert.Single(h.Document.Elements);
        });
    }

    [Fact]
    public void PointerDown_ClickInsideTextBox_FallsThroughToToolAndReturnsTrue()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Text;
            h.Controller.PointerDown(new Point(10, 10), shift: false, overActiveEditor: false);
            OpenTextBox(h).Text = "가나";

            // 상자 **안** 클릭은 바깥 클릭 분기를 타지 않고 도구로 떨어지므로 소비된다.
            bool handled = h.Controller.PointerDown(new Point(12, 12), shift: false, overActiveEditor: true);

            Assert.True(handled);
            Assert.Single(h.Canvas.Children.OfType<TextBox>()); // 편집이 이어진다
        });
    }

    [Fact]
    public void PointerMove_LeftButtonNotPressed_IsNoOp()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Pen;
            h.Controller.PointerDown(new Point(10, 10), shift: false);
            Assert.IsAssignableFrom<System.Windows.Shapes.Shape>(h.Canvas.Children[0]);

            h.Controller.PointerMove(new Point(60, 60), shift: false, leftPressed: false);
            Assert.Single(h.Controller.ActiveStrokePoints!);

            h.Controller.PointerMove(new Point(60, 60), shift: false, leftPressed: true);
            Assert.Equal(2, h.Controller.ActiveStrokePoints!.Count);
        });
    }

    [Fact]
    public void Wheel_NotInteractive_ReturnsFalse()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Pen;
            h.State.WheelAdjustsPenSize = true;
            var before = h.State.Thickness;
            h.State.SurfacesVisible = false;

            Assert.False(h.Controller.Wheel(new Point(10, 10), notches: +1));
            Assert.Equal(before, h.State.Thickness);
        });
    }

    [Fact]
    public void Wheel_SelectToolDragActive_ReturnsTrue()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Select;
            // 빈 곳 누르기 → 마퀴 드래그 진행 중 (핸들을 잡지 않는다).
            h.Controller.PointerDown(new Point(500, 500), shift: false);

            // 드래그 중 휠은 삼키기만 한다 — 두 세션이 같은 요소를 잡지 못하게 (R7).
            Assert.True(h.Controller.Wheel(new Point(500, 500), notches: +1));
            Assert.Empty(h.Commits);
        });
    }

    [Fact]
    public void Wheel_SelectToolCrossMonitorSelection_ReturnsFalse()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Select;
            var mine = MakeStroke(new Point(10, 10), new Point(40, 40));
            var theirs = MakeStroke(new Point(10, 10), new Point(40, 40));
            h.Document.Add(mine);
            h.Selection.Set([mine, theirs]); // 이 서피스는 둘 중 하나만 소유한다

            // SEL-LIM-5: 모니터에 걸친 선택은 확대하지 않고 휠을 통과시킨다.
            Assert.False(h.Controller.Wheel(new Point(25, 25), notches: +1));
            Assert.Equal(new ElementTransformState(1, 1, 0, default), mine.TransformState);
        });
    }

    [Fact]
    public void Wheel_NonSelectToolWithSettingOff_ReturnsFalse()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Pen;
            h.State.WheelAdjustsPenSize = false;
            var before = h.State.Thickness;

            Assert.False(h.Controller.Wheel(new Point(10, 10), notches: +1));
            Assert.Equal(before, h.State.Thickness);
        });
    }

    [Fact]
    public void Wheel_NonSelectToolWithSettingOn_StepsThicknessAndReturnsTrue()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Pen;
            h.State.WheelAdjustsPenSize = true;
            h.State.Thickness = ThicknessStep.Small;

            Assert.True(h.Controller.Wheel(new Point(10, 10), notches: +1));
            Assert.Equal(ThicknessStep.Medium, h.State.Thickness);
        });
    }

    [Fact]
    public void Escape_NoTextBox_ReturnsFalse()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Text;

            Assert.False(h.Controller.Escape());
            Assert.Empty(h.Document.Elements);
        });
    }

    [Fact]
    public void Escape_WithTextBox_CommitsAndReturnsTrue()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Text;
            h.Controller.PointerDown(new Point(10, 10), shift: false, overActiveEditor: false);
            OpenTextBox(h).Text = "가나";

            Assert.True(h.Controller.Escape());
            Assert.Single(h.Document.Elements);
            Assert.Empty(h.Canvas.Children.OfType<TextBox>());
        });
    }

    /// <summary>
    /// 페이드 마감이 <b>주입 시계</b>에서 나온다 (R7 이음매의 도달 가능한 유일한 증인).
    /// 실제 <c>DateTime.UtcNow</c>를 쓰면 마감이 2020년이 아니라 오늘이 되어 아래 <c>Due</c>가 빈다.
    /// </summary>
    [Fact]
    public void PointerUp_FadingStroke_SchedulesFadeFromInjectedClock()
    {
        RunSta(() =>
        {
            var h = new Harness(() => FixedNow);
            h.State.ActiveTool = ToolKind.Pen;
            h.State.FadingInk = true;
            h.Fading.Duration = TimeSpan.FromSeconds(5);

            h.Controller.PointerDown(new Point(10, 10), shift: false);
            h.Controller.PointerMove(new Point(60, 60), shift: false, leftPressed: true);
            h.Controller.PointerUp(new Point(60, 60), shift: false);

            Assert.Single(h.Document.Elements);
            Assert.Empty(h.Fading.Core.Due(FixedNow.AddSeconds(4.9)));
            Assert.Single(h.Fading.Core.Due(FixedNow.AddSeconds(5)));
        });
    }

    /// <summary>
    /// R8: 스타일러스 뒤집기(지우개 꼭지)는 활성 도구가 펜이더라도 즉시 지우개로 동작한다.
    /// AppState.ActiveTool은 변하지 않아야 한다 (SEL-B-4).
    /// </summary>
    [Fact]
    public void PointerDown_PenTool_Inverted_ActsAsEraserWithoutChangingActiveTool()
    {
        RunSta(() =>
        {
            var h = new Harness();
            var stroke = MakeStroke(new Point(10, 10), new Point(30, 30));
            h.Document.Add(stroke);
            h.State.ActiveTool = ToolKind.Pen;

            bool handled = h.Controller.PointerDown(new Point(20, 20), shift: false, overActiveEditor: false, inverted: true);

            Assert.True(handled);
            Assert.Empty(h.Document.Elements); // 획이 지워짐
            Assert.Empty(h.Canvas.Children); // 펜 획 미리보기가 시작되지 않음
            Assert.Equal(ToolKind.Pen, h.State.ActiveTool); // ActiveTool 유지
        });
    }

    /// <summary>
    /// R8: 스타일러스 뒤집기 상태로 드래그하면 지나간 획들을 연속 삭제한다.
    /// </summary>
    [Fact]
    public void PointerMove_InvertedDrag_ErasesMultipleElements()
    {
        RunSta(() =>
        {
            var h = new Harness();
            var stroke1 = MakeStroke(new Point(10, 10), new Point(30, 30));
            var stroke2 = MakeStroke(new Point(60, 60), new Point(80, 80));
            h.Document.Add(stroke1);
            h.Document.Add(stroke2);
            h.State.ActiveTool = ToolKind.Pen;

            h.Controller.PointerDown(new Point(20, 20), shift: false, overActiveEditor: false, inverted: true);
            Assert.Single(h.Document.Elements); // 첫 번째 획 지워짐

            h.Controller.PointerMove(new Point(70, 70), shift: false, leftPressed: true);
            Assert.Empty(h.Document.Elements); // 두 번째 획도 지워짐

            h.Controller.PointerUp(new Point(70, 70), shift: false);
            Assert.Equal(ToolKind.Pen, h.State.ActiveTool);
        });
    }

    private static TextBox OpenTextBox(Harness h) => Assert.Single(h.Canvas.Children.OfType<TextBox>());

    private static StrokeElement MakeStroke(Point a, Point b) =>
        new([a, b], Colors.Red, thickness: 4, isHighlighter: false);

    /// <summary>컨트롤러 1대 + 그 협력자들. 창 대신 <see cref="ISurfaceHost"/>를 무동작으로 채운다.</summary>
    private sealed class Harness : ISurfaceHost
    {
        public Harness(Func<DateTime>? now = null)
        {
            Canvas = new Canvas();
            State = new AppState();
            Document = new AnnotationDocument("test");
            Selection = new SelectionModel();
            Ledger = new UndoLedger(OwnerLookup, Selection);
            Fading = new FadingInkController(new FadeSchedulerCore());
            Controller = new SurfaceInputController(
                Canvas, State, Document, Ledger, Fading, this,
                Selection, OwnerLookup, _ => 1.0,
                rect => Marquee = rect,
                frame => GestureGroupFrame = frame,
                (deltas, drop) => Commits.Add((deltas, drop)),
                () => ClickThroughRequests++,
                new SurfaceInputSeams
                {
                    // 프로덕션(창)과 **같은 식**으로 캔버스에서 유도한다. measure/arrange가 없으므로
                    // 값은 (0,0,0,0)이고, 그래서 위 문서대로 이 스위트는 어떤 핸들도 잡지 않는다.
                    SurfaceBounds = () => new Rect(0, 0, Canvas.ActualWidth, Canvas.ActualHeight),
                    Now = now ?? (() => DateTime.UtcNow),
                    // R7: 실제 DispatcherTimer는 펌프 없는 STA 쓰레드에서 영영 틱하지 않는다.
                    IdleScheduler = Idle,
                });
        }

        /// <summary>휠 유휴 디바운스 가짜 (R7) — 만료는 테스트가 직접 일으킨다.</summary>
        public FakeIdleScheduler Idle { get; } = new();

        public Canvas Canvas { get; }

        public AppState State { get; }

        public AnnotationDocument Document { get; }

        public SelectionModel Selection { get; }

        public UndoLedger Ledger { get; }

        public FadingInkController Fading { get; }

        public SurfaceInputController Controller { get; }

        public Rect? Marquee { get; private set; }

        public GroupFrame? GestureGroupFrame { get; private set; }

        public List<(IReadOnlyList<TransformDelta> Deltas, Point? Drop)> Commits { get; } = [];

        public int ClickThroughRequests { get; private set; }

        private AnnotationDocument? OwnerLookup(AnnotationElement element) =>
            Document.Elements.Contains(element) ? Document : null;

        public void SetNoActivate(bool on) { }

        public void ActivateWindow() { }

        public void CaptureMouse() { }

        public void ReleaseMouseCapture() { }

        public DpiScale GetDpi() => new(1.0, 1.0);
    }
}
