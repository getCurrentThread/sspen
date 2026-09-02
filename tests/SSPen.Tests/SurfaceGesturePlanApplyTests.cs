using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

using static SSPen.Tests.StaThread;
namespace SSPen.Tests;

/// <summary>
/// <c>SurfaceInputController.BeginSelectGesture</c>가 <see cref="GesturePlan"/>을 <b>옮겨 적는 순서</b>의 증인.
/// 플래너 자체는 <see cref="SelectionGesturePlannerTests"/>가 값으로 검증하므로, 여기서는 순수 계획이
/// 볼 수 없는 것만 본다 — 선택집합 쓰기와 스냅샷의 선후, 제스처 프레임 푸시 횟수, 캡처·걸쇠.
///
/// <see cref="SurfaceEntryPointTests"/>와 달리 잉크 캔버스를 실제로 measure/arrange 한다.
/// 그래야 <c>SurfaceBounds</c>가 <c>(0,0,0,0)</c>이 아니어서 회전 핸들 클램프가 렌더와 같은 위치를
/// 내고 그룹 핸들을 잡을 수 있다 (R5). 창도 <c>Application</c>도 만들지 않으므로 헤드리스 안전하다.
/// </summary>
public class SurfaceGesturePlanApplyTests
{
    private const double SurfaceWidth = 1920;
    private const double SurfaceHeight = 1080;

    private static StrokeElement Stroke(double x, double y, double w, double h) =>
        new([new Point(x, y), new Point(x + w, y + h)], Colors.Red, 2, isHighlighter: false);

    // ---- SEL-AC-9: SelectHit은 반드시 스냅샷보다 앞이다 ----

    /// <summary>
    /// 고르지 않은 요소를 클릭한 <b>같은 제스처</b>에서 곧바로 끌면 움직여야 한다.
    /// 선택 교체를 스냅샷 뒤로 미루면 방금 고른 요소의 시작 상태가 비고,
    /// <c>MoveSelection</c>은 시작 상태가 없는 요소를 예외도 로그도 없이 건너뛰므로
    /// 드래그가 <b>조용히 무동작</b>이 된다 (SEL-AC-9).
    /// </summary>
    [Fact]
    public void PointerDown_ClickUnselectedElement_ThenDragMovesIt()
    {
        RunSta(() =>
        {
            var h = new Harness();
            var a = Stroke(400, 400, 50, 50);
            h.Document.Add(a);
            h.State.ActiveTool = ToolKind.Select;

            h.Controller.PointerDown(new Point(425, 425), shift: false);
            Assert.Equal([a], h.Selection.Elements);

            h.Controller.PointerMove(new Point(445, 435), shift: false, leftPressed: true);

            Assert.Equal(new Vector(20, 10), a.TransformState.Translation);
        });
    }

    /// <summary>
    /// 그룹 프레임 <b>안쪽 빈 자리</b>를 눌러 끌면 선택 전원이 함께 움직인다 (R6 + SEL-AC-9).
    /// 커서 밑에는 요소가 없으므로, 이 경로가 살아 있지 않으면 마퀴가 시작되어 선택이 날아간다.
    /// </summary>
    [Fact]
    public void PointerDown_ClickInsideFrame_ThenDragMovesWholeSelection()
    {
        RunSta(() =>
        {
            var h = new Harness();
            var a = Stroke(400, 400, 50, 50);
            var b = Stroke(600, 600, 50, 50);
            h.Document.Add(a);
            h.Document.Add(b);
            h.Selection.Set([a, b]);
            h.State.ActiveTool = ToolKind.Select;

            h.Controller.PointerDown(new Point(500, 550), shift: false);
            h.Controller.PointerMove(new Point(520, 570), shift: false, leftPressed: true);

            Assert.Equal(new Vector(20, 20), a.TransformState.Translation);
            Assert.Equal(new Vector(20, 20), b.TransformState.Translation);
            Assert.Null(h.Marquee);
        });
    }

    // ---- R1: 그려지는 프레임은 회전에서만 밀린다 ----

    /// <summary>
    /// 그룹 <b>회전</b> 핸들을 잡으면 마우스 다운 한 번에 동결 크기·각도 0의 프레임이
    /// <b>정확히 한 번</b> 밀린다 (머리의 null 해제 뒤 한 번).
    /// </summary>
    [Fact]
    public void PointerDown_GroupRotateHandle_PushesPosedFrameOnce()
    {
        RunSta(() =>
        {
            var h = new Harness();
            var a = Stroke(400, 400, 50, 50);
            var b = Stroke(600, 600, 50, 50);
            h.Document.Add(a);
            h.Document.Add(b);
            h.Selection.Set([a, b]);
            h.State.ActiveTool = ToolKind.Select;
            var frame = SelectionGroup.Frame([a, b])!.Value;

            h.Controller.PointerDown(SelectionGroup.RotateHandle(frame), shift: false);

            Assert.Equal([null, new GroupFrame(frame, 0)], h.FramePushes);
        });
    }

