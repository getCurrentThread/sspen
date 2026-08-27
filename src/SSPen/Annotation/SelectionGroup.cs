using System.Windows;

namespace SSPen.Annotation;

/// <summary>
/// 그룹 프레임의 조작 핸들 5종 (R1): 모서리 4 + 회전 1.
///
/// 왜 8개가 아닌가: 그룹 프레임 축 기준 <b>비등방</b> 스케일은 구성 요소의 회전각이 90도의 배수가
/// 아니면 전단(shear)을 요구하는데, <see cref="ElementTransformState"/>는 전단을 표현할 수 없다
/// (LD-1/A3). 30도 요소에 diag(2,1)을 먹이면 로컬 두 축의 사잇각이 90도에서 123도로 벌어진다.
/// 반면 <b>등방</b> 스케일은 R(θ)와 sI가 교환되므로 각도가 제각각이어도 닫힌 형태로 정확하다.
/// 그래서 그릴 수 없는 조작은 어포던스도 만들지 않는다 — 측면 핸들을 아예 그리지 않는다.
/// </summary>
public enum GroupHandleKind
{
    TopLeft,
    TopRight,
    BottomRight,
    BottomLeft,
    Rotate,
}

/// <summary>
/// 다중 선택의 <b>그룹 프레임</b> 순수 기하 (R1). UI와 분리되어 헤드리스 유닛 테스트 대상이다.
///
/// 좌표 프레임 계약 (TransformMath의 3-프레임에 이어지는 <b>네 번째</b> 계약):
/// 그룹 프레임은 선택 요소들의 <see cref="AnnotationElement.TransformedBounds"/> 합집합인
/// <b>축 정렬</b> 사각형이다. 요소별 OBB(<c>TransformedCorners</c>)와 통합하지 않는다 —
/// 그룹은 자체 회전각을 <b>저장하지 않으므로</b>(해제 후 재선택하면 항상 축 정렬로 리셋된다)
/// 기울어진 그룹 프레임이라는 상태 자체가 존재하지 않는다.
/// 그 대가로 그룹을 회전하면 다음 재계산에서 프레임이 커진다 — 그래서 제스처 도중에는
/// 프레임을 <b>동결</b>해야 한다 (SurfaceInputController._groupFrame).
/// </summary>
public static class SelectionGroup
{
    /// <summary>그룹 조작이 시작되는 최소 선택 개수. 1개면 기존 요소별 8핸들 경로를 그대로 탄다.</summary>
    public const int MinGroupCount = 2;

    /// <summary>모서리 핸들 순서 (힌트 우선순위 고정).</summary>
    public static readonly GroupHandleKind[] CornersClockwise =
    [
        GroupHandleKind.TopLeft,
        GroupHandleKind.TopRight,
        GroupHandleKind.BottomRight,
        GroupHandleKind.BottomLeft,
    ];

    /// <summary>
    /// 그룹 프레임 = 선택 요소들의 축 정렬 월드 경계 합집합.
    /// 페이딩 요소는 제외한다 (SEL-LIM-3 방어를 <see cref="SelectionGeometry"/> 두 함수에만 두면
    /// 이 경로가 세 번째 구멍이 된다 — 사라지는 요소가 프레임을 부풀린다).
    /// 대상이 없으면 null.
    /// </summary>
    public static Rect? Frame(IEnumerable<AnnotationElement> elements)
    {
        Rect? frame = null;
        foreach (var element in elements)
        {
            if (element.IsFading)
            {
                continue;
            }
            var bounds = element.TransformedBounds;
            frame = frame is { } current ? Rect.Union(current, bounds) : bounds;
        }
        return frame;
    }

    public static Point Center(Rect frame) => new(frame.X + frame.Width / 2, frame.Y + frame.Height / 2);

    /// <summary>
    /// 이 서피스에서 <b>핸들을 잡을 수 있는가</b> (SEL-LIM-5의 단일 술어).
    ///
    /// 이 함수가 따로 있는 이유는 계약이 세 곳(장식 렌더 / 히트 테스트 / 휠 확대)에 흩어져 있었고,
    /// 흩어진 사이에 구멍이 났기 때문이다: 모니터에 걸친 선택에서 이 서피스가 요소를 <b>1개만</b>
    /// 소유하면 요소별 렌더 경로는 <c>owned.Count &lt; MinGroupCount</c>라 그룹 분기를 건너뛰고
    /// 8핸들을 전부 그렸는데, 히트 테스트는 걸친 선택이라며 전부 막아 <b>보이지만 잡히지 않는 핸들</b>이
    /// 생겼다. 그 핸들을 누르면 빈 곳 분기로 떨어져 선택이 통째로 날아가고 클릭 통과까지 켜졌다.
    /// 술어를 하나로 합치면 그 어긋남 자체가 표현 불가능해진다.
    /// </summary>
    public static bool HandlesGrabbable(int ownedCount, int selectionCount) =>
        ownedCount > 0 && ownedCount == selectionCount;

