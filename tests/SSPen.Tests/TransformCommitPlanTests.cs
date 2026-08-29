using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 변형 확정 계획 순수 규칙 검증 (f3, SEL-12, SEL-14).
/// 여기서 잠그는 세 계약 — 안 바뀐 요소는 원장에 싣지 않는다(f3), 델타마다 자기 소유 문서를
/// 든다(SEL-12), 놓은 지점은 이동일 때만 흐른다(f7/SEL-14) — 은 지금까지 컨트롤러 안의 주석과
/// <see cref="SelectionRedTeamTests"/>의 주석으로만 존재했고 헤드리스 증인이 없었다.
/// 규칙이 <see cref="TransformCommitPlan"/>이라는 순수 타입으로 나오면서 처음으로 직접 검증된다.
/// </summary>
public class TransformCommitPlanTests
{
    private static StrokeElement NewStroke() =>
        new([new Point(0, 0), new Point(10, 10)], Colors.Black, 3, isHighlighter: false);

    private static Func<AnnotationElement, AnnotationDocument?> LookupIn(params AnnotationDocument[] documents) =>
        element => documents.FirstOrDefault(d => d.Elements.Contains(element));

    private static ElementTransformState Moved(double dx, double dy) =>
        ElementTransformState.Identity with { Translation = new Vector(dx, dy) };

    // ---- f3: 실제로 바뀐 것만 싣는다 ----

    /// <summary>제자리 클릭이 빈 undo 항목을 만들면 안 된다 (f3). 안 바뀐 요소는 통째로 빠진다.</summary>
    [Fact]
    public void Build_UnchangedElement_EmitsNothing()
    {
        var doc = new AnnotationDocument("M1");
        var unchanged = NewStroke();
        var changed = NewStroke();
        doc.Add(unchanged);
        doc.Add(changed);

        var before = ElementTransformState.Identity;
        changed.TransformState = Moved(5, 5);

        var deltas = TransformCommitPlan.Build(
            [(unchanged, before), (changed, before)], LookupIn(doc), doc);

        var only = Assert.Single(deltas);
        Assert.Same(changed, only.Element);
    }

    /// <summary>전부 제자리면 빈 목록 — 호출부의 <c>Count &gt; 0</c> 게이트가 원장 항목을 막는다 (f3/SEL-12).</summary>
    [Fact]
    public void Build_AllUnchanged_ReturnsEmptyList()
    {
        var doc = new AnnotationDocument("M1");
        var a = NewStroke();
        var b = NewStroke();
        doc.Add(a);
        doc.Add(b);
        a.TransformState = Moved(3, 0);
        b.TransformState = Moved(0, 4);

        var deltas = TransformCommitPlan.Build(
            [(a, a.TransformState), (b, b.TransformState)], LookupIn(doc), doc);

        Assert.Empty(deltas);
    }

    // ---- SEL-12: 델타마다 자기 소유 문서 ----

    /// <summary>다중 모니터 선택: 소유 문서는 목록 전체가 아니라 요소마다 찾는다 (SEL-12).</summary>
    [Fact]
    public void Build_MixedOwners_UsesOwnerLookupPerElement()
    {
        var d1 = new AnnotationDocument("M1");
        var d2 = new AnnotationDocument("M2");
        var onD1 = NewStroke();
        var onD2 = NewStroke();
        d1.Add(onD1);
        d2.Add(onD2);

        var before = ElementTransformState.Identity;
        onD1.TransformState = Moved(1, 0);
        onD2.TransformState = Moved(0, 1);

        var deltas = TransformCommitPlan.Build(
            [(onD1, before), (onD2, before)], LookupIn(d1, d2), d1);

        Assert.Equal(2, deltas.Count);
        Assert.Same(d1, deltas[0].BeforeOwner);
        Assert.Same(d2, deltas[1].BeforeOwner);
    }