    /// <summary>
    /// 그룹 <b>모서리</b> 핸들은 아무것도 밀지 않는다 — 살아있는 합집합이 정답이므로
    /// 동결 프레임을 밀면 마우스 업에서 프레임이 튄다 (R1). 머리의 null 해제만 남는다.
    /// </summary>
    [Fact]
    public void PointerDown_GroupCornerHandle_PushesNoPosedFrame()
    {
        RunSta(() =>
        {
            var h = new Harness();
            var a = Stroke(400, 400, 50, 50);
            var b = Stroke(600, 600, 50, 50);
            h.Document.Add(a);
            h.Document.Add(b);
            h.Selection.Set([a, b]);
            h.State.ActiveTool = ToolKind.Select;
            var frame = SelectionGroup.Frame([a, b])!.Value;

            h.Controller.PointerDown(
                SelectionGroup.CornerCenter(frame, GroupHandleKind.TopLeft), shift: false);

            Assert.Equal([null], h.FramePushes);

            // 그래도 배율·피벗의 기준은 동결되었다: 모서리를 끌면 실제로 커진다.
            h.Controller.PointerMove(new Point(frame.Right, frame.Bottom), shift: false, leftPressed: true);
            Assert.True(a.TransformState.ScaleX < 1);
        });
    }

    // ---- 토글·마퀴는 스냅샷도 캡처도 하지 않는다 ----

    /// <summary>
    /// Shift+요소는 토글로 끝난다 — 스냅샷을 잡지 않으므로 이어지는 이동이 아무것도 옮기지 않는다 (SEL-AC-3).
    /// </summary>
    [Fact]
    public void PointerDown_ShiftClickElement_TogglesWithoutStartingDrag()
    {
        RunSta(() =>
        {
            var h = new Harness();
            var a = Stroke(400, 400, 50, 50);
            var b = Stroke(600, 600, 50, 50);
            h.Document.Add(a);
            h.Document.Add(b);
            h.Selection.Set([a]);
            h.State.ActiveTool = ToolKind.Select;

            h.Controller.PointerDown(new Point(625, 625), shift: true);
            Assert.Equal([a, b], h.Selection.Elements);

            h.Controller.PointerMove(new Point(700, 700), shift: true, leftPressed: true);

            Assert.Equal(default, a.TransformState.Translation);
            Assert.Equal(default, b.TransformState.Translation);
        });
    }

    /// <summary>
    /// 마퀴는 시작 상태를 잡지 않는다. 잡으면 <c>CancelActiveInput</c>의 롤백이 선택 전원에
    /// 무의미한 상태 대입과 알림(R15)을 뿌려, 드래그도 하지 않은 요소가 재그리기를 유발한다.
    /// </summary>
    [Fact]
    public void PointerDown_ShiftEmptyArea_DoesNotSnapshotSelection()
    {
        RunSta(() =>
        {
            var h = new Harness();
            var a = Stroke(400, 400, 50, 50);
            h.Document.Add(a);
            h.Selection.Set([a]);
            h.State.ActiveTool = ToolKind.Select;

            h.Controller.PointerDown(new Point(1200, 900), shift: true);
            Assert.Equal([a], h.Selection.Elements); // Shift+빈 곳은 누적 의도라 해제하지 않는다
            int before = h.TransformNotifications;

            h.Controller.CancelActiveInput();

            Assert.Equal(before, h.TransformNotifications);
        });
    }

    /// <summary>
    /// R2/R5: 빈 곳 <b>마우스 다운</b>은 클릭 통과를 켜지 않는다. 여기서 켜면 IsInteractive가 떨어져
    /// 막 시작한 마퀴가 얼어붙고 버튼 업이 서피스에 닿지 못한다. 판정은 업에서만 난다.
    /// </summary>
    [Fact]
    public void PointerDown_EmptyArea_StartsMarqueeWithoutEngagingClickThrough()
    {
        RunSta(() =>
        {
            var h = new Harness();
            var a = Stroke(400, 400, 50, 50);
            h.Document.Add(a);
            h.Selection.Set([a]);
            h.State.ActiveTool = ToolKind.Select;

            h.Controller.PointerDown(new Point(1200, 900), shift: false);

            Assert.Equal(new Rect(new Point(1200, 900), new Point(1200, 900)), h.Marquee);
            Assert.Empty(h.Selection.Elements);
            Assert.Equal(0, h.ClickThroughRequests);

            // 제자리에서 떼면 그제서야 클릭 통과로 넘어간다 (걸쇠는 다운에서 세워졌다).
            h.Controller.PointerUp(new Point(1200, 900), shift: false);
            Assert.Equal(1, h.ClickThroughRequests);
        });
    }

