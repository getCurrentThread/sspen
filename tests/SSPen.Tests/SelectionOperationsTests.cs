using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using SSPen.Interop;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 선택 조작 순수 계획 함수 검증 (SEL-13/SEL-14, ARCH-15/ARCH-20).
/// 계획과 실행을 분리했으므로 문서를 실제로 변경하지 않고 순서·인덱스·상태 보정을 검증할 수 있다.
/// </summary>
public class SelectionOperationsTests
{
    private const double Tolerance = 1e-9;

    private static StrokeElement NewStroke() =>
        new([new Point(0, 0), new Point(10, 10)], Colors.Black, 3, isHighlighter: false);

    private static Func<AnnotationElement, AnnotationDocument?> LookupIn(params AnnotationDocument[] documents) =>
        element => documents.FirstOrDefault(d => d.Elements.Contains(element));

    // ---- PlanDelete (SEL-13) ----

    /// <summary>
    /// 이 함수의 존재 이유 자체: 제거하면서 인덱스를 수집하면 앞 요소가 빠질 때마다 뒤 인덱스가
    /// 밀려 복원 자리가 어긋난다. 계획은 **제거 전에** 완결되어야 한다.
    /// </summary>
    [Fact]
    public void PlanDelete_MultiDocument_CollectsIndicesBeforeRemoval()
    {
        var d1 = new AnnotationDocument("M1");
        var d2 = new AnnotationDocument("M2");
        var a = NewStroke();
        var b = NewStroke();
        var c = NewStroke();
        var onD2 = NewStroke();
        d1.Add(a);
        d1.Add(b);
        d1.Add(c);
        d2.Add(onD2);

        // a(0)와 c(2)를 삭제 대상으로. c의 인덱스는 a 제거에 영향받지 않아야 한다.
        var plan = SelectionOperations.PlanDelete([a, c, onD2], LookupIn(d1, d2));

        Assert.Equal(3, plan.Count);
        Assert.Equal(0, plan.Single(e => ReferenceEquals(e.Element, a)).Index);
        Assert.Equal(2, plan.Single(e => ReferenceEquals(e.Element, c)).Index);
        Assert.Equal(0, plan.Single(e => ReferenceEquals(e.Element, onD2)).Index);
        Assert.Same(d1, plan.Single(e => ReferenceEquals(e.Element, a)).Document);
        Assert.Same(d2, plan.Single(e => ReferenceEquals(e.Element, onD2)).Document);

        // 계획은 순수하다: 문서는 아직 그대로다.
        Assert.Equal(3, d1.Elements.Count);
        Assert.Single(d2.Elements);
    }

    /// <summary>
    /// 복원은 **오름차순** 삽입이다. 역순 삽입은 연속 인덱스를 함께 지웠을 때 망가진다
    /// ([a,b,c,d]에서 a,b,c 삭제 → 역순 복원은 [a,d,b,c]).
    /// </summary>
    [Fact]
    public void PlanDelete_RestoreOrder_IsAscendingIndex()
    {
        var document = new AnnotationDocument("M1");
        var a = NewStroke();
        var b = NewStroke();
        var c = NewStroke();
        var d = NewStroke();
        foreach (var s in (StrokeElement[])[a, b, c, d])
        {
            document.Add(s);
        }

        // 선택 순서를 일부러 뒤섞어도 계획은 인덱스 오름차순으로 정규화된다.
        // **인접 인덱스(0,1,2)를 쓴다**: 띄어진 인덱스는 오름차순과 역순이 우연히 같은 결과를 내서
        // 순서 결함을 못 잡는다 — 실제로 그 맹점이 원래 결함을 숨겼다.
        var plan = SelectionOperations.PlanDelete([c, a, b], LookupIn(document));

        Assert.Equal([0, 1, 2], plan.Select(e => e.Index).ToArray());

        // 원장 undo가 쓰는 순서 그대로: **오름차순** 삽입.
        foreach (var entry in plan)
        {
            entry.Document.Remove(entry.Element);
        }
        Assert.Equal(new AnnotationElement[] { d }, document.Elements);

        foreach (var entry in plan)
        {
            entry.Document.Insert(entry.Index, entry.Element);
        }
        Assert.Equal(new AnnotationElement[] { a, b, c, d }, document.Elements);
    }

