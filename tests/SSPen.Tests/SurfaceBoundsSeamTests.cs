using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="SurfaceInputSeams.SurfaceBounds"/> 배선의 증인 (R5). 서피스 논리 경계는 창이 유일
/// 소유자이고, 컨트롤러는 이 이음매로만 그 값을 본다 — "그려지는 위치 == 잡히는 위치"가 두 벌로
/// 계산되지 않는다는 사실이 여기서 관측된다.
///
/// 관측 방법: 화면 위쪽에 붙은 요소의 회전 핸들은 서피스 밖으로 나가 안쪽으로 <b>클램프</b>되어 그려진다.
/// 그 클램프된 자리를 누르면 회전이 시작되어야 한다. 배선이 틀리면 같은 점이 크기 핸들
/// <c>Top</c>에 걸려 <b>조용히 크기 조절</b>이 되므로, 회전 여부(<c>AngleDegrees != 0</c>)가
/// 세 가지 오배선(누락·<c>Rect.Empty</c>·생성 시점 캡처)을 전부 갈라낸다.
///
/// <c>UIElement</c> 생성이 <c>InputManager</c>를 초기화하므로 본문은 STA 쓰레드에서 돈다
/// (창도 <c>Application</c>도 만들지 않으므로 헤드리스 안전하다).
/// </summary>
public class SurfaceBoundsSeamTests
{
    private static readonly Rect Surface = new(0, 0, 1920, 1080);

    /// <summary>상단에 붙은 요소: 로컬 경계 (800,2,100,60), 상단 변 중앙 (850,2).</summary>
    private static StrokeElement TopEdgeStroke() =>
        new([new Point(800, 2), new Point(900, 62)], Colors.Red, 2, isHighlighter: false);

    /// <summary>클램프된 회전 핸들 (850, 4) — 미클램프는 (850, −22)라 서피스 밖이다.</summary>
    private static Point ClampedRotateSpot(AnnotationElement element) =>
        TransformMath.ClampRotateHandle(
            TransformMath.RotateHandleWorld(element.TransformState, element.LocalBounds),
            Surface,
            TransformMath.HandleScreenSize / 2);

    /// <summary>
    /// 클램프된 자리에서 회전이 시작된다 — 이 단계의 진짜 증인이다.
    /// 이음매를 <c>Rect.Empty</c>로 배선하면 (850,4)는 회전 히트를 놓치고 크기 핸들 <c>Top</c>
    /// (로컬 (850,2), reach 4)에 걸려 각도가 0으로 남는다.
    /// </summary>
    [Fact]
    public void PointerDown_RotateHandleClampedAtTopEdge_StartsRotateNotScale()
    {
        RunSta(() =>
        {
            var h = new Harness(() => Surface);
            var a = TopEdgeStroke();
            h.Document.Add(a);
            h.State.ActiveTool = ToolKind.Select;
            h.Selection.Set([a]);

            var spot = ClampedRotateSpot(a);
            Assert.Equal(850, spot.X, 9);
            Assert.Equal(TransformMath.HandleScreenSize / 2, spot.Y, 9);

            h.Controller.PointerDown(spot, shift: false);
            // 중심 (850,32) 기준 축에서 벗어난 점으로 끌면 각도가 붙는다.
            h.Controller.PointerMove(new Point(878, 32), shift: false, leftPressed: true);

            Assert.NotEqual(0, a.TransformState.AngleDegrees);
            Assert.Equal(1, a.TransformState.ScaleX, 9); // 크기 조절로 새지 않았다
        });
    }

