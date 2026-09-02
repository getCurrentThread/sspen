using System.Reflection;
using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 드래그 시작 스냅샷 + R15 집행자 검증 (R15, R5, R7, SEL-B-4, f3/SEL-12).
///
/// 핵심 증인은 <b>선택집합을 비운 뒤에도 롤백이 성립한다</b>는 것이다. 예전 구현은 id만 들고
/// 롤백 때 선택집합·핸들 대상에서 요소를 되찾았고, ESC 경로(선택을 먼저 비우고
/// <c>CancelActiveInput</c>을 부른다)에서 이동·그룹 스케일·그룹 회전의 롤백이 통째로
/// 무동작이 되어 <b>화면에는 변형된 채 원장에는 없는</b> 변형이 남았다.
///
/// 컨트롤러 수준 end-to-end 증인은 9단계(a3046fd)가 <c>Point</c> 진입점을 분리한 뒤
/// <c>SurfaceCancelOrderTests.LostMouseUp_ThenNewPress_CancelStillRollsBackInFlightTransform</c>
/// 으로 붙었다 (8단계가 예약했던 이름 <c>Cancel_MidGroupRotateWithClearedSelection_…</c>은
/// 도착하지 않았다 — 링크는 실제 증인 이름을 가리켜야 한다). 아래 타입 수준 증인 +
/// 리플렉션 트립와이어는 그 위에 남는 방어선이다.
/// </summary>
public class DragBaseStatesTests
{
    private static StrokeElement NewStroke() =>
        new([new Point(0, 0), new Point(10, 10)], Colors.Black, 3, isHighlighter: false);

    private static Func<AnnotationElement, AnnotationDocument?> LookupIn(params AnnotationDocument[] documents) =>
        element => documents.FirstOrDefault(d => d.Elements.Contains(element));

    private static ElementTransformState Moved(double dx, double dy) =>
        ElementTransformState.Identity with { Translation = new Vector(dx, dy) };

    // ---- 확정 결함(ESC 중 롤백 무동작)의 회귀 증인 ----

    /// <summary>
    /// 드래그 중 선택이 비어도 롤백은 전원을 시작 상태로 되돌린다 (R5/SEL-B-4 경로).
    /// id로만 되찾던 옛 구현에서는 이 테스트가 빨갛다 — 되찾을 곳이 사라지기 때문이다.
    /// </summary>
    [Fact]
    public void RollbackAll_AfterSelectionCleared_RestoresEveryElement()
    {
        var doc = new AnnotationDocument("M1");
        var a = NewStroke();
        var b = NewStroke();
        var c = NewStroke();
        doc.Add(a);
        doc.Add(b);
        doc.Add(c);
        var selection = new SelectionModel();
        selection.Set([a, b, c]);

        var states = new DragBaseStates(LookupIn(doc), doc);
        states.Snapshot(selection, handleTarget: null); // 이동/그룹 경로는 핸들 대상이 없다.

        // 드래그가 진행된 상태
        states.Apply(a, Moved(40, 0));
        states.Apply(b, Moved(40, 0));
        states.Apply(c, Moved(40, 0));

        // ESC → EngageClickThrough가 선택을 먼저 비운다.
        selection.Clear();

        states.RollbackAll();

        Assert.Equal(ElementTransformState.Identity, a.TransformState);
        Assert.Equal(ElementTransformState.Identity, b.TransformState);
        Assert.Equal(ElementTransformState.Identity, c.TransformState);
    }

    /// <summary>
    /// 롤백도 R15 알림을 동반해야 한다 — 상태만 되돌리고 알리지 않으면 화면은 변형된 채로 멈춘다
    /// (AnnotationDocument.ElementTransformChanged가 경고한 무증상 결함).
    /// </summary>
    [Fact]
    public void RollbackAll_AfterSelectionCleared_RaisesTransformChangedForEveryElement()
    {
        var doc = new AnnotationDocument("M1");
        var a = NewStroke();
        var b = NewStroke();
        doc.Add(a);
        doc.Add(b);
        var selection = new SelectionModel();
        selection.Set([a, b]);

        var states = new DragBaseStates(LookupIn(doc), doc);
        states.Snapshot(selection, handleTarget: null);
        states.Apply(a, Moved(5, 5));
        states.Apply(b, Moved(5, 5));
        selection.Clear();

        var notified = new List<AnnotationElement>();
        doc.ElementTransformChanged += notified.Add;

        states.RollbackAll();

        Assert.Equal([a, b], notified);
    }