    [Fact]
    public void PlanDelete_ElementWithNoOwner_IsSkipped()
    {
        var document = new AnnotationDocument("M1");
        var present = NewStroke();
        var orphan = NewStroke();
        document.Add(present);

        var plan = SelectionOperations.PlanDelete([present, orphan], LookupIn(document));

        Assert.Single(plan);
        Assert.Same(present, plan[0].Element);
    }

    // ---- PlanTransferOrder (SEL-AC-18) ----

    private static TransformDelta Delta(AnnotationElement element, AnnotationDocument owner) =>
        new(element, element.TransformState, element.TransformState, owner, owner);

    [Fact]
    public void PlanTransferOrder_MultipleElements_ReturnsSourceIndexAscending()
    {
        var source = new AnnotationDocument("M1");
        var bottom = NewStroke();
        var middle = NewStroke();
        var top = NewStroke();
        foreach (var s in (StrokeElement[])[bottom, middle, top])
        {
            source.Add(s);
        }
        var deltas = new[] { Delta(bottom, source), Delta(middle, source), Delta(top, source) };

        // 선택 순서가 z순서와 반대여도 이관은 원본 인덱스 오름차순을 따른다.
        var order = SelectionOperations.PlanTransferOrder([top, bottom, middle], deltas);

        Assert.Equal(new AnnotationElement[] { bottom, middle, top }, order);
    }

    /// <summary>다중 문서 선택은 각 요소의 **자기 소유 문서** 인덱스로 줄 세워야 한다.</summary>
    [Fact]
    public void PlanTransferOrder_MultiDocument_OrdersByOwnDocumentIndex()
    {
        var d1 = new AnnotationDocument("M1");
        var d2 = new AnnotationDocument("M2");
        var firstOnD1 = NewStroke();
        var secondOnD1 = NewStroke();
        var firstOnD2 = NewStroke();
        d1.Add(firstOnD1);
        d1.Add(secondOnD1);
        d2.Add(firstOnD2);
        var deltas = new[] { Delta(firstOnD1, d1), Delta(secondOnD1, d1), Delta(firstOnD2, d2) };

        var order = SelectionOperations.PlanTransferOrder(
            [secondOnD1, firstOnD2, firstOnD1], deltas);

        // 각 문서의 인덱스 0인 두 요소가 먼저 오고(동순위는 입력 순서로 안정 정렬),
        // 인덱스 1인 secondOnD1이 마지막이다. 문서를 가로질러 인덱스가 섞이는 것이 핵심.
        Assert.Equal(3, order.Count);
        Assert.Equal(secondOnD1, order[^1]);
        Assert.Contains(firstOnD1, order.Take(2));
        Assert.Contains(firstOnD2, order.Take(2));
    }

    [Fact]
    public void PlanTransferOrder_ElementMissingFromOwner_SortsLast()
    {
        var source = new AnnotationDocument("M1");
        var present = NewStroke();
        var missing = NewStroke();
        source.Add(present);
        var deltas = new[] { Delta(present, source), Delta(missing, source) };

        var order = SelectionOperations.PlanTransferOrder([missing, present], deltas);

        Assert.Equal(new AnnotationElement[] { present, missing }, order);
    }

    // ---- 전체 지우기 후 장식 고아 방지 (R10) ----

    /// <summary>
    /// R10의 진짜 계약: 문서를 비우면 선택집합도 비어야 한다. 아니면 빈 화면에 핸들만 떠 있는
    /// 고아 상태가 된다. <c>Document.Clear()</c>가 요소마다 <c>ElementRemoved</c>를 발화하고
    /// R17 구독자가 떨구므로 이 경로가 실제로 성립하는지를 직접 검증한다.
    /// </summary>
    [Fact]
    public void ClearAll_WithActiveSelection_EmptiesSelection()
    {
        var document = new AnnotationDocument("M1");
        var selection = new SelectionModel();
        selection.AttachTo(document);
        var a = NewStroke();
        var b = NewStroke();
        document.Add(a);
        document.Add(b);
        selection.Set([a, b]);
        Assert.Equal(2, selection.Count);

        document.Clear();

        Assert.Empty(document.Elements);
        Assert.Equal(0, selection.Count);
    }