    /// <summary>모서리 핸들의 월드 중심. 회전 핸들은 <see cref="RotateHandle"/>로 간다.</summary>
    public static Point CornerCenter(Rect frame, GroupHandleKind handle) => handle switch
    {
        GroupHandleKind.TopLeft => frame.TopLeft,
        GroupHandleKind.TopRight => frame.TopRight,
        GroupHandleKind.BottomRight => frame.BottomRight,
        GroupHandleKind.BottomLeft => frame.BottomLeft,
        _ => throw new ArgumentOutOfRangeException(nameof(handle), handle, "회전 핸들은 RotateHandle로 계산한다."),
    };

    /// <summary>핸들을 잡았을 때 <b>월드 위치가 고정되어야 하는</b> 대각 반대편 모서리.</summary>
    public static Point AnchorCorner(Rect frame, GroupHandleKind handle) => handle switch
    {
        GroupHandleKind.TopLeft => frame.BottomRight,
        GroupHandleKind.TopRight => frame.BottomLeft,
        GroupHandleKind.BottomRight => frame.TopLeft,
        GroupHandleKind.BottomLeft => frame.TopRight,
        _ => Center(frame),
    };

    /// <summary>회전 핸들의 월드 위치: 상단 변 중앙에서 화면 거리만큼 위. 프레임이 축 정렬이라 방향이 항상 −Y다.</summary>
    public static Point RotateHandle(
        Rect frame, double screenOffset = TransformMath.RotateHandleScreenOffset) =>
        new(frame.X + frame.Width / 2, frame.Top - screenOffset);

    /// <summary>상단 변 중앙 (회전 스템 시작점).</summary>
    public static Point TopCenter(Rect frame) => new(frame.X + frame.Width / 2, frame.Top);

    /// <summary>
    /// 커서 아래의 그룹 핸들. 회전 핸들이 먼저인 이유는 요소별 경로와 같다 —
    /// 회전 핸들이 프레임 <b>바깥</b>에 놓이므로 순서를 뒤집으면 빈 곳 분기가 가로챈다.
    /// 렌더와 <b>같은 클램프 위치</b>를 써야 힌트와 그림이 어긋나지 않는다 (R5).
    /// </summary>
    public static GroupHandleKind? HitHandle(
        Rect frame,
        Point world,
        Rect surfaceBounds,
        double handleScreenSize = TransformMath.HandleScreenSize,
        double rotateScreenOffset = TransformMath.RotateHandleScreenOffset)
    {
        double reach = handleScreenSize / 2;

        var rotate = TransformMath.ClampRotateHandle(
            RotateHandle(frame, rotateScreenOffset), surfaceBounds, reach);
        if ((world - rotate).Length <= reach)
        {
            return GroupHandleKind.Rotate;
        }

        foreach (var handle in CornersClockwise)
        {
            var center = CornerCenter(frame, handle);
            if (Math.Abs(world.X - center.X) <= reach && Math.Abs(world.Y - center.Y) <= reach)
            {
                return handle;
            }
        }
        return null;
    }

    /// <summary>
    /// 모서리 드래그의 <b>등방</b> 배율: 대각 앵커에서 잡은 모서리로 향하는 축에 커서를 정사영한 비율.
    /// 대각 방향 성분만 쓰므로 종횡비가 절대 변하지 않고, 커서가 앵커를 지나쳐도 부호가 자연스럽게 뒤집히는 대신
    /// 호출부가 <see cref="TransformMath.ClampGroupFactor"/>로 하한을 걸어 뒤집기를 막는다
    /// (그룹 뒤집기는 요소별 부호 규약 R14와 달리 프레임에 의미 있는 표현이 없다).
    /// </summary>
    public static double ScaleFactor(Rect frame, GroupHandleKind handle, Point to)
    {
        if (handle == GroupHandleKind.Rotate)
        {
            return 1;
        }
        var anchor = AnchorCorner(frame, handle);
        var axis = CornerCenter(frame, handle) - anchor;
        double lengthSquared = axis.LengthSquared;
        if (lengthSquared < TransformMath.MinScale)
        {
            return 1;
        }
        var cursor = to - anchor;
        return ((cursor.X * axis.X) + (cursor.Y * axis.Y)) / lengthSquared;
    }
}
