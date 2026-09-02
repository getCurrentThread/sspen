using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

using static SSPen.Tests.StaThread;
namespace SSPen.Tests;

/// <summary>
/// 휠 확대/축소 정책 전체의 증인 (R7, f3/SEL-12, f7/SEL-14, D5, SEL-LIM-6).
///
/// <see cref="WheelScaleController"/>는 시계와 유휴 스케줄러를 주입받는 순수 협력자라 WPF 비주얼
/// 없이 검증된다 — 옮기기 전에는 이 정책이 <c>SurfaceInputController</c>의 <c>DispatcherTimer</c>에
/// 묶여 있어 헤드리스 증인을 쓸 수 없었다.
///
/// 마지막 두 테스트만 컨트롤러 수준(STA)이다: "확대 → 지우기" 원장 순서와 취소 경로의 확정은
/// 컨트롤러 배선이 있어야 관측된다.
/// </summary>
public class WheelScaleControllerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static StrokeElement NewStroke(double x, double y) =>
        new([new Point(x, y), new Point(x + 20, y + 20)], Colors.Black, 3, isHighlighter: false);

    /// <summary>테스트가 직접 밀어 주는 시계.</summary>
    private sealed class Clock
    {
        public DateTime Now { get; private set; } = T0;

        public void Advance(TimeSpan span) => Now += span;
    }

    /// <summary>
    /// 컨트롤러 1대 + 그 협력자들. <paramref name="documents"/>의 첫 문서가 폴백이다.
    /// <c>applyTransformState</c>는 프로덕션과 <b>같이</b> 단 하나의 <see cref="DragBaseStates"/>
    /// 인스턴스의 <c>Apply</c> 메서드 그룹을 넘긴다 (R15 유일 집행 지점).
    /// </summary>
    private sealed class Rig
    {
        public Rig(params AnnotationDocument[] documents)
        {
            Documents = documents.Length > 0 ? documents : [new AnnotationDocument("test")];
            var enforcer = new DragBaseStates(OwnerLookup, Documents[0]);
            Controller = new WheelScaleController(
                OwnerLookup, Documents[0], enforcer.Apply,
                (deltas, drop) => Commits.Add((deltas, drop)),
                () => Clock.Now, Idle);
        }

        public AnnotationDocument[] Documents { get; }

        public Clock Clock { get; } = new();

        public FakeIdleScheduler Idle { get; } = new();

        public WheelScaleController Controller { get; }

        public List<(IReadOnlyList<TransformDelta> Deltas, Point? Drop)> Commits { get; } = [];

        public AnnotationDocument? OwnerLookup(AnnotationElement element) =>
            Documents.FirstOrDefault(d => d.Elements.Contains(element));
    }

    /// <summary>
    /// 연속 노치 10회는 원장 <b>1항목</b>이다 (f3/SEL-12). 노치마다 실었다면 여기서 10이 나온다.
    /// 확정은 유휴 만료 시점에만 일어나므로, 노치 도중에는 커밋이 하나도 없어야 한다.
    /// </summary>
    [Fact]
    public void Step_TenNotches_ProducesOneCommit()
    {
        var rig = new Rig();
        var a = NewStroke(100, 100);
        rig.Documents[0].Add(a);

        for (int i = 0; i < 10; i++)
        {
            rig.Controller.Step([a], new Point(110, 110), notches: +1, dragActive: false);
            rig.Clock.Advance(TimeSpan.FromMilliseconds(30));
        }
        Assert.Empty(rig.Commits);
        Assert.True(rig.Controller.Active);

        rig.Clock.Advance(WheelScaleSession.IdleTimeout);
        rig.Idle.Fire();

        var (deltas, drop) = Assert.Single(rig.Commits);
        Assert.Single(deltas);
        Assert.Null(drop); // f7/SEL-14: 휠은 아무것도 "놓지" 않는다
        Assert.False(rig.Controller.Active);
        Assert.Equal(Math.Pow(WheelScaleSession.NotchFactor, 10), a.TransformState.ScaleX, 9);
    }

    /// <summary>
    /// 드래그 중 휠은 무동작이다 (R7 (a)). 두 세션이 같은 요소를 잡으면 시작 상태 스냅샷이 둘로
    /// 갈라져 한 번의 드래그가 실행취소 2번이 된다 — 그중 하나는 아무 일도 하지 않는 유령 스텝이다.
    /// 세션이 열리지 않았다는 것까지 확인한다 (열리면 뒤늦은 유휴 만료가 유령 항목을 싣는다).
    /// </summary>
    [Fact]
    public void Step_WhileDragActive_IsNoOp()
    {
        var rig = new Rig();
        var a = NewStroke(100, 100);
        rig.Documents[0].Add(a);
        var before = a.TransformState;

        rig.Controller.Step([a], new Point(110, 110), notches: +1, dragActive: true);

        Assert.False(rig.Controller.Active);
        Assert.Equal(before, a.TransformState);
        Assert.Equal(0, rig.Idle.RestartCount);
        Assert.Empty(rig.Commits);
    }

    /// <summary>
    /// 클램프된 배율을 세션에 되먹이지 않으면 천장 위에 데드존이 생긴다 (R7 (b), D5).
    /// 60노치를 굴리면 <c>1.1^60 ≈ 304</c>지만 화면에 적용되는 값은 <c>MaxScale = 100</c>이다.
    /// 되먹이면 첫 역방향 노치에서 <c>100 / 1.1</c>로 즉시 반응하고, 안 되먹이면 304/1.1 = 277이
    /// 여전히 100으로 잘려 <b>아무 일도 일어나지 않는다</b>.
    /// </summary>
    [Fact]
    public void Step_ClampedFactorIsFedBack_FirstReverseNotchReacts()
    {
        var rig = new Rig();
        var a = NewStroke(100, 100);
        rig.Documents[0].Add(a);

        rig.Controller.Step([a], new Point(110, 110), notches: +60, dragActive: false);
        Assert.Equal(TransformMath.MaxScale, a.TransformState.ScaleX, 9);

        rig.Controller.Step([a], new Point(110, 110), notches: -1, dragActive: false);

        Assert.Equal(TransformMath.MaxScale / WheelScaleSession.NotchFactor, a.TransformState.ScaleX, 9);
    }

    /// <summary>
    /// 세션이 잡은 요소 리스트가 이 타입의 존재 이유다 (R7). 확정은 유휴 타이머로 비동기 발생하므로
    /// 그 사이 ESC나 클릭 통과 전환으로 선택이 비어도 요소를 되찾을 수 있어야 한다 —
    /// 못 되찾으면 <b>화면에는 커진 채 원장에는 없는</b> 변형이 남아 실행취소로 지울 수 없다.
    ///
    /// 관측: 넘긴 리스트를 <b>비운 뒤</b> 마감해도 커밋이 나온다. 구조 트립와이어도 함께 건다 —
    /// 생성자가 <c>SelectionModel</c>을 받기 시작하면 선택집합 재조회의 문이 다시 열린다.
    /// </summary>
    [Fact]
    public void Flush_AfterSelectionCleared_StillCommitsHeldElements()
    {
        var rig = new Rig();
        var a = NewStroke(100, 100);
        rig.Documents[0].Add(a);
        var owned = new List<AnnotationElement> { a };

        rig.Controller.Step(owned, new Point(110, 110), notches: +3, dragActive: false);
        owned.Clear(); // 선택이 비었다 (ESC / 클릭 통과 전환)

        rig.Controller.Flush(commit: true);

        var (deltas, _) = Assert.Single(rig.Commits);
        Assert.Same(a, Assert.Single(deltas).Element);

        Assert.DoesNotContain(
            typeof(WheelScaleController).GetConstructors().Single().GetParameters(),
            p => p.ParameterType == typeof(SelectionModel));
    }

    /// <summary>
    /// 실제로 바뀐 요소가 없으면 원장 항목을 만들지 않는다 (f3) — 빈 실행취소 항목은
    /// 실행취소 1회를 통째로 삼킨다. 노치 0회는 배율 1이라 상태가 그대로다.
    /// </summary>
    [Fact]
    public void Flush_NoChangedElement_EmitsNoCommit()
    {
        var rig = new Rig();
        var a = NewStroke(100, 100);
        rig.Documents[0].Add(a);
        var before = a.TransformState;

        rig.Controller.Step([a], new Point(110, 110), notches: 0, dragActive: false);
        Assert.True(rig.Controller.Active);
        Assert.Equal(before, a.TransformState);

        rig.Controller.Flush(commit: true);

        Assert.Empty(rig.Commits);
        Assert.False(rig.Controller.Active);
    }

    /// <summary>
    /// 한 박자 늦게 도착한 틱은 무해해야 한다 (R7). 마지막 노치 이후 <see cref="WheelScaleSession.IdleTimeout"/>이
    /// 지나지 않았으면 확정하지 않고 다음 만료를 기다린다 — <c>DueToCommit</c> 재확인을 지우면
    /// 아직 굴리는 중인 휠이 중간에 원장 항목으로 잘려 한 제스처가 실행취소 2번이 된다.
    /// </summary>
    [Fact]
    public void Idle_NotYetDue_DoesNotCommit()
    {
        var rig = new Rig();
        var a = NewStroke(100, 100);
        rig.Documents[0].Add(a);

        rig.Controller.Step([a], new Point(110, 110), notches: +1, dragActive: false);
        rig.Clock.Advance(TimeSpan.FromMilliseconds(100));
        rig.Idle.Fire();

        Assert.Empty(rig.Commits);
        Assert.True(rig.Controller.Active);
    }

    /// <summary>
    /// 유휴 만료 뒤에는 정확히 한 번만 확정한다. 노치마다 구독이 쌓이면(<c>-=</c> 없이 <c>+=</c>만)
    /// 만료 한 번이 <see cref="WheelScaleController.Flush"/>를 노치 수만큼 부른다 — 구독자 수와
    /// 커밋 수를 함께 본다. 두 번째 만료는 세션이 이미 닫혀 무동작이어야 한다.
    /// </summary>
    [Fact]
    public void Idle_AfterDue_CommitsExactlyOnce()
    {
        var rig = new Rig();
        var a = NewStroke(100, 100);
        rig.Documents[0].Add(a);

        for (int i = 0; i < 5; i++)
        {
            rig.Controller.Step([a], new Point(110, 110), notches: +1, dragActive: false);
        }
        Assert.Equal(1, rig.Idle.SubscriberCount);
        Assert.Equal(5, rig.Idle.RestartCount);
        Assert.Equal(WheelScaleSession.IdleTimeout, rig.Idle.LastInterval);

        rig.Clock.Advance(WheelScaleSession.IdleTimeout);
        rig.Idle.Fire();
        rig.Idle.Fire();

        Assert.Single(rig.Commits);
    }

    /// <summary>
    /// 고정점은 세션 시작 시점에 <b>동결</b>된다 (R7). 노치마다 다시 계산하면 커서가 흔들릴 때
    /// 선택이 표류한다. 관측: 두 번째 노치를 전혀 다른 커서 위치에서 굴려도 결과가 첫 커서를
    /// 고정점으로 삼은 <see cref="TransformMath.ScaleAbout"/>와 일치한다.
    /// </summary>
    [Fact]
    public void Pivot_FrozenAcrossNotches()
    {
        var rig = new Rig();
        var a = NewStroke(100, 100);
        rig.Documents[0].Add(a);
        var start = a.TransformState;
        var firstCursor = new Point(105, 105); // 프레임 안 → 고정점은 커서 (하이브리드 규칙)

        rig.Controller.Step([a], firstCursor, notches: +1, dragActive: false);
        rig.Controller.Step([a], new Point(900, 900), notches: +1, dragActive: false);

        double factor = Math.Pow(WheelScaleSession.NotchFactor, 2);
        var expected = TransformMath.ScaleAbout(start, a.LocalBounds, firstCursor, factor);
        Assert.Equal(expected.Translation.X, a.TransformState.Translation.X, 9);
        Assert.Equal(expected.Translation.Y, a.TransformState.Translation.Y, 9);
    }

    /// <summary>
    /// 델타는 요소마다 <b>자기 소유 문서</b>를 든다 (다중 모니터 선택). 폴백 문서로 뭉뚱그리면
    /// 실행취소가 다른 모니터의 요소를 이 문서에 되돌려 놓는다.
    /// </summary>
    [Fact]
    public void Flush_MixedOwners_UsesOwnerLookupPerElement()
    {
        var left = new AnnotationDocument("left");
        var right = new AnnotationDocument("right");
        var rig = new Rig(left, right);
        var a = NewStroke(100, 100);
        var b = NewStroke(140, 140);
        left.Add(a);
        right.Add(b);

        rig.Controller.Step([a, b], new Point(120, 120), notches: +2, dragActive: false);
        rig.Controller.Flush(commit: true);

        var (deltas, _) = Assert.Single(rig.Commits);
        Assert.Equal(2, deltas.Count);
        Assert.Same(left, deltas[0].BeforeOwner);
        Assert.Same(left, deltas[0].AfterOwner);
        Assert.Same(right, deltas[1].BeforeOwner);
        Assert.Same(right, deltas[1].AfterOwner);
    }

    /// <summary>
    /// 요소를 없애기 전에 확대를 먼저 확정한다 (R7 (c)). 안 그러면 유휴 타이머가 뒤늦게 깨어나
    /// <b>이미 지워진</b> 요소의 변형을 지우기 항목 뒤에 실어 실행취소 1회가 무동작이 된다.
    ///
    /// 관측 방법: 원장을 프로덕션과 같이 배선해(커밋 → <c>RecordTransform</c>) 두 항목의 순서를
    /// 실행취소로 되짚는다. 첫 실행취소가 <b>지우기</b>를 되돌리고(요소가 돌아오되 여전히 커져 있다),
    /// 두 번째가 <b>확대</b>를 되돌린다. 순서가 뒤집혔다면 첫 실행취소가 아무 일도 하지 않는다.
    /// </summary>
    [Fact]
    public void Flush_BeforeErase_OrdersScaleAheadOfDelete()
    {
        RunSta(() =>
        {
            var h = new ControllerRig();
            var a = NewStroke(100, 100);
            h.Document.Add(a);
            h.State.ActiveTool = ToolKind.Select;
            h.Selection.Set([a]);

            Assert.True(h.Controller.Wheel(new Point(110, 110), notches: +3));
            double scaled = a.TransformState.ScaleX;
            Assert.True(scaled > 1);

            // 지우개로 같은 요소를 지운다 — EraseAt 머리에서 휠이 먼저 확정돼야 한다.
            h.State.ActiveTool = ToolKind.Eraser;
            h.Controller.PointerDown(new Point(110, 110), shift: false);
            Assert.DoesNotContain(a, h.Document.Elements);
            Assert.Equal(2, h.Ledger.Count);

            Assert.True(h.Ledger.Undo());
            Assert.Contains(a, h.Document.Elements);
            Assert.Equal(scaled, a.TransformState.ScaleX, 9);

            Assert.True(h.Ledger.Undo());
            Assert.Equal(1, a.TransformState.ScaleX, 9);
        });
    }

    /// <summary>
    /// 입력 취소(비인터랙티브 전환·캡처 세션·서피스 숨김)는 진행 중인 휠 세션을 확정한다 (R7).
    /// 방치하면 원장에 없는 변형이 화면에 남아 실행취소로 지울 수 없고, 확정을 두 번 하면
    /// 한 제스처가 실행취소 2번이 된다 — 그래서 <b>정확히 1항목</b>이다.
    /// </summary>
    [Fact]
    public void Cancel_WithPendingWheelSessionAndNoDrag_CommitsExactlyOneLedgerEntry()
    {
        RunSta(() =>
        {
            var h = new ControllerRig();
            var a = NewStroke(100, 100);
            h.Document.Add(a);
            h.State.ActiveTool = ToolKind.Select;
            h.Selection.Set([a]);

            Assert.True(h.Controller.Wheel(new Point(110, 110), notches: +3));
            Assert.Equal(0, h.Ledger.Count);

            h.Controller.CancelActiveInput();

            Assert.Equal(1, h.Ledger.Count);
            Assert.True(a.TransformState.ScaleX > 1); // 화면의 결과가 그대로 남고
            Assert.True(h.Ledger.Undo());             // 실행취소로 지울 수 있다
            Assert.Equal(1, a.TransformState.ScaleX, 9);

            h.Controller.CancelActiveInput();
            Assert.Equal(0, h.Ledger.Count);
        });
    }

    /// <summary>
    /// <see cref="SurfaceInputController"/> 1대. 커밋을 <b>프로덕션과 같이</b> 원장에 흘려
    /// (<c>AppController.CommitTransform</c>의 <c>RecordTransform</c> 자리) 항목 순서를 관측한다.
    /// 이관 판정은 재현하지 않는다 — 서피스가 하나뿐이라 항상 제자리다.
    /// </summary>
    private sealed class ControllerRig : ISurfaceHost
    {
        public ControllerRig()
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
                (deltas, _) => Ledger.RecordTransform(deltas),
                () => { },
                new SurfaceInputSeams
                {
                    SurfaceBounds = () => new Rect(0, 0, 1920, 1080),
                    IdleScheduler = Idle,
                });
        }

        public Canvas Canvas { get; }

        public AppState State { get; }

        public AnnotationDocument Document { get; }

        public SelectionModel Selection { get; }

        public UndoLedger Ledger { get; }

        public FadingInkController Fading { get; }

        public FakeIdleScheduler Idle { get; } = new();

        public SurfaceInputController Controller { get; }

        private AnnotationDocument? OwnerLookup(AnnotationElement element) =>
            Document.Elements.Contains(element) ? Document : null;

        public void SetNoActivate(bool on) { }

        public void ActivateWindow() { }

        public void CaptureMouse() { }

        public void ReleaseMouseCapture() { }

        public DpiScale GetDpi() => new(1.0, 1.0);
    }
}