    // ---- RebaseState (ARCH-20, R18) ----

    private static readonly PhysicalRect Left = new(-1920, 0, 1920, 1080);
    private static readonly PhysicalRect Center = new(0, 0, 1920, 1080);

    private static Rect Bounds() => new(100, 100, 200, 100);

    [Fact]
    public void RebaseState_SameDpi_LeavesScaleUnchanged()
    {
        var state = new ElementTransformState(2, 3, 45, new Vector(10, 20));

        var rebased = SelectionOperations.RebaseState(state, Bounds(), Center, 1.0, Left, 1.0);

        Assert.Equal(2, rebased.ScaleX, Tolerance);
        Assert.Equal(3, rebased.ScaleY, Tolerance);
    }

    [Fact]
    public void RebaseState_DifferentDpi_ScalesBothAxesByRatio()
    {
        // 100% → 150%: 대상이 더 촘촘하므로 논리 단위가 작아진다. r = 1.0/1.5.
        var state = new ElementTransformState(2, 3, 0, default);

        var rebased = SelectionOperations.RebaseState(state, Bounds(), Center, 1.0, Left, 1.5);

        Assert.Equal(2 * (1.0 / 1.5), rebased.ScaleX, Tolerance);
        Assert.Equal(3 * (1.0 / 1.5), rebased.ScaleY, Tolerance);
    }

    [Fact]
    public void RebaseState_PreservesAngle()
    {
        var state = new ElementTransformState(1, 1, 137.5, new Vector(3, 4));

        var rebased = SelectionOperations.RebaseState(state, Bounds(), Center, 1.0, Left, 1.5);

        Assert.Equal(137.5, rebased.AngleDegrees, Tolerance);
    }

    /// <summary>
    /// R18/ARCH-20 핵심 증인: <c>Translation</c>에 점 사상을 그대로 먹이는 오구현을 잡는다.
    ///
    /// **반드시 DPI가 달라야 한다.** 두 구현의 차이는 정확히 <c>c·(r−1)</c>이므로
    /// <c>r = 1</c>인 균일 DPI에서는 둘이 **수치적으로 같다** — 현 리그(3×1920×1080 균일)가
    /// 정확히 그 경우라 통합 테스트로는 이 결함을 영원히 잡을 수 없다. 이 헤드리스 증인이 유일한 방어선이다.
    /// </summary>
    [Fact]
    public void RebaseState_TreatsTranslationAsDisplacementNotPoint()
    {
        var bounds = Bounds();
        var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        var translation = new Vector(10, 20);
        var state = new ElementTransformState(1, 1, 0, translation);
        const double sourceDpi = 1.0;
        const double targetDpi = 1.5;

        var rebased = SelectionOperations.RebaseState(state, bounds, Center, sourceDpi, Left, targetDpi);

        // 오구현: 변위를 위치처럼 그대로 사상한다.
        var naive = CoordinateSpace.Rebase(
            new Point(translation.X, translation.Y), Center, sourceDpi, Left, targetDpi);

        // 두 구현은 c·(r−1)만큼 갈라진다 — 증인이 실제로 변별력을 가졌음을 먼저 못박는다.
        Assert.NotEqual(naive.X, rebased.Translation.X, Tolerance);

        // 그리고 올바른 쪽은 **물리 위치를 보존하는 쪽**이다.
        double sourcePhysicalX = Center.X + (center.X + translation.X) * sourceDpi;
        double correctPhysicalX = Left.X + (center.X + rebased.Translation.X) * targetDpi;
        double naivePhysicalX = Left.X + (center.X + naive.X) * targetDpi;

        Assert.Equal(sourcePhysicalX, correctPhysicalX, 1e-6);
        Assert.NotEqual(sourcePhysicalX, naivePhysicalX, 1e-6);
    }

