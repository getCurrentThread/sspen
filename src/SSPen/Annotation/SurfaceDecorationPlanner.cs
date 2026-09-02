using System.Windows;

namespace SSPen.Annotation;

/// <summary>장식 프리미티브 (43단계) — <see cref="AnnotationVisualFactory"/>의 장식 빌더 넷과 1:1이다. 창은 이 목록을 순서대로 그리기만 한다.</summary>
public abstract record DecorationPrimitive;

/// <summary>마퀴 사각형 — 축 정렬 (SEL-B-1).</summary>
public sealed record MarqueePrimitive(Rect Rect) : DecorationPrimitive;

/// <summary>선택 테두리 (요소별 OBB 4점 또는 그룹 프레임 4점).</summary>
public sealed record OutlinePrimitive(Point[] Corners) : DecorationPrimitive;

/// <summary>크기/회전 핸들 하나 — 월드 좌표 중심, 크기는 <c>TransformMath.HandleScreenSize</c>로 창이 그린다.</summary>
public sealed record HandlePrimitive(Point Center) : DecorationPrimitive;

/// <summary>회전 핸들 스템 (상단 변 중앙 → 클램프된 회전 핸들).</summary>
public sealed record RotateStemPrimitive(Point From, Point To) : DecorationPrimitive;

/// <summary>
/// 선택 장식 배치의 순수 계획 (43단계, SEL-10/SEL-11, R1, R5, SEL-LIM-5/6). "그려지는 위치 == 잡히는 위치"의 <b>렌더 절반</b>이다 —
/// 히트 절반(<see cref="SelectionGesturePlanner"/>)과 같은 함수군(<see cref="SelectionGroup"/>·<see cref="TransformMath"/>)을 같은
/// 인자로 부르므로, 렌더가 핸들을 다른 곳에 그리는 상태는 표현 불가능하다. 창(<c>ContentSurfaceWindow.RedrawDecorations</c>)은
/// 결과를 순서대로 <c>Children.Add</c>할 뿐 판정을 재유도하지 않는다.
///
/// <see cref="SelectionGroup.HandlesGrabbable"/>는 여기서 <b>직접</b> 부른다 — 사전 계산 bool을 받지 않는다(7e44cd3 교훈: 재유도가
/// 기록된 회귀 원인). 세 호출 지점은 이 플래너, <see cref="SelectionGesturePlanner.Plan"/>, <c>SurfaceInputController.Wheel</c>이다.
/// <paramref name="surfaceBounds"/>는 창이 매 호출마다 값으로 넘긴다 (R5: 캐시 금지, <c>SurfaceInputSeams.SurfaceBounds</c>와 같은 출처).
/// 표 드래그 HUD 배지는 여기 없다 — 선택 장식이 아니라 별도 힌트 채널(<c>setTableBadge</c>, 26단계)이다.
/// </summary>
public static class SurfaceDecorationPlanner
{
    /// <param name="owned">이 서피스가 소유한 선택 요소 (<see cref="SelectionGroup.OwnedBy"/>).</param>
    /// <param name="selectionCount">전체 선택 개수 — 모니터에 걸친 선택(SEL-LIM-5) 판정용.</param>
    /// <param name="gestureFrame">GroupRotate 중 컨트롤러가 밀어 넣은 포즈 프레임 (없으면 살아있는 축 정렬 합집합).</param>
    public static IReadOnlyList<DecorationPrimitive> Plan(
        IReadOnlyList<AnnotationElement> owned,
        int selectionCount,
        Rect? marquee,
        GroupFrame? gestureFrame,
        Rect surfaceBounds)
    {
        var plan = new List<DecorationPrimitive>();
        if (marquee is { } rect)
        {
            plan.Add(new MarqueePrimitive(rect));
        }

        // 모니터에 걸친 선택은 경계만 그리고 핸들을 숨긴다 (SEL-LIM-5): 두 서피스의 논리 좌표계가 서로소라 공통 프레임이
        // 성립하지 않으므로, 잡을 수 없는 핸들을 그리면 거짓 어포던스가 된다. 소유 요소가 1개인 서피스는 아래 요소별 경로를
        // 타므로 그룹 분기에만 걸어두면 그쪽에 구멍이 난다.
        bool handles = SelectionGroup.HandlesGrabbable(owned.Count, selectionCount);

        // R1: 다중 선택은 **하나의 그룹**으로 보인다 — 요소별 프레임 대신 공통 축 정렬 프레임 1개.
        if (owned.Count >= SelectionGroup.MinGroupCount)
        {
            PlanGroup(plan, owned, gestureFrame, surfaceBounds, handles);
            return plan;
        }

        foreach (var element in owned)
        {
            plan.Add(new OutlinePrimitive(element.TransformedCorners()));
            if (!handles)
            {
                continue;
            }

            var bounds = element.LocalBounds;
            var matrix = element.TransformMatrix;
            foreach (var handle in TransformMath.SizeHandlesCornersFirst)
            {
                plan.Add(new HandlePrimitive(matrix.Transform(TransformMath.HandleCenterLocal(bounds, handle))));
            }

            // 회전 핸들은 렌더와 힌트가 **같은 클램프된 위치**를 써야 한다 (R5).
            var stemStart = TransformMath.TopCenterWorld(element.TransformState, bounds);
            var rotate = TransformMath.ClampRotateHandle(
                TransformMath.RotateHandleWorld(element.TransformState, bounds),
                surfaceBounds,
                TransformMath.HandleScreenSize / 2);
            plan.Add(new RotateStemPrimitive(stemStart, rotate));
            plan.Add(new HandlePrimitive(rotate));
        }
        return plan;
    }

    /// <summary>
    /// 그룹 장식 (R1): 공통 프레임 + 모서리 4핸들 + 회전 핸들 1개. 측면 4핸들을 그리지 않는 이유는 <see cref="SelectionGroup"/> 참고 —
    /// 비등방 그룹 스케일은 회전된 요소에 전단을 요구해 <see cref="ElementTransformState"/>로 표현할 수 없다.
    /// 회전 중에는 컨트롤러가 밀어 넣은 각도로 테두리·핸들·스템이 함께 돈다 — 히트 테스트도 같은 <see cref="GroupFrame"/> 계산을 쓴다 (R5).
    /// </summary>
    private static void PlanGroup(
        List<DecorationPrimitive> plan, IReadOnlyList<AnnotationElement> owned, GroupFrame? gestureFrame, Rect surfaceBounds, bool handles)
    {
        GroupFrame? current = gestureFrame;
        if (current is null && SelectionGroup.Frame(owned) is { } live)
        {
            current = new GroupFrame(live, 0);
        }
        if (current is not { } frame)
        {
            return;
        }

        plan.Add(new OutlinePrimitive(SelectionGroup.Corners(frame)));
        if (!handles)
        {
            return;
        }

        foreach (var handle in SelectionGroup.CornersClockwise)
        {
            plan.Add(new HandlePrimitive(SelectionGroup.CornerCenter(frame, handle)));
        }

        var rotate = TransformMath.ClampRotateHandle(
            SelectionGroup.RotateHandle(frame), surfaceBounds, TransformMath.HandleScreenSize / 2);
        plan.Add(new RotateStemPrimitive(SelectionGroup.TopCenter(frame), rotate));
        plan.Add(new HandlePrimitive(rotate));
    }
}
