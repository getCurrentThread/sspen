using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SSPen.Annotation;
using Xunit;

using static SSPen.Tests.StaThread;
namespace SSPen.Tests;

/// <summary>
/// <c>SurfaceInputController.CancelActiveInput</c>의 증인 (ARCH-2, ARCH-6, R7, R15, SEL-LIM-6).
///
/// 이 메서드는 "진행 중인 것을 전부 정리한다"가 아니라 <b>취소 의미가 서로 다른 다섯 가지</b>를
/// 정해진 순서로 마감하는 오케스트레이터다 — 획·도형은 폐기, 텍스트는 <b>커밋</b>, 변형은 롤백,
/// 휠은 <b>확정</b>, 제스처 각도는 소멸. 그 비대칭이 무너지면(예: 균일한 <c>Cancel()</c>로 묶으면)
/// 사용자가 입력한 글자가 사라지거나 원장에 없는 변형이 화면에 남는다.
///
/// <see cref="SurfaceGesturePlanApplyTests"/>와 같은 이유로 잉크 캔버스를 실제로 measure/arrange
/// 한다 — 그래야 <c>SurfaceBounds</c>가 <c>(0,0,0,0)</c>이 아니어서 그룹 회전 핸들을 잡을 수 있다 (R5).
/// 창도 <c>Application</c>도 만들지 않으므로 헤드리스 안전하다. 측정 결과(<c>ActualWidth</c> 등)를
/// 단언 대상으로 삼지 않고, 휠 확정도 <c>DispatcherTimer</c> 만료가 아니라 명시 호출로 유도한다.
/// </summary>
public class SurfaceCancelOrderTests
{
    private const double SurfaceWidth = 1920;
    private const double SurfaceHeight = 1080;

    private static StrokeElement Stroke(double x, double y, double w, double h) =>
        new([new Point(x, y), new Point(x + w, y + h)], Colors.Red, 2, isHighlighter: false);

    // ---- 텍스트만 폐기가 아니라 커밋이다 (ARCH-2) ----

    /// <summary>
    /// 편집 중 텍스트는 취소에서 <b>커밋</b>된다. ARCH-2 NOACTIVATE 핸드셰이크로 이미 창을 활성화하고
    /// 포커스까지 준 편집이므로, 여기서 폐기하면 사용자가 방금 친 글자가 예고 없이 사라진다.
    /// </summary>
    [Fact]
    public void Cancel_TextBoxIsCommittedNotDiscarded()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Text;
            h.Controller.PointerDown(new Point(300, 300), shift: false, overActiveEditor: false);
            Assert.Single(h.Canvas.Children.OfType<TextBox>()).Text = "가나";

            h.Controller.CancelActiveInput();

