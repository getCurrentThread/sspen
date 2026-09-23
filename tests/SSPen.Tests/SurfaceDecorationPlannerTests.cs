using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="SurfaceDecorationPlanner"/>의 증인 (43단계, SEL-10, SEL-LIM-5/6, R5). 프리미티브 개수·순서(단일 11 / 그룹 7 / 걸친 선택 1 /
/// 빈 선택 0 — 통합 DecorationRenderTests의 DecorationsPerElement=11과 같은 수), 포즈 프레임의 코너가 <see cref="SelectionGroup.CornerCenter"/>와
/// 같음, 그리고 <b>교차 불변식</b>: 플래너가 낸 모든 핸들 중심에서 <see cref="SelectionGesturePlanner.Plan"/>을 부르면 같은 종류의 핸들이
/// 잡힌다 — "그려지는 위치 == 잡히는 위치"의 두 절반이 같은 함수군을 쓴다는 헤드리스 증인. 크기 쪽(그려진 가장자리 == 도달거리)과
/// 회전 시의 알려진 한계는 <c>Plan_EveryDrawnHandleEdge_*</c>·<c>Plan_RotatedElement_*_KnownLimit</c>가 본다 (60단계, A4-6).
/// </summary>
public class SurfaceDecorationPlannerTests
{
    private static readonly Rect Surface = new(0, 0, 1920, 1080);

    private static StrokeElement Stroke(double x, double y, double w, double h) =>
        new([new Point(x, y), new Point(x + w, y + h)], Colors.Black, 4, isHighlighter: false);

    [Fact]
    public void Plan_Empty_IsEmpty() => Assert.Empty(SurfaceDecorationPlanner.Plan([], 0, null, null, Surface));

    [Fact]
    public void Plan_MarqueeOnly_IsOneMarquee()
    {
        var plan = SurfaceDecorationPlanner.Plan([], 0, new Rect(10, 10, 50, 40), null, Surface);

        var marquee = Assert.IsType<MarqueePrimitive>(Assert.Single(plan));
        Assert.Equal(new Rect(10, 10, 50, 40), marquee.Rect);
    }

    /// <summary>단일 선택: 테두리 1 + 크기 핸들 8 + 스템 1 + 회전 핸들 1 = 11 (DecorationRenderTests.DecorationsPerElement).</summary>
    [Fact]
    public void Plan_SingleOwnedElement_HasElevenPrimitivesInRenderOrder()
    {
        var plan = SurfaceDecorationPlanner.Plan([Stroke(300, 300, 200, 100)], 1, null, null, Surface);

        Assert.Equal(11, plan.Count);
        Assert.IsType<OutlinePrimitive>(plan[0]);
        Assert.All(plan.Skip(1).Take(8), p => Assert.IsType<HandlePrimitive>(p));
        Assert.IsType<RotateStemPrimitive>(plan[9]);
        Assert.IsType<HandlePrimitive>(plan[10]);
    }

    /// <summary>그룹: 테두리 1 + 모서리 4 + 스템 1 + 회전 1 = 7 — 측면 핸들은 없다 (SEL-LIM-4).</summary>
    [Fact]
    public void Plan_Group_HasSevenPrimitives()
    {
        var plan = SurfaceDecorationPlanner.Plan([Stroke(300, 300, 200, 100), Stroke(600, 400, 100, 100)], 2, null, null, Surface);

        Assert.Equal(7, plan.Count);
        Assert.IsType<OutlinePrimitive>(plan[0]);
        Assert.Equal(4, plan.Skip(1).Take(4).Count(p => p is HandlePrimitive));
        Assert.IsType<RotateStemPrimitive>(plan[5]);
        Assert.IsType<HandlePrimitive>(plan[6]);
    }

    /// <summary>SEL-LIM-5: 모니터에 걸친 선택에서 이 서피스가 하나만 소유하면 테두리만 — 요소별 경로에서도 핸들 0.</summary>
    [Fact]
    public void Plan_CrossMonitorOwnedOneOfTwo_IsBorderOnly()
    {
        var plan = SurfaceDecorationPlanner.Plan([Stroke(300, 300, 200, 100)], 2, null, null, Surface);

        Assert.IsType<OutlinePrimitive>(Assert.Single(plan));
    }

    [Fact]
    public void Plan_CrossMonitorGroupOwnedTwoOfThree_IsBorderOnly()
    {
        var plan = SurfaceDecorationPlanner.Plan([Stroke(300, 300, 200, 100), Stroke(600, 400, 100, 100)], 3, null, null, Surface);

        Assert.IsType<OutlinePrimitive>(Assert.Single(plan));
    }

    /// <summary>교차 불변식: 플래너가 낸 모든 핸들 중심은 히트 플래너에서 같은 종류의 핸들로 잡힌다.</summary>
    [Fact]
    public void Plan_EveryHandleCenter_IsHitBySelectionGesturePlanner_Single()
    {
        var element = Stroke(300, 300, 200, 100);
        var owned = new List<AnnotationElement> { element };
        var plan = SurfaceDecorationPlanner.Plan(owned, 1, null, null, Surface);

        foreach (var handle in plan.OfType<HandlePrimitive>())
        {
            var hit = SelectionGesturePlanner.Plan(owned, owned, 1, _ => true, handle.Center, shift: false, Surface);
            Assert.True(hit.Kind is SelectionDragKind.Scale or SelectionDragKind.Rotate, $"핸들 중심 {handle.Center}에서 {hit.Kind}");
        }
    }

    /// <summary>마우스 다운 시점의 그룹(각도 0 — 포즈는 BeginSelectGesture 머리에서 null로 밀린다): 히트 플래너가 같은 핸들을 잡는다.</summary>
    [Fact]
    public void Plan_EveryHandleCenter_IsHitBySelectionGesturePlanner_Group()
    {
        var owned = new List<AnnotationElement> { Stroke(300, 300, 200, 100), Stroke(600, 400, 100, 100) };
        var plan = SurfaceDecorationPlanner.Plan(owned, 2, null, null, Surface);

        foreach (var handle in plan.OfType<HandlePrimitive>())
        {
            var hit = SelectionGesturePlanner.Plan(owned, owned, 2, _ => true, handle.Center, shift: false, Surface);
            Assert.True(hit.Kind is SelectionDragKind.GroupScale or SelectionDragKind.GroupRotate, $"핸들 {handle.Center}에서 {hit.Kind}");
        }
    }

    /// <summary>
    /// 포즈된 그룹(GroupRotate 진행 중, SEL-LIM-6): 이때의 히트 절반은 마우스 다운 플래너가 아니라 <see cref="SelectionGroup.HitHandle"/>이다 —
    /// 커서를 같은 GroupFrame으로 프레임 공간에 되돌려 판정하므로 "그려진 코너 == 잡히는 코너"가 각도와 무관하게 성립한다.
    /// </summary>
    [Theory]
    [InlineData(30)]
    [InlineData(135)]
    [InlineData(-60)]
    public void Plan_EveryHandleCenter_OfPosedGroup_IsHitByGroupHitHandle(double angle)
    {
        var owned = new List<AnnotationElement> { Stroke(300, 300, 200, 100), Stroke(600, 400, 100, 100) };
        var frame = new GroupFrame(SelectionGroup.Frame(owned)!.Value, angle);
        var plan = SurfaceDecorationPlanner.Plan(owned, 2, null, frame, Surface);

        var expected = new[] { GroupHandleKind.TopLeft, GroupHandleKind.TopRight, GroupHandleKind.BottomRight, GroupHandleKind.BottomLeft, GroupHandleKind.Rotate };
        var handles = plan.OfType<HandlePrimitive>().ToArray();
        Assert.Equal(5, handles.Length);
        for (int i = 0; i < handles.Length; i++)
        {
            Assert.Equal(expected[i], SelectionGroup.HitHandle(frame, handles[i].Center, Surface));
        }
    }

    /// <summary>포즈 프레임: 코너 4개는 SelectionGroup.CornerCenter와 같은 자리 — 렌더와 히트가 같은 회전을 쓴다 (SEL-LIM-6).</summary>
    [Fact]
    public void Plan_PosedGroupFrame_CornersMatchSelectionGroupCornerCenter()
    {
        var owned = new List<AnnotationElement> { Stroke(300, 300, 200, 100), Stroke(600, 400, 100, 100) };
        var frame = new GroupFrame(SelectionGroup.Frame(owned)!.Value, 30);

        var plan = SurfaceDecorationPlanner.Plan(owned, 2, null, frame, Surface);

        var corners = plan.Skip(1).Take(4).Cast<HandlePrimitive>().Select(h => h.Center).ToArray();
        var expected = SelectionGroup.CornersClockwise.Select(k => SelectionGroup.CornerCenter(frame, k)).ToArray();
        Assert.Equal(expected, corners);
        Assert.Equal(SelectionGroup.Corners(frame), ((OutlinePrimitive)plan[0]).Corners);
    }

    /// <summary>R5: 상단 가장자리의 회전 핸들은 서피스 안으로 클램프된다 — 스템 끝과 핸들 중심이 같은 점이다.</summary>
    [Fact]
    public void Plan_RotateHandleAtTopEdge_IsClampedInsideSurface_AndStemEndsThere()
    {
        var plan = SurfaceDecorationPlanner.Plan([Stroke(300, 2, 200, 40)], 1, null, null, Surface);

        var stem = Assert.IsType<RotateStemPrimitive>(plan[9]);
        var rotate = Assert.IsType<HandlePrimitive>(plan[10]);
        Assert.Equal(stem.To, rotate.Center);
        Assert.True(rotate.Center.Y >= TransformMath.HandleScreenSize / 2, $"회전 핸들 Y {rotate.Center.Y}");
    }

    /// <summary>
    /// "그려진 크기 == 잡히는 도달거리" — AGENTS L25 불변식의 <b>크기</b> 절반 (60단계, A4-6). 창은 각 핸들을 중심에서 한 변
    /// <see cref="TransformMath.HandleScreenSize"/>인 월드 축 정렬 사각형(회전 핸들은 같은 지름의 원)으로 그린다. 각도 0에서는
    /// 그 그림의 가장자리 표본(<see cref="DrawnEdgeOffsets"/>)이 모두 <b>같은</b> 핸들로 잡혀야 한다 — 모서리 핸들은 모서리 우선
    /// 규칙이 같은 종류를 보장하고, 200×100 요소에서는 변 핸들끼리 겹치지 않는다. 배율 3은 로컬 도달거리를 배율로 나누는
    /// 보정(<c>reachX/reachY</c>)이 월드 반경을 지키는지 본다. 이 자리의 옛 증인은 같은 상수를 자기 자신과 비교해 아무것도 보지 않았다.
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(3.0)]
    public void Plan_EveryDrawnHandleEdge_IsGrabbable_AxisAligned_Single(double scale)
    {
        // 옛 증인에서 옮긴 사용성 하한: 핸들이 10px 미만이면 잡는 손이 자주 빗나간다.
        Assert.True(TransformMath.HandleScreenSize >= 10, "핸들이 10px 미만이면 잡는 손이 자주 빗나간다");
        var element = Stroke(300, 300, 200, 100);
        element.TransformState = ElementTransformState.Identity with { ScaleX = scale, ScaleY = scale };

        var plan = SurfaceDecorationPlanner.Plan([element], 1, null, null, Surface);

        var expected = TransformMath.SizeHandlesCornersFirst.Append(HandleKind.Rotate).ToArray();
        var handles = plan.OfType<HandlePrimitive>().ToArray();
        Assert.Equal(expected.Length, handles.Length);
        for (int i = 0; i < handles.Length; i++)
        {
            foreach (var offset in DrawnEdgeOffsets(square: !handles[i].Rotate))
            {
                var point = handles[i].Center + offset;
                var hit = TransformMath.HitHandle(element.TransformState, element.LocalBounds, point, Surface);
                Assert.True(hit == expected[i], $"배율 {scale}: {expected[i]} 핸들의 그려진 가장자리 {point}에서 {hit}");
            }
        }
    }

    /// <summary>그룹(각도 0 — 마우스 다운 시점의 프레임): 모서리 4 + 회전 1의 그려진 가장자리가 모두 같은 그룹 핸들로 잡힌다.</summary>
    [Fact]
    public void Plan_EveryDrawnHandleEdge_IsGrabbable_AxisAligned_Group()
    {
        var owned = new List<AnnotationElement> { Stroke(300, 300, 200, 100), Stroke(600, 400, 100, 100) };
        var frame = SelectionGroup.Frame(owned)!.Value;

        var plan = SurfaceDecorationPlanner.Plan(owned, 2, null, null, Surface);

        var expected = SelectionGroup.CornersClockwise.Append(GroupHandleKind.Rotate).ToArray();
        var handles = plan.OfType<HandlePrimitive>().ToArray();
        Assert.Equal(expected.Length, handles.Length);
        for (int i = 0; i < handles.Length; i++)
        {
            foreach (var offset in DrawnEdgeOffsets(square: !handles[i].Rotate))
            {
                var point = handles[i].Center + offset;
                var hit = SelectionGroup.HitHandle(frame, point, Surface);
                Assert.True(hit == expected[i], $"{expected[i]} 그룹 핸들의 그려진 가장자리 {point}에서 {hit}");
            }
        }
    }

    /// <summary>
    /// 알려진 한계의 특성화 (A4-6, <see cref="HandlePrimitive"/> 문서): 회전된 요소의 크기 핸들은 월드 축 정렬 사각형으로 그려지지만
    /// 히트는 요소 로컬 축으로 돈 정사각형이다. 45°에서 그려진 사각형의 모서리 방향 (+4.9, +4.9)는 중심에서 약 6.9px라 로컬 축으로
    /// 되돌리면 한 축이 도달거리 5를 넘는다 — 보이는데 잡히지 않는다. 반대로 로컬 대각 (4.9, 4.9)를 월드로 올린 점은 그려진 사각형
    /// 밖인데 잡힌다. 그림을 로컬 축으로 돌리거나 히트를 월드 축으로 바꾸면 여기가 빨개진다 — 그때 이 테스트와 한계 문장을 함께 고친다.
    /// </summary>
    [Fact]
    public void Plan_RotatedElement_SizeHandleDrawnCorner_OutsideReach_KnownLimit()
    {
        var element = Stroke(300, 300, 200, 100);
        element.TransformState = ElementTransformState.Identity with { AngleDegrees = 45 };

        var plan = SurfaceDecorationPlanner.Plan([element], 1, null, null, Surface);

        var sizeHandles = plan.OfType<HandlePrimitive>().Where(h => !h.Rotate).ToArray();
        Assert.Equal(TransformMath.SizeHandlesCornersFirst.Length, sizeHandles.Length);
        var drawnCorner = new Vector(4.9, 4.9);
        var rotatedCorner = element.TransformMatrix.Transform(drawnCorner); // 로컬 대각을 월드로 — 이동 성분 없이 회전만
        for (int i = 0; i < sizeHandles.Length; i++)
        {
            var kind = TransformMath.SizeHandlesCornersFirst[i];
            var center = sizeHandles[i].Center;
            Assert.Null(TransformMath.HitHandle(element.TransformState, element.LocalBounds, center + drawnCorner, Surface));
            Assert.Equal(kind, TransformMath.HitHandle(element.TransformState, element.LocalBounds, center + rotatedCorner, Surface));
        }
    }

    /// <summary>
    /// 그려진 핸들 가장자리의 표본: 축 방향 네 점(원·사각형 공통), 사각형이면 네 모서리도. 경계에서 부동소수 오차로
    /// 갈리지 않도록 0.01 안쪽을 잡는다.
    /// </summary>
    private static IEnumerable<Vector> DrawnEdgeOffsets(bool square)
    {
        double r = TransformMath.HandleScreenSize / 2 - 0.01;
        yield return new Vector(r, 0);
        yield return new Vector(-r, 0);
        yield return new Vector(0, r);
        yield return new Vector(0, -r);
        if (square)
        {
            yield return new Vector(r, r);
            yield return new Vector(r, -r);
            yield return new Vector(-r, r);
            yield return new Vector(-r, -r);
        }
    }

    /// <summary>단일 술어 규약: HandlesGrabbable의 정의는 SelectionGroup 한 곳뿐이고 플래너는 사전 bool을 받지 않는다.</summary>
    [Fact]
    public void HandlesGrabbable_DefinedOnlyInSelectionGroup_ByReflection()
    {
        Assert.NotNull(typeof(SelectionGroup).GetMethod("HandlesGrabbable"));
        Assert.Null(typeof(SurfaceDecorationPlanner).GetMethod("HandlesGrabbable"));
        var plan = typeof(SurfaceDecorationPlanner).GetMethod("Plan")!;
        Assert.DoesNotContain(plan.GetParameters(), p => p.ParameterType == typeof(bool));
    }

    /// <summary>SEL-LIM-6: 프리미티브는 그룹 각도를 싣지 않는다 — 각도는 좌표 계산에만 쓰이고 죽는다.</summary>
    [Fact]
    public void DecorationPrimitives_HaveNoGroupFrameOrAngleSlot_ByReflection()
    {
        var types = new[] { typeof(MarqueePrimitive), typeof(OutlinePrimitive), typeof(HandlePrimitive), typeof(RotateStemPrimitive) };

        foreach (var type in types)
        {
            Assert.DoesNotContain(type.GetProperties(), p => p.PropertyType == typeof(GroupFrame) || p.PropertyType == typeof(double));
        }
    }
}
