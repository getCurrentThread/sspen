using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SSPen.Annotation;
using Xunit;

using static SSPen.Tests.StaThread;
using static SSPen.Tests.TestGeometry;
namespace SSPen.Tests;

/// <summary>
/// <c>SurfaceInputController.CancelActiveInput</c>의 증인 (ARCH-2, ARCH-6, R7, R15, SEL-LIM-6), 그리고
/// 버튼 업 유실 뒤 새 누름의 잔여 정리 <c>SettleOrphanedPress</c>의 증인 (91단계, A3-1 — 파일 끝 절)과
/// 그 사이 원장 변경을 낡은 스냅샷이 덮지 않는다는 증인 (96단계, FINAL-REVIEW-SETTLE-LEDGER — 마지막 절).
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
    /// 버튼 업을 잃은 그룹 회전이 다음 마우스 다운을 맞은 뒤에도, 요소는 시작 상태로 돌아가고
    /// 원장에는 아무것도 실리지 않아야 한다 (R15).
    ///
    /// 91단계(A3-1)부터는 <b>새 누름이 롤백한다</b> — <c>PointerDown</c>이 <c>SettleOrphanedPress</c>로
    /// 잔여 제스처를 롤백 → Reset한 뒤 새 제스처를 시작하므로, 뒤의 취소가 만나는 스냅샷은 이미 비어 있다.
    /// 그 전에는 스냅샷이 살아남아 뒤의 취소가 롤백했다. 어느 쪽이든 관측 결과(항등 상태 + 빈 커밋)는 같아야 한다.
    ///
    /// 피어 불변식은 그대로다: <c>BeginSelectGesture</c> 머리는 각도만 지우는 <c>setGestureGroupFrame(null)</c>이고,
    /// 거기서 롤백 없이 <c>ResetSelectGesture()</c>를 부르면 시작 상태 스냅샷이 함께 사라져 롤백이 예외도
    /// 로그도 없이 무동작이 된다 — 원장에 없는 변형이 화면에 남아 실행취소로 지울 수 없게 된다.
    /// 이어지는 마우스 다운을 <b>Shift+빈 곳</b>으로 잡는 이유: 그 경로는 마퀴라서 스냅샷을 새로
    /// 잡지 않고(SEL-AC-3/R15) 선택도 비우지 않으므로, 롤백 대상이 선택에 남은 채 관측된다.
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

    // ---- 버튼 업 유실 뒤 새 누름: SettleOrphanedPress (91단계, A3-1) ----
    //
    // 캡처를 잃어 버튼 업이 오지 않으면, 인터랙티브가 유지되는 한 진행 중 필드가 그대로 남는다
    // (도구 전환은 CancelActiveInput을 부르지 않는다). 다음 누름은 먼저 그 잔여를 정리해야 한다 —
    // 획·도형·표는 폐기, 변형은 롤백 → Reset. 아래 다섯 증인은 정리가 없을 때의 네 결함
    // (유령 지우기, 원장 없는 변형, 되살아난 회전, 고아 미리보기)을 하나씩 잠근다.

    /// <summary>
    /// 지우개 드래그의 업을 잃은 뒤 선택 도구로 요소를 끌면 <b>이동</b>이어야 한다.
    /// 걸쇠(<c>_eraserDragging</c>)가 남아 있으면 이동 사다리가 지우개 분기로 떨어져 지나간 요소를 지운다.
    /// </summary>
    [Fact]
    public void LostMouseUp_EraserThenSelectDrag_DoesNotErase()
    {
        RunSta(() =>
        {
            var h = new Harness();
            var a = Stroke(400, 400, 50, 50);
            var b = Stroke(600, 600, 50, 50);
            h.Document.Add(a);
            h.Document.Add(b);
            h.State.ActiveTool = ToolKind.Eraser;
            h.Controller.PointerDown(new Point(100, 100), shift: false); // 빈 곳 — 지운 것 없이 걸쇠만 선다

            // 버튼 업 유실: 업 없이 도구가 바뀌고 다음 누름이 온다.
            h.State.ActiveTool = ToolKind.Select;
            h.Controller.PointerDown(new Point(425, 425), shift: false);
            h.Controller.PointerMove(new Point(625, 625), shift: false, leftPressed: true);

            Assert.Equal([a, b], h.Document.Elements);
            Assert.Equal(0, h.Ledger.Count);
            Assert.Equal(new Vector(200, 200), a.TransformState.Translation); // 지우개가 아니라 이동 분기를 탔다
        });
    }

    /// <summary>
    /// 이동의 업을 잃은 뒤 다른 요소를 클릭하면, 원장에 없는 이동은 <b>롤백</b>되어야 한다 (R15).
    /// 정리가 없으면 새 스냅샷이 옛 스냅샷을 덮어 a가 변위된 채 남고, 실행취소로도 되돌릴 수 없다.
    /// </summary>
    [Fact]
    public void LostMouseUp_MoveThenElementClick_RollsBackUnledgeredTransform()
    {
        RunSta(() =>
        {
            var h = new Harness();
            var a = Stroke(400, 400, 50, 50);
            var b = Stroke(600, 600, 50, 50);
            h.Document.Add(a);
            h.Document.Add(b);
            h.State.ActiveTool = ToolKind.Select;

            h.Controller.PointerDown(new Point(425, 425), shift: false);
            h.Controller.PointerMove(new Point(505, 485), shift: false, leftPressed: true);
            Assert.Equal(new Vector(80, 60), a.TransformState.Translation); // 이동이 실제로 걸려 있었다 (거짓 안심 방지)

            // 버튼 업 유실: 업 없이 b를 누르고 뗀다.
            h.Controller.PointerDown(new Point(625, 625), shift: false);
            h.Controller.PointerUp(new Point(625, 625), shift: false);

            Assert.Equal(ElementTransformState.Identity, a.TransformState);
            Assert.Equal(ElementTransformState.Identity, b.TransformState);
            Assert.Equal([b], h.Selection.Elements);
            Assert.Empty(h.Commits);
        });
    }

    /// <summary>
    /// 그룹 회전의 업을 잃은 뒤 Shift+요소 토글은 이동을 시작하지 않는다 (SEL-AC-3) — 옛 회전도 되살리면 안 된다.
    /// 토글 분기는 <c>_dragKind</c>를 갱신하지 않고 돌아가므로, 정리가 없으면 옛 GroupRotate가
    /// 새 누름 지점을 기준으로 이어져 a·b가 다시 돌고 가이드 프레임이 밀린다.
    /// </summary>
    [Fact]
    public void LostMouseUp_GroupRotateThenShiftToggle_DoesNotResumeRotation()
    {
        RunSta(() =>
        {
            var h = new Harness();
            var a = Stroke(400, 400, 50, 50);
            var b = Stroke(600, 600, 50, 50);
            var c = Stroke(1000, 200, 50, 50);
            h.Document.Add(a);
            h.Document.Add(b);
            h.Document.Add(c);
            h.Selection.Set([a, b]);
            h.State.ActiveTool = ToolKind.Select;
            var frame = SelectionGroup.Frame([a, b])!.Value;

            h.Controller.PointerDown(SelectionGroup.RotateHandle(frame), shift: false);
            h.Controller.PointerMove(new Point(frame.Right + 120, frame.Top + 40), shift: false, leftPressed: true);
            Assert.NotEqual(ElementTransformState.Identity, a.TransformState);

            // 버튼 업 유실: 업 없이 c를 Shift+누르고 끈다.
            h.Controller.PointerDown(new Point(1025, 225), shift: true);
            h.Controller.PointerMove(new Point(1100, 300), shift: false, leftPressed: true);

            Assert.Equal([a, b, c], h.Selection.Elements); // 토글은 됐다
            Assert.Equal(ElementTransformState.Identity, a.TransformState);
            Assert.Equal(ElementTransformState.Identity, b.TransformState);
            Assert.Null(h.FramePushes[^1]);
            Assert.Empty(h.Commits);
        });
    }

    /// <summary>
    /// 획의 업을 잃은 뒤 새 획을 시작하면 미리보기는 <b>하나</b>여야 한다. 정리가 없으면 옛 Path가 참조만 잃고
    /// 캔버스에 남는다 — 문서에 없는 시각물이라 지우개·전체 지우기로도 사라지지 않는다.
    /// </summary>
    [Fact]
    public void LostMouseUp_StrokeThenNewStroke_LeavesSinglePreview()
    {
        RunSta(() =>
        {
            var h = new Harness();
            h.State.ActiveTool = ToolKind.Pen;
            h.Controller.PointerDown(new Point(100, 100), shift: false);
            h.Controller.PointerMove(new Point(180, 160), shift: false, leftPressed: true);

            // 버튼 업 유실: 업 없이 다음 획이 시작된다.
            h.Controller.PointerDown(new Point(300, 300), shift: false);

            Assert.Single(h.Canvas.Children.OfType<Shape>());
            Assert.Empty(h.Document.Elements);
            Assert.Equal(0, h.Ledger.Count);
        });
    }

    /// <summary>
    /// 획의 업을 잃은 뒤 선택 도구로 요소를 끌면 그 요소가 움직여야 한다. 정리가 없으면 그리기 분기가 이동을
    /// 선점해 옛 획에 점을 잇고, 업에서 그 획을 문서에 커밋해 버린다 — 선택 이동은 한 번도 돌지 않는다.
    /// </summary>
    [Fact]
    public void LostMouseUp_StrokeThenSelectDrag_MovesElementNotStroke()
    {
        RunSta(() =>
        {
            var h = new Harness();
            var a = Stroke(400, 400, 50, 50);
            h.Document.Add(a);
            h.State.ActiveTool = ToolKind.Pen;
            h.Controller.PointerDown(new Point(100, 100), shift: false);
            h.Controller.PointerMove(new Point(180, 160), shift: false, leftPressed: true);

            // 버튼 업 유실: 업 없이 도구가 바뀌고 a를 끈다.
            h.State.ActiveTool = ToolKind.Select;
            h.Controller.PointerDown(new Point(425, 425), shift: false);
            h.Controller.PointerMove(new Point(505, 485), shift: false, leftPressed: true);
            h.Controller.PointerUp(new Point(505, 485), shift: false);

            Assert.Equal([a], h.Document.Elements);
            Assert.Empty(h.Canvas.Children.OfType<Shape>());
            var delta = Assert.Single(Assert.Single(h.Commits).Deltas);
            Assert.Same(a, delta.Element);
            Assert.Equal(new Vector(80, 60), delta.After.Translation);
        });
    }

    // ---- 업 유실과 새 누름 사이의 원장 변경은 낡은 스냅샷에 지지 않는다 (96단계, FINAL-REVIEW-SETTLE-LEDGER) ----
    //
    // 업을 잃은 드래그의 스냅샷은 다음 누름(SettleOrphanedPress)이나 다음 취소(CancelActiveInput)까지 살아 있다.
    // 그 사이 원장 진입점(실행취소·다른 서피스의 변형 확정·이관 등)이 요소 상태를 바꿨다면 그 값이 원장의 진실이다 —
    // 롤백이 드래그 시작 상태로 덮어쓰면 방금 되돌린 실행취소가 화면에서 조용히 무효가 되고, 원장과 화면이 어긋난다.

    /// <summary>
    /// 이동을 한 번 확정한 뒤 두 번째 이동의 업을 잃고 실행취소하면, 요소는 <b>실행취소 결과</b>(첫 이동 전)에 있어야 한다.
    /// 새 누름의 정리가 낡은 스냅샷(첫 이동 뒤)으로 롤백하면 실행취소가 화면에서 되돌려지고 원장은 이미 비어 있어
    /// 다시 실행취소할 수도 없다.
    /// </summary>
    [Fact]
    public void LostMouseUp_MoveThenUndoThenNewPress_KeepsUndoneState()
    {
        RunSta(() =>
        {
            var h = new LedgerHarness();
            var a = Stroke(400, 400, 50, 50);
            h.Document.Add(a);
            h.State.ActiveTool = ToolKind.Select;
            h.DragWithLostUp(a);

            Assert.True(h.Commands.Undo()); // 원장 진입점 — 첫 이동을 되돌린다
            Assert.Equal(ElementTransformState.Identity, a.TransformState);

            h.Controller.PointerDown(new Point(1200, 900), shift: false); // 빈 곳 새 누름 → SettleOrphanedPress

            Assert.Equal(ElementTransformState.Identity, a.TransformState);
            Assert.Equal(0, h.Ledger.Count);
            Assert.Single(h.Commits); // 롤백도 정리도 원장에 싣지 않는다
        });
    }

    /// <summary>
    /// 같은 시나리오에서 새 누름 대신 취소(ESC·클릭 통과 전환 → <c>CancelActiveInput</c>)가 와도 실행취소 결과가 남아야 한다 —
    /// 두 정리는 합치지 않지만 롤백은 같은 <c>DragBaseStates.RollbackAll</c> 하나를 쓴다.
    /// </summary>
    [Fact]
    public void LostMouseUp_MoveThenUndoThenCancel_KeepsUndoneState()
    {
        RunSta(() =>
        {
            var h = new LedgerHarness();
            var a = Stroke(400, 400, 50, 50);
            h.Document.Add(a);
            h.State.ActiveTool = ToolKind.Select;
            h.DragWithLostUp(a);

            Assert.True(h.Commands.Undo());

            h.Controller.CancelActiveInput();

            Assert.Equal(ElementTransformState.Identity, a.TransformState);
            Assert.Equal(0, h.Ledger.Count);
        });
    }

    /// <summary>
    /// 원장 진입점이 건드리지 않은 요소는 여전히 롤백된다 — 업 유실 정리 자체(91단계)는 그대로여야 한다.
    /// a·b를 함께 끌다가 업을 잃고, 그 사이 a만 바꾸는 원장 항목을 실행취소하면 a는 실행취소 결과, b는 시작 상태다.
    /// </summary>
    [Fact]
    public void LostMouseUp_GroupMoveThenUndoOfOneMember_RollsBackOnlyUntouchedMember()
    {
        RunSta(() =>
        {
            var h = new LedgerHarness();
            var a = Stroke(400, 400, 50, 50);
            var b = Stroke(600, 600, 50, 50);
            h.Document.Add(a);
            h.Document.Add(b);
            h.State.ActiveTool = ToolKind.Select;
            // a만 (80,60) 옮겨 확정한다 (원장 1건).
            h.Controller.PointerDown(new Point(425, 425), shift: false);
            h.Controller.PointerMove(new Point(505, 485), shift: false, leftPressed: true);
            h.Controller.PointerUp(new Point(505, 485), shift: false);
            // Shift+b 토글로 [a, b]를 만든다 (토글은 이동을 시작하지 않는다, SEL-AC-3).
            h.Controller.PointerDown(new Point(625, 625), shift: true);
            h.Controller.PointerUp(new Point(625, 625), shift: false);
            Assert.Equal([a, b], h.Selection.Elements);

            // a를 잡고 그룹을 (40,40) 끌다가 업을 잃는다.
            h.Controller.PointerDown(new Point(505, 485), shift: false);
            h.Controller.PointerMove(new Point(545, 525), shift: false, leftPressed: true);
            Assert.Equal(new Vector(40, 40), b.TransformState.Translation); // b도 실제로 끌려 있었다 (거짓 안심 방지)

            Assert.True(h.Commands.Undo()); // a의 첫 이동만 되돌린다 — b는 원장 연산이 건드리지 않았다

            h.Controller.PointerDown(new Point(1200, 900), shift: false);

            Assert.Equal(ElementTransformState.Identity, a.TransformState); // 실행취소 결과가 남는다
            Assert.Equal(ElementTransformState.Identity, b.TransformState); // 원장에 없는 끌기는 롤백된다
            Assert.Single(h.Commits);
        });
    }

    /// <summary>캔버스를 실제 크기로 측정한다 — 핸들·회전 판정이 살아 있어야 취소 순서를 관측할 수 있다.</summary>
    private sealed class Harness()
        : SurfaceHarness(new SurfaceHarnessOptions { Layout = new Size(SurfaceWidth, SurfaceHeight) });

    /// <summary>
    /// 커밋을 원장에도 싣고 프로덕션 원장 진입점(<see cref="LedgerCommands"/>)을 붙인 변종 (96단계) —
    /// <c>flushPendingTransforms</c>는 프로덕션 팬아웃처럼 이 서피스의 <c>FlushPendingTransforms</c>다.
    /// </summary>
    private sealed class LedgerHarness : SurfaceHarness
    {
        public LedgerHarness()
            : base(new SurfaceHarnessOptions { Layout = new Size(SurfaceWidth, SurfaceHeight), CommitToLedger = true })
        {
            Commands = new LedgerCommands(
                State, Selection, Ledger,
                documents: () => [Document],
                ownerOf: element => Document.Elements.Contains(element) ? Document : null,
                flushPendingTransforms: Controller.FlushPendingTransforms,
                transferSurfaces: () => [],
                closePins: () => 0);
        }

        public LedgerCommands Commands { get; }

        /// <summary>
        /// (400,400) 50×50 획을 (80,60) 옮겨 <b>확정</b>(원장 1건)한 뒤, 다시 (40,40) 끌다가 업을 잃는다.
        /// 끝나면 요소는 (120,100)에 있고, 살아남은 스냅샷의 시작 상태는 (80,60)이다.
        /// </summary>
        public void DragWithLostUp(AnnotationElement element)
        {
            Controller.PointerDown(new Point(425, 425), shift: false);
            Controller.PointerMove(new Point(505, 485), shift: false, leftPressed: true);
            Controller.PointerUp(new Point(505, 485), shift: false);
            Assert.Equal(1, Ledger.Count);

            Controller.PointerDown(new Point(505, 485), shift: false);
            Controller.PointerMove(new Point(545, 525), shift: false, leftPressed: true);
            Assert.Equal(new Vector(120, 100), element.TransformState.Translation); // 두 번째 이동이 실제로 걸려 있었다
        }
    }
}