    /// <summary>소유자를 못 찾으면 제스처가 벌어진 문서로 떨어진다 — 소유자 없는 델타를 만들지 않는다 (SEL-12).</summary>
    [Fact]
    public void Build_MissingOwner_FallsBackToGestureDocument()
    {
        var gesture = new AnnotationDocument("M1");
        var orphan = NewStroke(); // 어느 문서에도 담기지 않았다.
        var before = ElementTransformState.Identity;
        orphan.TransformState = Moved(7, 7);

        var deltas = TransformCommitPlan.Build([(orphan, before)], LookupIn(gesture), gesture);

        var only = Assert.Single(deltas);
        Assert.Same(gesture, only.BeforeOwner);
        Assert.Same(gesture, only.AfterOwner);
    }

    /// <summary>
    /// <see cref="SelectionTransfer.Execute"/>가 <c>After</c>/<c>AfterOwner</c>를 뒤에 다시 쓰므로,
    /// Build는 소유권을 추정하지 않고 전/후를 같게 둔다 — 이관 판정이 두 곳에 생기면 안 된다 (SEL-14).
    /// </summary>
    [Fact]
    public void Build_BeforeOwnerAndAfterOwnerAreIdentical()
    {
        var d1 = new AnnotationDocument("M1");
        var d2 = new AnnotationDocument("M2");
        var a = NewStroke();
        var b = NewStroke();
        d1.Add(a);
        d2.Add(b);
        var before = ElementTransformState.Identity;
        a.TransformState = Moved(1, 1);
        b.TransformState = Moved(2, 2);

        var deltas = TransformCommitPlan.Build([(a, before), (b, before)], LookupIn(d1, d2), d1);

        Assert.All(deltas, delta => Assert.Same(delta.BeforeOwner, delta.AfterOwner));
    }

    /// <summary>입력 순서 보존 — 원장 1항목 안의 델타 순서가 곧 undo 복원 순서다 (SEL-12).</summary>
    [Fact]
    public void Build_PreservesInputOrder()
    {
        var doc = new AnnotationDocument("M1");
        var a = NewStroke();
        var b = NewStroke();
        var c = NewStroke();
        doc.Add(a);
        doc.Add(b);
        doc.Add(c);
        var before = ElementTransformState.Identity;
        a.TransformState = Moved(1, 0);
        b.TransformState = Moved(2, 0);
        c.TransformState = Moved(3, 0);

        var deltas = TransformCommitPlan.Build(
            [(c, before), (a, before), (b, before)], LookupIn(doc), doc);

        Assert.Equal([c, a, b], deltas.Select(d => d.Element));
    }

    /// <summary>After는 스냅샷이 아니라 <b>지금</b>의 상태다 — 드래그가 끝난 시점 값을 읽는다 (SEL-12).</summary>
    [Fact]
    public void Build_ChangedElement_UsesCurrentTransformStateAsAfter()
    {
        var doc = new AnnotationDocument("M1");
        var element = NewStroke();
        doc.Add(element);
        var before = ElementTransformState.Identity;
        var after = new ElementTransformState(2, 3, 45, new Vector(10, -20));
        element.TransformState = after;

        var only = Assert.Single(TransformCommitPlan.Build([(element, before)], LookupIn(doc), doc));

        Assert.Equal(before, only.Before);
        Assert.Equal(after, only.After);
        Assert.Same(element, only.Element);
    }

    // ---- f7/SEL-14: 놓은 지점은 이동일 때만 ----

    public static TheoryData<SelectionDragKind> AllDragKinds
    {
        get
        {
            var data = new TheoryData<SelectionDragKind>();
            foreach (var kind in Enum.GetValues<SelectionDragKind>())
            {
                data.Add(kind);
            }
            return data;
        }
    }

    /// <summary>
    /// 드롭 지점이 이동 이외로 새면 회전 핸들이 옆 모니터에 닿았다는 이유로 선택 전체가 이관된다 (f7/SEL-14).
    /// 열거 멤버가 늘면 행이 자동으로 따라오도록 리터럴 행이 아니라 전수 열거로 검사한다.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllDragKinds))]
    public void CarriesDropPoint_OnlyMove_IsTrue(SelectionDragKind kind)
    {
        Assert.Equal(kind == SelectionDragKind.Move, TransformCommitPlan.CarriesDropPoint(kind));
    }
}