    // ---- 반복 적용의 안정성 ----

    /// <summary>
    /// 커서 위치를 번갈아 100번 눌렀다 떼도 요소의 변형 상태는 <b>비트 동일</b>하게 항등이고
    /// 원장에는 아무것도 실리지 않는다 (f3: 제자리 클릭은 빈 undo 항목을 만들지 않는다).
    ///
    /// 적용부가 필드마다 도는 루프로 바뀌거나 스냅샷/선택 쓰기 순서가 흔들리면 여기서 잔여
    /// 변형이나 유령 원장 항목으로 드러난다.
    /// </summary>
    [Fact]
    public void Apply_HundredAlternatingCursorPositions_LeavesElementStatesBitIdentical()
    {
        RunSta(() =>
        {
            var h = new Harness();
            var a = Stroke(400, 400, 50, 50);
            var b = Stroke(600, 600, 50, 50);
            h.Document.Add(a);
            h.Document.Add(b);
            h.State.ActiveTool = ToolKind.Select;

            var onInk = new Point(425, 425);
            var empty = new Point(1200, 900);
            for (int i = 0; i < 100; i++)
            {
                var pos = i % 2 == 0 ? onInk : empty;
                h.Controller.PointerDown(pos, shift: false);
                h.Controller.PointerUp(pos, shift: false);
            }

            Assert.Equal(ElementTransformState.Identity, a.TransformState);
            Assert.Equal(ElementTransformState.Identity, b.TransformState);
            Assert.Empty(h.Commits);
            Assert.Null(h.Marquee);
            Assert.Null(h.GestureGroupFrame);
        });
    }

    /// <summary>컨트롤러 1대 + 그 협력자들. 창 대신 <see cref="ISurfaceHost"/>를 무동작으로 채운다.</summary>
    private sealed class Harness : ISurfaceHost
    {
        public Harness()
        {
            Canvas = new Canvas { Width = SurfaceWidth, Height = SurfaceHeight };
            // measure/arrange를 실제로 돌려 ActualWidth/Height를 채운다 — SurfaceBounds의 출처다.
            Canvas.Measure(new Size(SurfaceWidth, SurfaceHeight));
            Canvas.Arrange(new Rect(0, 0, SurfaceWidth, SurfaceHeight));

            State = new AppState();
            Document = new AnnotationDocument("test");
            Selection = new SelectionModel();
            Ledger = new UndoLedger(OwnerLookup, Selection);
            Fading = new FadingInkController(new FadeSchedulerCore());
            Document.ElementTransformChanged += _ => TransformNotifications++;
            Controller = new SurfaceInputController(
                Canvas, State, Document, Ledger, Fading, this,
                Selection, OwnerLookup, _ => 1.0,
                rect => Marquee = rect,
                frame => FramePushes.Add(frame),
                (deltas, drop) => Commits.Add((deltas, drop)),
                () => ClickThroughRequests++,
                // 프로덕션(창)과 **같은 식**으로 캔버스에서 유도한다 (R5).
                new SurfaceInputSeams
                {
                    SurfaceBounds = () => new Rect(0, 0, Canvas.ActualWidth, Canvas.ActualHeight),
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

        /// <summary>제스처 프레임 푸시 전부 (해제 null 포함) — 횟수와 순서가 검증 대상이다.</summary>
        public List<GroupFrame?> FramePushes { get; } = [];

        public GroupFrame? GestureGroupFrame => FramePushes.Count == 0 ? null : FramePushes[^1];

        public List<(IReadOnlyList<TransformDelta> Deltas, Point? Drop)> Commits { get; } = [];

        public int ClickThroughRequests { get; private set; }

        /// <summary>R15 알림 횟수 — 스냅샷을 잡았는지 여부의 관측 가능한 그림자다.</summary>
        public int TransformNotifications { get; private set; }

        private AnnotationDocument? OwnerLookup(AnnotationElement element) =>
            Document.Elements.Contains(element) ? Document : null;

        public void SetNoActivate(bool on) { }

        public void ActivateWindow() { }

        public void CaptureMouse() { }

        public void ReleaseMouseCapture() { }

        public DpiScale GetDpi() => new(1.0, 1.0);
    }
}
