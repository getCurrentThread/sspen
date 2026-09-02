using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="SurfaceDecorationPlanner"/>의 증인 (43단계, SEL-10, SEL-LIM-5/6, R5). 프리미티브 개수·순서(단일 11 / 그룹 7 / 걸친 선택 1 /
/// 빈 선택 0 — 통합 DecorationRenderTests의 DecorationsPerElement=11과 같은 수), 포즈 프레임의 코너가 <see cref="SelectionGroup.CornerCenter"/>와
/// 같음, 그리고 <b>교차 불변식</b>: 플래너가 낸 모든 핸들 중심에서 <see cref="SelectionGesturePlanner.Plan"/>을 부르면 같은 종류의 핸들이
/// 잡힌다 — "그려지는 위치 == 잡히는 위치"의 두 절반이 같은 함수군을 쓴다는 헤드리스 증인.
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