    /// <summary>
    /// 트립와이어: 롤백이 선택집합이나 핸들 대상을 <b>다시 받으면</b> 위 결함이 그대로 돌아온다.
    /// <c>RollbackAll(SelectionModel, ...)</c> 오버로드를 붙이는 순간 빨개진다.
    /// </summary>
    [Fact]
    public void RollbackAll_TakesNoSelectionArgument_ByReflection()
    {
        var overloads = typeof(DragBaseStates)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name is "RollbackAll")
            .ToList();

        var only = Assert.Single(overloads);
        Assert.Empty(only.GetParameters());

        // Pairs도 같은 이유로 인자를 받지 않는다 (프로퍼티여야 하고, 해석 콜백 메서드가 없어야 한다).
        Assert.NotNull(typeof(DragBaseStates).GetProperty("Pairs", BindingFlags.Public | BindingFlags.Instance));
        var methodNames = typeof(DragBaseStates)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToList();
        Assert.DoesNotContain("Pairs", methodNames);
        Assert.DoesNotContain("Resolve", methodNames);
        Assert.DoesNotContain("FindDragged", methodNames);
    }

    /// <summary>커밋 경로도 선택집합을 경유하지 않는다 — 비운 뒤에도 held 쌍이 그대로 나온다 (f3 필터의 입력).</summary>
    [Fact]
    public void Pairs_AfterSelectionCleared_StillEnumeratesHeldElements()
    {
        var doc = new AnnotationDocument("M1");
        var a = NewStroke();
        var b = NewStroke();
        doc.Add(a);
        doc.Add(b);
        var selection = new SelectionModel();
        selection.Set([a, b]);

        var states = new DragBaseStates(LookupIn(doc), doc);
        states.Snapshot(selection, handleTarget: null);
        states.Apply(a, Moved(7, 0));
        selection.Clear();

        var pairs = states.Pairs.ToList();

        Assert.Equal([a, b], pairs.Select(p => p.Element));
        Assert.All(pairs, p => Assert.Equal(ElementTransformState.Identity, p.Before));

        // 같은 쌍으로 원장 델타가 만들어진다 — 선택이 비었어도 원장 기록이 성립한다.
        var deltas = TransformCommitPlan.Build(states.Pairs, LookupIn(doc), doc);
        var only = Assert.Single(deltas);
        Assert.Same(a, only.Element);
    }

    // ---- 스냅샷/롤백 기본 계약 ----

    /// <summary>롤백은 요소마다 <b>자기</b> 시작 상태로 돌아간다 (공통값 하나로 뭉개지 않는다).</summary>
    [Fact]
    public void RollbackAll_RestoresEveryBaseState()
    {
        var doc = new AnnotationDocument("M1");
        var a = NewStroke();
        var b = NewStroke();
        doc.Add(a);
        doc.Add(b);
        a.TransformState = Moved(1, 2);
        b.TransformState = ElementTransformState.Identity with { AngleDegrees = 30 };
        var selection = new SelectionModel();
        selection.Set([a, b]);

        var states = new DragBaseStates(LookupIn(doc), doc);
        states.Snapshot(selection, handleTarget: null);
        states.Apply(a, Moved(99, 99));
        states.Apply(b, Moved(99, 99));

        states.RollbackAll();

        Assert.Equal(Moved(1, 2), a.TransformState);
        Assert.Equal(ElementTransformState.Identity with { AngleDegrees = 30 }, b.TransformState);
    }

    /// <summary>스냅샷 없이 롤백해도 아무 일도 없어야 한다 — CancelActiveInput은 제스처 없이도 불린다.</summary>
    [Fact]
    public void RollbackAll_WithoutSnapshot_IsNoOp()
    {
        var doc = new AnnotationDocument("M1");
        var a = NewStroke();
        doc.Add(a);
        a.TransformState = Moved(3, 4);

        int raised = 0;
        doc.ElementTransformChanged += _ => raised++;

        var states = new DragBaseStates(LookupIn(doc), doc);
        Assert.False(states.Active);
        states.RollbackAll();

        Assert.Equal(Moved(3, 4), a.TransformState);
        Assert.Equal(0, raised);
        Assert.Empty(states.Pairs);
    }

    // ---- R15: 알림은 소유 문서로 ----

    /// <summary>다중 선택 이동에서 다른 모니터 소속 요소는 <b>그 요소의 소유 문서</b>로 알린다 (R15/D1).</summary>
    [Fact]
    public void Apply_RaisesOnOwningDocument_NotOnFallback()
    {
        var here = new AnnotationDocument("M1");
        var there = new AnnotationDocument("M2");
        var foreign = NewStroke();
        there.Add(foreign);

        var onHere = new List<AnnotationElement>();
        var onThere = new List<AnnotationElement>();
        here.ElementTransformChanged += onHere.Add;
        there.ElementTransformChanged += onThere.Add;

        var states = new DragBaseStates(LookupIn(here, there), here);
        states.Apply(foreign, Moved(10, 0));

        Assert.Empty(onHere);
        Assert.Same(foreign, Assert.Single(onThere));
        Assert.Equal(Moved(10, 0), foreign.TransformState);
    }

    /// <summary>대입 1회당 알림 정확히 1회 — 어느 문서에도 속하지 않으면 제스처가 벌어진 문서로 떨어진다.</summary>
    [Fact]
    public void Apply_RaisesExactlyOncePerWrite()
    {
        var doc = new AnnotationDocument("M1");
        var orphan = NewStroke(); // 어느 문서에도 없다 → fallback 경로

        int raised = 0;
        doc.ElementTransformChanged += _ => raised++;

        var states = new DragBaseStates(LookupIn(doc), doc);
        states.Apply(orphan, Moved(1, 0));
        states.Apply(orphan, Moved(2, 0));

        Assert.Equal(2, raised);
        Assert.Equal(Moved(2, 0), orphan.TransformState);
    }

    // ---- 스냅샷 구성 ----

    /// <summary>핸들 대상은 선택집합에 없을 수 없지만 방어적으로 함께 넣는다.</summary>
    [Fact]
    public void Snapshot_IncludesHandleTargetOutsideSelection()
    {
        var doc = new AnnotationDocument("M1");
        var selected = NewStroke();
        var handle = NewStroke();
        doc.Add(selected);
        doc.Add(handle);
        handle.TransformState = Moved(2, 2);
        var selection = new SelectionModel();
        selection.Set([selected]);

        var states = new DragBaseStates(LookupIn(doc), doc);
        states.Snapshot(selection, handle);

        Assert.Equal([selected, handle], states.Elements);
        Assert.Equal(Moved(2, 2), states.BaseStates![handle.Id]);
    }

    /// <summary>
    /// 핸들 대상이 이미 선택집합에 있으면 쌍이 중복되면 안 된다 — 중복 쌍은 같은 요소의
    /// <see cref="TransformDelta"/>를 원장에 두 번 싣는다 (f3/SEL-12). 단일 선택 크기/회전이 바로 이 경우다.
    /// </summary>
    [Fact]
    public void Snapshot_HandleTargetAlsoInSelection_ProducesNoDuplicatePair()
    {
        var doc = new AnnotationDocument("M1");
        var only = NewStroke();
        doc.Add(only);
        var selection = new SelectionModel();
        selection.Set([only]);

        var states = new DragBaseStates(LookupIn(doc), doc);
        states.Snapshot(selection, only);

        Assert.Same(only, Assert.Single(states.Elements));
        Assert.Single(states.Pairs);

        states.Apply(only, Moved(6, 6));
        Assert.Single(TransformCommitPlan.Build(states.Pairs, LookupIn(doc), doc));
    }

    /// <summary>쌍의 순서는 선택 순서를 그대로 따른다 — 커밋과 롤백이 같은 순서를 봐야 한다.</summary>
    [Fact]
    public void Snapshot_PreservesSelectionOrder()
    {
        var doc = new AnnotationDocument("M1");
        var a = NewStroke();
        var b = NewStroke();
        var c = NewStroke();
        doc.Add(a);
        doc.Add(b);
        doc.Add(c);
        var selection = new SelectionModel();
        selection.Set([c, a, b]);

        var states = new DragBaseStates(LookupIn(doc), doc);
        states.Snapshot(selection, handleTarget: null);

        Assert.Equal([c, a, b], states.Pairs.Select(p => p.Element));
    }

    /// <summary>제스처가 끝나면 스냅샷은 사라진다 — 다음 프레임이 죽은 시작 상태를 읽으면 안 된다 (SEL-7).</summary>
    [Fact]
    public void Reset_ClearsBaseStatesAndPairs()
    {
        var doc = new AnnotationDocument("M1");
        var a = NewStroke();
        doc.Add(a);
        var selection = new SelectionModel();
        selection.Set([a]);

        var states = new DragBaseStates(LookupIn(doc), doc);
        states.Snapshot(selection, handleTarget: null);
        Assert.True(states.Active);

        states.Reset();

        Assert.False(states.Active);
        Assert.Null(states.BaseStates);
        Assert.Empty(states.Elements);
        Assert.Empty(states.Pairs);
    }
}