    /// <summary>
    /// 이관의 진짜 목적: 놓는 순간 요소가 **화면상 같은 물리 자리**에 있어야 한다.
    /// 변형 후 로컬 중심의 월드 사상점이 두 좌표계에서 같은 물리 픽셀을 가리키는지 확인한다.
    /// </summary>
    [Fact]
    public void RebaseState_PreservesPhysicalPositionOfTransformedCenter()
    {
        var bounds = Bounds();
        var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        var state = new ElementTransformState(2, 2, 30, new Vector(40, -15));

        var rebased = SelectionOperations.RebaseState(state, bounds, Center, 1.0, Left, 1.5);

        // 원본 좌표계에서의 물리 위치.
        double sourcePhysicalX = Center.X + (center.X + state.Translation.X) * 1.0;
        double sourcePhysicalY = Center.Y + (center.Y + state.Translation.Y) * 1.0;

        // 대상 좌표계에서의 물리 위치.
        double targetPhysicalX = Left.X + (center.X + rebased.Translation.X) * 1.5;
        double targetPhysicalY = Left.Y + (center.Y + rebased.Translation.Y) * 1.5;

        Assert.Equal(sourcePhysicalX, targetPhysicalX, 1e-6);
        Assert.Equal(sourcePhysicalY, targetPhysicalY, 1e-6);
    }

    // ---- ScaleDisplacementForDpi (D1, R18) ----

    /// <summary>
    /// 대상 배율이 더 높으면 논리 변위는 <b>줄어든다</b> (D1). 150%에서는 1 논리 단위가 1.5 물리
    /// 픽셀이므로 같은 물리 거리를 유지하려면 <c>×(1/1.5)</c>가 되어야 한다.
    /// 이 리그는 3대 모두 100%라 <c>r ≠ 1</c>은 헤드리스에서만 검증된다 (R18).
    /// </summary>
    [Fact]
    public void ScaleDisplacementForDpi_TargetHigherDpi_ShrinksDelta()
    {
        var scaled = SelectionOperations.ScaleDisplacementForDpi(new Vector(30, -60), 1.0, 1.5);

        Assert.Equal(30 * 2.0 / 3.0, scaled.X, Tolerance);
        Assert.Equal(-60 * 2.0 / 3.0, scaled.Y, Tolerance);
    }

    /// <summary>대상 배율이 더 낮으면 논리 변위는 <b>커진다</b> (D1). 위 사례의 역방향이다.</summary>
    [Fact]
    public void ScaleDisplacementForDpi_TargetLowerDpi_GrowsDelta()
    {
        var scaled = SelectionOperations.ScaleDisplacementForDpi(new Vector(30, -60), 1.5, 1.0);

        Assert.Equal(45, scaled.X, Tolerance);
        Assert.Equal(-90, scaled.Y, Tolerance);
    }

    /// <summary>
    /// 이 리전의 핵심 증인 (D1). 식의 형태가 아니라 D1이 지키려는 <b>물리 거리 보존</b>을 직접
    /// 단언한다: <c>d_source · srcDpi == d_target · tgtDpi</c>. 비율을 뒤집거나 통째로 빼면 둘 다
    /// 여기서 걸린다. 반드시 <c>r ≠ 1</c>로 검증한다 — <c>r = 1</c>이면 올바른 식과 비율을 뺀
    /// 순진한 식이 수치적으로 같아 아무것도 증명하지 못한다 (R18).
    /// </summary>
    [Fact]
    public void ScaleDisplacementForDpi_PreservesPhysicalDistance()
    {
        const double sourceDpi = 1.0;
        const double targetDpi = 1.75;
        var delta = new Vector(37, -13);

        var scaled = SelectionOperations.ScaleDisplacementForDpi(delta, sourceDpi, targetDpi);

        Assert.Equal(delta.X * sourceDpi, scaled.X * targetDpi, Tolerance);
        Assert.Equal(delta.Y * sourceDpi, scaled.Y * targetDpi, Tolerance);
    }

    /// <summary>
    /// 배율이 같으면 <c>×1.0</c> 왕복조차 넣지 않고 원본 벡터를 그대로 돌려준다 (D1).
    /// 단독으로는 아무것도 증명하지 않는 계약 고정이다 — 증인은 위 세 개다 (R18).
    /// </summary>
    [Fact]
    public void ScaleDisplacementForDpi_SameDpi_IsIdentity()
    {
        var delta = new Vector(30, -60);

        var scaled = SelectionOperations.ScaleDisplacementForDpi(delta, 1.25, 1.25);

        Assert.Equal(delta, scaled);
    }