            Assert.Empty(h.Canvas.Children.OfType<TextBox>());
            var element = Assert.IsType<TextElement>(Assert.Single(h.Document.Elements));
            Assert.Equal("가나", element.Text);
            Assert.Equal(1, h.Ledger.Count); // 추가 항목 1건 — 실행취소로 지울 수 있다
        });
    }

    // ---- 획·도형은 폐기다 (원장 항목이 없으므로 지울 것도 남기지 않는다) ----

    /// <summary>
    /// 진행 중 획은 <b>폐기</b>된다. 미리보기 폴리라인만 있고 원장 항목이 없으므로 커밋으로 바꾸면
    /// 사용자가 완성하지 않은 획이 문서에 들어간다.
    /// </summary>
    [Fact]
    public void Cancel_StrokeIsDiscardedWithNoLedgerEntry()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Pen;
            h.Controller.PointerDown(new Point(100, 100), shift: false);
            h.Controller.PointerMove(new Point(180, 160), shift: false, leftPressed: true);
            Assert.Single(h.Canvas.Children.OfType<Shape>());

            h.Controller.CancelActiveInput();

            Assert.Empty(h.Document.Elements);
            Assert.Equal(0, h.Ledger.Count);
            Assert.Empty(h.Canvas.Children.OfType<Shape>());
        });
    }

    /// <summary>진행 중 도형도 <b>폐기</b>다 (획과 같은 이유).</summary>
    [Fact]
    public void Cancel_ShapeIsDiscardedWithNoLedgerEntry()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Rectangle;
            h.Controller.PointerDown(new Point(100, 100), shift: false);
            h.Controller.PointerMove(new Point(300, 260), shift: false, leftPressed: true);
            Assert.Single(h.Canvas.Children.OfType<Shape>());

            h.Controller.CancelActiveInput();

            Assert.Empty(h.Document.Elements);
            Assert.Equal(0, h.Ledger.Count);
            Assert.Empty(h.Canvas.Children.OfType<Shape>());
        });
    }

    // ---- 이 메서드의 원래 존재 이유: 유령 드래그 삭제 방지 ----

    /// <summary>
    /// gen-7 자문(MED)의 원래 시나리오. 지우개 드래그 중 비인터랙티브로 전환되면 버튼 업이 유실되는데,
    /// 걸쇠가 남아 있으면 그 뒤 도착하는 이동이 계속 요소를 지운다 (유령 드래그 삭제).
    /// </summary>
    [Fact]
    public void Cancel_ClearsEraserDraggingFlag()
    {
        RunSta(() =>
        {
            var h = new Harness();
            var a = Stroke(400, 400, 50, 50);
            var b = Stroke(600, 600, 50, 50);
            h.Document.Add(a);
            h.Document.Add(b);
            h.State.ActiveTool = ToolKind.Eraser;

            h.Controller.PointerDown(new Point(425, 425), shift: false);
            Assert.Equal([b], h.Document.Elements); // 걸쇠가 섰고 a는 지워졌다

            h.Controller.CancelActiveInput();
            h.Controller.PointerMove(new Point(625, 625), shift: false, leftPressed: true);

            Assert.Equal([b], h.Document.Elements); // 취소 뒤의 이동은 더 이상 지우지 않는다
            Assert.Equal(1, h.Ledger.Count);
        });
    }

    // ---- ARCH-6: 캡처 해제는 언제나 마지막에 정확히 한 번 ----

    /// <summary>
    /// 캡처 해제(ARCH-6)는 정확히 한 번이다. 앞쪽 단계마다 흩어 놓으면 롤백·휠 확정이 끝나기도 전에
    /// 캡처가 풀려, 그 사이 도착하는 입력이 다른 창으로 새어 나간다.
    /// </summary>
    [Fact]
    public void Cancel_ReleasesMouseCaptureExactlyOnce()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Pen;
            h.Controller.PointerDown(new Point(100, 100), shift: false);
            Assert.Equal(0, h.ReleaseCaptureCalls);

            h.Controller.CancelActiveInput();

            Assert.Equal(1, h.ReleaseCaptureCalls);
        });
    }

    // ---- 마퀴 해제는 제스처가 살아 있을 때만 (무조건 호출 금지) ----

    /// <summary>
    /// <c>setMarquee(null)</c>은 <c>_dragKind != None</c>일 때만이다. 무조건 호출로 바꾸면
    /// 제스처가 없는 상태의 취소(도구 전환·클릭 통과 전환마다 온다)가 창에 의미 없는 장식 갱신을
    /// 매번 흘린다.
    /// </summary>
    [Fact]
    public void Cancel_ClearsMarqueeOnlyWhenAGestureWasLive()
    {
        RunSta(() =>
        {
            var live = new Harness();
            live.State.ActiveTool = ToolKind.Select;
            live.Controller.PointerDown(new Point(1200, 900), shift: false); // 빈 곳 → 마퀴 시작
            Assert.NotNull(live.MarqueePushes[^1]);

            live.Controller.CancelActiveInput();
            Assert.Equal(1, live.MarqueePushes.Count(m => m is null));

            var idle = new Harness();
            idle.State.ActiveTool = ToolKind.Select;

            idle.Controller.CancelActiveInput();
            Assert.Equal(0, idle.MarqueePushes.Count(m => m is null));
        });
    }

    // ---- SEL-LIM-6: 제스처 각도는 ResetSelectGesture 한 곳에서만 소멸한다 ----

    /// <summary>
    /// 그룹 회전 중 취소하면 그려지는 프레임의 각도가 반드시 <b>소멸</b>해야 한다.
    /// 남으면 다음 마우스 다운의 히트 테스트가 화면에 그려진 것과 다른 프레임을 보게 되어,
    /// 보이는 핸들을 눌렀는데 빈 곳 분기로 떨어진다 (SEL-LIM-6).
    /// </summary>
    [Fact]
    public void Cancel_DuringGroupRotate_ReleasesGestureGroupFrame()
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
            h.Controller.PointerMove(new Point(frame.Right + 120, frame.Top + 40), shift: false, leftPressed: true);
            // 비어 있지 않은 각도가 실제로 밀려 있었음을 먼저 확인한다 (거짓 안심 방지).
            Assert.NotNull(h.FramePushes[^1]);
            Assert.NotEqual(0, h.FramePushes[^1]!.Value.AngleDegrees);

            h.Controller.CancelActiveInput();

            Assert.Null(h.FramePushes[^1]);
        });
    }

    // ---- 피어 불변식: 머리의 null 푸시를 ResetSelectGesture로 바꾸면 안 된다 ----

    /// <summary>
    /// 버튼 업을 잃은 그룹 회전이 <b>살아 있는 채로</b> 다음 마우스 다운을 맞아도, 그 뒤의 취소는
    /// 여전히 시작 상태로 롤백해야 한다 (R15).
    ///
    /// <c>BeginSelectGesture</c> 머리는 각도만 지우는 <c>setGestureGroupFrame(null)</c>이고,
    /// 거기서 <c>ResetSelectGesture()</c>를 부르면 시작 상태 스냅샷이 함께 사라져 롤백이 예외도
    /// 로그도 없이 무동작이 된다 — 원장에 없는 변형이 화면에 남아 실행취소로 지울 수 없게 된다.
    /// 이어지는 마우스 다운을 <b>Shift+빈 곳</b>으로 잡는 이유: 그 경로는 마퀴라서 스냅샷을 새로
    /// 잡지 않으므로(SEL-AC-3/R15), 살아남아야 할 스냅샷이 덮이지 않고 그대로 관측된다.
    /// </summary>
    [Fact]
    public void LostMouseUp_ThenNewPress_CancelStillRollsBackInFlightTransform()
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
            h.Controller.PointerMove(new Point(frame.Right + 120, frame.Top + 40), shift: false, leftPressed: true);
            Assert.NotEqual(ElementTransformState.Identity, a.TransformState);
            Assert.NotEqual(ElementTransformState.Identity, b.TransformState);

            // 버튼 업 유실 재현: 업 없이 곧바로 다음 마우스 다운이 온다.
            h.Controller.PointerDown(new Point(1200, 900), shift: true);
            Assert.Equal([a, b], h.Selection.Elements); // Shift+빈 곳은 누적 의도라 해제하지 않는다

            h.Controller.CancelActiveInput();

            Assert.Equal(ElementTransformState.Identity, a.TransformState);
            Assert.Equal(ElementTransformState.Identity, b.TransformState);
            Assert.Empty(h.Commits); // 롤백은 원장에 아무것도 싣지 않는다
        });
    }

    /// <summary>캔버스를 실제 크기로 측정한다 — 핸들·회전 판정이 살아 있어야 취소 순서를 관측할 수 있다.</summary>
    private sealed class Harness()
        : SurfaceHarness(new SurfaceHarnessOptions { Layout = new Size(SurfaceWidth, SurfaceHeight) });
}