    /// <summary>
    /// 이음매는 <b>매 히트 테스트마다</b> 평가된다. 생성 시점에는 measure/arrange가 아직 돌지 않아
    /// 경계가 <c>(0,0,0,0)</c>이고, 그 값을 필드에 얼려 두면 <c>IsEmpty == false</c>라 클램프 경로로
    /// 들어가 <c>left &gt; right</c> 분기가 중심점을 돌려주어 <b>회전 핸들이 (0,0)으로 붕괴</b>한다.
    /// </summary>
    [Fact]
    public void SeamIsEvaluatedPerHitTest_NotCapturedAtConstruction()
    {
        RunSta(() =>
        {
            var bounds = new Rect(0, 0, 0, 0); // 레이아웃 전 상태
            var h = new Harness(() => bounds);
            var a = TopEdgeStroke();
            h.Document.Add(a);
            h.State.ActiveTool = ToolKind.Select;
            h.Selection.Set([a]);

            bounds = Surface; // 레이아웃이 돈 뒤

            h.Controller.PointerDown(ClampedRotateSpot(a), shift: false);
            h.Controller.PointerMove(new Point(878, 32), shift: false, leftPressed: true);

            Assert.NotEqual(0, a.TransformState.AngleDegrees);
        });
    }

    /// <summary>
    /// <c>Rect.Empty</c>는 "경계 없음"이 아니라 <see cref="TransformMath.ClampRotateHandle"/>의
    /// <b>다른 코드 경로</b>("클램프하지 않음")다. 그 경로에서는 회전 핸들이 서피스 밖 (850,−22)에
    /// 그대로 남아 거기서 잡힌다.
    ///
    /// 계약 고정용이며 증인이 아니다 — <c>required</c> 덕분에 프로덕션에서는 도달할 수 없는 값이고,
    /// 이 테스트는 "기본값을 두면 왜 안 되는가"의 근거를 실행 가능한 형태로 박아 둘 뿐이다.
    /// </summary>
    [Fact]
    public void SeamIsRectEmpty_RotateHandleIsGrabbableAtUnclampedSpot()
    {
        RunSta(() =>
        {
            var h = new Harness(() => Rect.Empty);
            var a = TopEdgeStroke();
            h.Document.Add(a);
            h.State.ActiveTool = ToolKind.Select;
            h.Selection.Set([a]);

            var unclamped = TransformMath.RotateHandleWorld(a.TransformState, a.LocalBounds);
            Assert.Equal(2 - TransformMath.RotateHandleScreenOffset, unclamped.Y, 9);

            h.Controller.PointerDown(unclamped, shift: false);
            h.Controller.PointerMove(new Point(904, 32), shift: false, leftPressed: true);

            Assert.NotEqual(0, a.TransformState.AngleDegrees);
        });
    }

    /// <summary>WPF 시각 객체를 만드는 본문만 STA 쓰레드로 보낸다 (예외는 스택 보존해 재던진다).</summary>
    private static void RunSta(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    /// <summary>
    /// 컨트롤러 1대 + 그 협력자들. 창 대신 <see cref="ISurfaceHost"/>를 무동작으로 채우고,
    /// 서피스 경계는 <b>테스트가 주는 델리게이트</b>가 그대로 흘러 들어간다 (캔버스에서 유도하지 않는다 —
    /// 그러면 이 스위트가 검증하려는 배선을 캔버스가 대신 메워 버린다).
    /// </summary>
    private sealed class Harness : ISurfaceHost
    {
        public Harness(Func<Rect> surfaceBounds)
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
                _ => { },
                _ => { },
                (deltas, drop) => Commits.Add((deltas, drop)),
                () => { },
                new SurfaceInputSeams { SurfaceBounds = surfaceBounds });
        }

        public Canvas Canvas { get; }

        public AppState State { get; }

        public AnnotationDocument Document { get; }

        public SelectionModel Selection { get; }

        public UndoLedger Ledger { get; }

        public FadingInkController Fading { get; }

        public SurfaceInputController Controller { get; }

        public List<(IReadOnlyList<TransformDelta> Deltas, Point? Drop)> Commits { get; } = [];

        private AnnotationDocument? OwnerLookup(AnnotationElement element) =>
            Document.Elements.Contains(element) ? Document : null;

        public void SetNoActivate(bool on) { }

        public void ActivateWindow() { }

        public void CaptureMouse() { }

        public void ReleaseMouseCapture() { }

        public DpiScale GetDpi() => new(1.0, 1.0);
    }
}