    /// <summary>
    /// <c>targetDpi > 0</c> 가드의 유일한 증인 (D1). <c>targetDpi</c>는 주입 델리게이트에서 오므로
    /// 0이 들어오면 변위가 ±∞가 되어 요소가 화면 밖으로 사라진다. 가드를 빼면 여기서 걸린다.
    /// </summary>
    [Fact]
    public void ScaleDisplacementForDpi_ZeroTargetDpi_IsIdentity()
    {
        var delta = new Vector(30, -60);

        var scaled = SelectionOperations.ScaleDisplacementForDpi(delta, 1.0, 0);

        Assert.Equal(delta, scaled);
        Assert.True(double.IsFinite(scaled.X));
        Assert.True(double.IsFinite(scaled.Y));
    }

    /// <summary>
    /// 같은 비율의 두 소유자가 갈라지지 않았음을 고정한다 (ARCH-20). "같은 비율"만 못박고
    /// "같은 함수"를 요구하지는 않는다 — <see cref="SelectionOperations.RebaseState"/>는 비율을
    /// 스케일과 사상된 점에 적용하고 이쪽은 생 변위에 적용하므로, 둘은 이웃이어야지 한 몸이
    /// 되어서는 안 된다.
    /// </summary>
    [Fact]
    public void ScaleDisplacementForDpi_MatchesRebaseStateRatio()
    {
        var rebased = SelectionOperations.RebaseState(
            new ElementTransformState(1, 1, 0, default), Bounds(), Center, 1.0, Left, 1.5);

        var scaled = SelectionOperations.ScaleDisplacementForDpi(new Vector(1, 0), 1.0, 1.5);

        Assert.Equal(rebased.ScaleX, scaled.X, Tolerance);
    }

    // ---- PlanMove (SEL-AC-9, SEL-LIM-5, D1, R15) ----

    /// <summary>모든 요소가 같은 배율일 때 쓰는 기본 조회 (환산이 항등이 되는 경우).</summary>
    private static Func<AnnotationElement, double> SameDpi(double dpi) => _ => dpi;

    private static Dictionary<long, ElementTransformState> BaseStatesOf(
        params AnnotationElement[] elements)
    {
        var map = new Dictionary<long, ElementTransformState>();
        foreach (var element in elements)
        {
            map[element.Id] = element.TransformState;
        }
        return map;
    }

    /// <summary>
    /// SEL-LIM-5의 증인: 모니터에 걸친 선택에서 <b>이동만</b>은 허용되므로, 다른 문서 소속 요소도
    /// 계획에 들어야 한다. 순회 대상을 이 서피스 소유분으로 좁히면 이 단언이 무너진다.
    /// </summary>
    [Fact]
    public void PlanMove_CrossMonitorSelection_IncludesElementsOwnedByOtherDocuments()
    {
        var d1 = new AnnotationDocument("M1");
        var d2 = new AnnotationDocument("M2");
        var onD1 = NewStroke();
        var onD2 = NewStroke();
        d1.Add(onD1);
        d2.Add(onD2);

        var plan = SelectionOperations.PlanMove(
            [onD1, onD2],
            BaseStatesOf(onD1, onD2),
            new Vector(12, -5),
            1.0,
            SameDpi(1.0));

        Assert.Equal(2, plan.Count);
        Assert.Same(onD1, plan[0].Element);
        Assert.Same(onD2, plan[1].Element);
        Assert.Equal(new Vector(12, -5), plan[0].Next.Translation);
        Assert.Equal(new Vector(12, -5), plan[1].Next.Translation);
    }

    /// <summary>
    /// D1: 배율이 다른 요소가 섞이면 <b>요소마다</b> 따로 환산해야 한다. 이 리그는 3대 모두
    /// 100%(r=1)이므로 같은 배율 단언은 올바른 식과 순진한 식을 구분하지 못한다 — r ≠ 1 필수.
    /// </summary>
    [Fact]
    public void PlanMove_MixedDpiTargets_ScalesEachDisplacementIndependently()
    {
        var a = NewStroke();
        var b = NewStroke();

        var plan = SelectionOperations.PlanMove(
            [a, b],
            BaseStatesOf(a, b),
            new Vector(30, 60),
            1.0,
            element => ReferenceEquals(element, a) ? 1.0 : 1.5);

        Assert.Equal(2, plan.Count);
        Assert.Equal(30, plan[0].Next.Translation.X, Tolerance);
        Assert.Equal(60, plan[0].Next.Translation.Y, Tolerance);
        Assert.Equal(20, plan[1].Next.Translation.X, Tolerance);
        Assert.Equal(40, plan[1].Next.Translation.Y, Tolerance);
    }

    /// <summary>
    /// 제스처 시작 뒤에 선택에 더해진 요소는 시작 상태가 없으므로 조용히 건너뛴다 (예외도 로그도 없다).
    /// 남은 요소들의 상대 순서는 그대로여야 한다.
    /// </summary>
    [Fact]
    public void PlanMove_ElementMissingFromBaseStates_IsSkipped()
    {
        var a = NewStroke();
        var b = NewStroke();
        var c = NewStroke();

        var plan = SelectionOperations.PlanMove(
            [a, b, c],
            BaseStatesOf(a, c), // b만 스냅샷에 없다.
            new Vector(4, 4),
            1.0,
            SameDpi(1.0));

        Assert.Equal(2, plan.Count);
        Assert.Same(a, plan[0].Element);
        Assert.Same(c, plan[1].Element);
    }

    /// <summary>
    /// 매 프레임 <b>드래그 시작 상태에서 재계산</b>한다 — 직전 프레임 결과에 누적하면 부동소수 오차가
    /// 쌓이고 취소 복원 기준도 사라진다. 1차 계획을 실제로 대입한 뒤 <b>같은</b> 시작 상태로 2차
    /// 계획을 세우면 결과는 <c>base + delta2</c>여야지 <c>base + delta1 + delta2</c>가 아니다.
    /// </summary>
    [Fact]
    public void PlanMove_RecomputesFromBaseState_NotFromCurrent()
    {
        var element = NewStroke();
        var baseStates = BaseStatesOf(element);

        var first = SelectionOperations.PlanMove(
            [element], baseStates, new Vector(10, 0), 1.0, SameDpi(1.0));
        element.TransformState = first[0].Next; // 호출부의 R15 집행을 흉내낸다.

        var second = SelectionOperations.PlanMove(
            [element], baseStates, new Vector(3, 0), 1.0, SameDpi(1.0));

        Assert.Equal(3, second[0].Next.Translation.X, Tolerance);
        Assert.Equal(0, second[0].Next.Translation.Y, Tolerance);
    }

    /// <summary>
    /// 반환 순서는 선택 순서다. 스냅샷 사전을 순회하면 순서가 사전 순서로 바뀌고, 선택집합에 없는
    /// 핸들 대상까지 섞여 들어온다.
    /// </summary>
    [Fact]
    public void PlanMove_PreservesSelectionOrder()
    {
        var a = NewStroke();
        var b = NewStroke();
        var c = NewStroke();
        var selected = new AnnotationElement[] { c, a, b };

        var plan = SelectionOperations.PlanMove(
            selected, BaseStatesOf(a, b, c), new Vector(1, 1), 1.0, SameDpi(1.0));

        Assert.Equal(3, plan.Count);
        Assert.Same(c, plan[0].Element);
        Assert.Same(a, plan[1].Element);
        Assert.Same(b, plan[2].Element);
    }

    /// <summary>
    /// 파일 계약의 증인 (ARCH-15, D4): 계획 함수는 아무것도 쓰지 않는다. 여기서 대입이 일어나면
    /// 알림 없는 <c>TransformState</c> 대입이 생겨 시각물과 장식이 갈린다 (R15).
    /// </summary>
    [Fact]
    public void PlanMove_DoesNotWriteTransformState()
    {
        var a = NewStroke();
        var b = NewStroke();

        var plan = SelectionOperations.PlanMove(
            [a, b], BaseStatesOf(a, b), new Vector(50, -70), 1.0, SameDpi(1.5));

        Assert.Equal(2, plan.Count); // 계획은 세워졌지만 소비하지 않았다.
        Assert.Equal(ElementTransformState.Identity, a.TransformState);
        Assert.Equal(ElementTransformState.Identity, b.TransformState);
    }
}
