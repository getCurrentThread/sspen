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
/// 제스처 도중 <b>화면에 그려지는</b> 그룹 프레임 (R1): 축 정렬 경계 + 그 경계의 <b>중심</b>을 축으로 한 회전각.
///
/// <b>수명 계약</b>: <c>AngleDegrees != 0</c>인 인스턴스는 GroupRotate 드래그가 진행 중일 때만 존재한다.
/// 어디에도 저장되지 않으며 <c>SurfaceInputController.ResetSelectGesture</c>에서 소멸한다.
/// 그래서 <see cref="SelectionGroup.Frame"/>의 반환형은 여전히 <c>Rect?</c>이고,
/// "그룹은 자체 회전각을 저장하지 않는다"는 계약은 문자 그대로 유지된다 —
/// 이것은 <b>네 번째 bounds 계약이 아니라 렌더/히트 좌표 값</b>이며, 원장(<see cref="TransformDelta"/>)에는
/// 각도를 실을 자리가 없으므로 지속 상태로 승격하면 실행취소가 되돌릴 수 없는 상태가 생긴다 (SEL-LIM-6).
///
/// 피벗을 별도 필드로 두지 않는 이유: 그룹 회전 피벗은 정의상 <c>SelectionGroup.Center(_groupFrame)</c>이고
/// 회전은 중심을 고정하므로 <see cref="Bounds"/>의 중심과 <b>항상</b> 같다. 두 값이 어긋날 방법을
/// 타입에서 없앤다.
///
/// <b>암시적 변환(<c>Rect</c> → <c>GroupFrame</c>)을 정의하지 말 것</b>: 정의하면 각도를 모르는
/// <see cref="SelectionGroup.Frame"/>의 반환값이 조용히 각도 0으로 승격되어, "여기서 각도가 사라진다"는
/// 사실이 호출부에서 보이지 않게 된다. 승격은 항상 <c>new GroupFrame(rect, 0)</c>으로 명시한다.
/// </summary>
/// <param name="Bounds">제스처 시작 시점에 <b>동결</b>된 축 정렬 경계. 회전 중에도 크기는 여기서 굳는다.</param>
/// <param name="AngleDegrees"><see cref="Pivot"/> 기준 회전각(도). 드래그 시작 기준 <b>누적 증분</b>이다.</param>
public readonly record struct GroupFrame(Rect Bounds, double AngleDegrees)
{
    /// <summary>회전 중심 = 축 정렬 경계의 중심. 회전이 중심을 고정하므로 각도에 불변이다.</summary>
    public Point Pivot => new(Bounds.X + (Bounds.Width / 2), Bounds.Y + (Bounds.Height / 2));

    /// <summary>
    /// 프레임 로컬(축 정렬) 점 → 월드. 각도 0이면 입력을 <b>그대로</b> 돌려준다 —
    /// 피벗 왕복 <c>(x−p)+p</c>의 1ulp 표류를 차단해 회전하지 않는 모든 경우가 수정 이전과 비트 동일이 된다.
    /// </summary>
    public Point ToWorld(Point framePoint)
    {
        if (AngleDegrees == 0)
        {
            return framePoint;
        }
        var pivot = Pivot;
        return pivot + TransformMath.RotateVector(framePoint - pivot, AngleDegrees);
    }

    /// <summary>
    /// 월드 점 → 프레임 로컬. 프레임 공간은 <b>순수 회전</b>(배율 항 없음)이라 등거리 사상이므로,
    /// 요소별 경로(<see cref="TransformMath.HitHandle"/>)에 필요한 축별 reach 보정이 여기서는 필요 없다.
    /// </summary>
    public Point ToFrame(Point world)
    {
        if (AngleDegrees == 0)
        {
            return world;
        }
        var pivot = Pivot;
        return pivot + TransformMath.RotateVector(world - pivot, -AngleDegrees);
    }
}

/// <summary>
/// GroupRotate 한 프레임분 계산 결과 (R1): 잉크에 먹일 피벗·각도 증분과 화면에 그릴 가이드 프레임을
/// <b>한 번에</b> 낸다.
///
/// 왜 세 값을 한 레코드로 묶는가: 따로 구하면 "가이드가 잉크와 다른 각도(또는 다른 피벗)를 쓰는 상태"가
/// 표현 가능해지고, 실제로 그 어긋남이 <b>"그룹을 회전해도 테두리 가이드가 같이 안 도는"</b> 증상이었다
/// (가이드 각도는 0에 못 박히고 잉크만 돌았다). 한 호출이 세 값을 함께 내면 어긋남이 표현 불가능해지고,
/// 컨트롤러 배선은 <c>step.Guide</c>를 밀고 <c>step.Pivot</c>/<c>step.DeltaDegrees</c>를
/// <see cref="TransformMath.RotateAbout"/>에 넘기는 기계적 작업만 남는다.
/// </summary>
/// <param name="Pivot">회전축. 동결 프레임의 중심이며 가이드와 잉크가 <b>반드시</b> 공유해야 한다.</param>
/// <param name="DeltaDegrees">드래그 시작 기준 누적 각도 증분 (Shift면 15도 배수로 스냅됨).</param>
/// <param name="Guide">화면에 그릴 프레임. 동결 프레임이 비어 있을 때만 null이다.</param>
public readonly record struct GroupRotateStep(Point Pivot, double DeltaDegrees, GroupFrame? Guide);

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
///
/// 단, 제스처 도중 <b>화면에 그려지는</b> 프레임만 <see cref="GroupFrame"/>으로 각도를 싣는다.
/// 그 각도는 렌더/히트 좌표 계산에만 쓰이고 <see cref="Frame"/>·원장·선택 모델 어디에도 흘러들지 않으며,
/// 마우스 업에서 소멸한다 (SEL-LIM-6: 그래서 제스처가 끝나면 프레임이 축 정렬로 복귀하며 커진다).
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
    ///
    /// <b>반환형이 각도 없는 <c>Rect?</c>인 것이 계약이다</b> — 각도를 여기로 올리려면 먼저
    /// <see cref="TransformDelta"/>에 각도 자리를 만들어 실행취소가 되돌릴 수 있게 해야 한다 (SEL-LIM-6).
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

    /// <summary>그룹 회전 피벗. 회전은 중심을 고정하므로 각도와 무관하다.</summary>
    public static Point Center(GroupFrame frame) => frame.Pivot;

    public static Point Center(Rect frame) => Center(new GroupFrame(frame, 0));

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

    /// <summary>
    /// 모서리 핸들의 <b>프레임 로컬</b> 중심. 테두리 4점·핸들 렌더·히트·배율 축이 전부 이 표 하나를 공유한다 —
    /// 표를 복제하면 "그리는 좌표"와 "잡히는 좌표"가 다시 갈라진다 (SEL-LIM-5 회귀의 결함 클래스).
    /// </summary>
    private static Point CornerLocal(Rect bounds, GroupHandleKind handle) => handle switch
    {
        GroupHandleKind.TopLeft => bounds.TopLeft,
        GroupHandleKind.TopRight => bounds.TopRight,
        GroupHandleKind.BottomRight => bounds.BottomRight,
        GroupHandleKind.BottomLeft => bounds.BottomLeft,
        _ => throw new ArgumentOutOfRangeException(nameof(handle), handle, "회전 핸들은 RotateHandle로 계산한다."),
    };

    /// <summary>모서리 핸들의 월드 중심. 회전 핸들은 <see cref="RotateHandle(GroupFrame, double)"/>로 간다.</summary>
    public static Point CornerCenter(GroupFrame frame, GroupHandleKind handle) =>
        frame.ToWorld(CornerLocal(frame.Bounds, handle));

    public static Point CornerCenter(Rect frame, GroupHandleKind handle) =>
        CornerCenter(new GroupFrame(frame, 0), handle);

    /// <summary>
    /// 그려지는 테두리 4점 (좌상 → 우상 → 우하 → 좌하). 순서는 요소별
    /// <see cref="AnnotationElement.TransformedCorners"/>와 <b>같은 계약</b>이고,
    /// <see cref="CornersClockwise"/>·<see cref="CornerCenter(GroupFrame, GroupHandleKind)"/>와 같은 표에서
    /// 나오므로 테두리 꼭짓점과 모서리 핸들 중심이 정의상 일치한다.
    /// </summary>
    public static Point[] Corners(GroupFrame frame)
    {
        var corners = new Point[CornersClockwise.Length];
        for (int i = 0; i < corners.Length; i++)
        {
            corners[i] = CornerCenter(frame, CornersClockwise[i]);
        }
        return corners;
    }

    /// <summary>핸들을 잡았을 때 <b>월드 위치가 고정되어야 하는</b> 대각 반대편 모서리.</summary>
    public static Point AnchorCorner(GroupFrame frame, GroupHandleKind handle) => handle switch
    {
        GroupHandleKind.TopLeft => CornerCenter(frame, GroupHandleKind.BottomRight),
        GroupHandleKind.TopRight => CornerCenter(frame, GroupHandleKind.BottomLeft),
        GroupHandleKind.BottomRight => CornerCenter(frame, GroupHandleKind.TopLeft),
        GroupHandleKind.BottomLeft => CornerCenter(frame, GroupHandleKind.TopRight),
        _ => frame.Pivot,
    };

    public static Point AnchorCorner(Rect frame, GroupHandleKind handle) =>
        AnchorCorner(new GroupFrame(frame, 0), handle);

    /// <summary>
    /// 회전 핸들의 월드 위치: 프레임 상단 변 중앙에서 <b>프레임 로컬 −Y</b> 방향으로 화면 거리만큼.
    /// 요소별 <see cref="TransformMath.RotateHandleWorld"/>와 같은 규칙이며, 프레임이 180도 돌면 핸들이
    /// 프레임 <b>아래</b>로 간다 (R5). 예전에는 프레임이 축 정렬이라 방향을 화면 −Y로 하드코딩했는데,
    /// 그것이 그룹 회전 중 회전 핸들이 제자리에 못 박혀 있던 원인이다.
    ///
    /// 요소별 경로와 달리 outward 정규화·퇴화 방어가 없는 이유: 그룹 프레임 공간에는 배율 항이 없어
    /// R(θ)·(0,−1)이 <b>항상</b> 단위 벡터다 (요소 행렬에는 S가 섞여 있어 정규화가 필요하다).
    /// 나중에 "요소 경로에는 있는데 여기엔 없다"며 정규화를 채워 넣지 말 것.
    /// </summary>
    public static Point RotateHandle(
        GroupFrame frame, double screenOffset = TransformMath.RotateHandleScreenOffset) =>
        TopCenter(frame) + (TransformMath.RotateVector(new Vector(0, -1), frame.AngleDegrees) * screenOffset);

    public static Point RotateHandle(
        Rect frame, double screenOffset = TransformMath.RotateHandleScreenOffset) =>
        RotateHandle(new GroupFrame(frame, 0), screenOffset);

    /// <summary>
    /// 회전한 상단 변의 중점 (회전 스템 시작점).
    /// 스템이 프레임 변에서 출발해야 테두리·스템·핸들이 한 도형으로 보인다.
    /// </summary>
    public static Point TopCenter(GroupFrame frame) =>
        frame.ToWorld(new Point(frame.Bounds.X + (frame.Bounds.Width / 2), frame.Bounds.Top));

    public static Point TopCenter(Rect frame) => TopCenter(new GroupFrame(frame, 0));

    /// <summary>
    /// 커서 아래의 그룹 핸들. 회전 핸들이 먼저인 이유는 요소별 경로와 같다 —
    /// 회전 핸들이 프레임 <b>바깥</b>에 놓이므로 순서를 뒤집으면 빈 곳 분기가 가로챈다.
    /// 렌더와 <b>같은 클램프 위치</b>를 써야 힌트와 그림이 어긋나지 않는다 (R5).
    ///
    /// 모서리 판정은 커서를 <see cref="GroupFrame.ToFrame"/>로 프레임 로컬에 되돌린 뒤 비교한다.
    /// 각도가 붙은 프레임에서도 <b>그려지는 좌표와 잡히는 좌표가 같은 계산에서 나오게</b> 하려는 것이다 —
    /// "제스처 중에는 히트 경로가 실행되지 않으니 괜찮다"는 도달 불가 논증에 기대지 않는다
    /// (그 논증은 호버 커서 힌트 같은 기능 하나에 조용히 무너진다).
    /// 프레임 공간이 순수 회전이라 <see cref="TransformMath.HitHandle"/>의 축별 reach 보정은 필요 없다.
    /// </summary>
    public static GroupHandleKind? HitHandle(
        GroupFrame frame,
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

        var local = frame.ToFrame(world);
        foreach (var handle in CornersClockwise)
        {
            var center = CornerLocal(frame.Bounds, handle);
            if (Math.Abs(local.X - center.X) <= reach && Math.Abs(local.Y - center.Y) <= reach)
            {
                return handle;
            }
        }
        return null;
    }

    public static GroupHandleKind? HitHandle(
        Rect frame,
        Point world,
        Rect surfaceBounds,
        double handleScreenSize = TransformMath.HandleScreenSize,
        double rotateScreenOffset = TransformMath.RotateHandleScreenOffset) =>
        HitHandle(new GroupFrame(frame, 0), world, surfaceBounds, handleScreenSize, rotateScreenOffset);

    /// <summary>
    /// 모서리 드래그의 <b>등방</b> 배율: 대각 앵커에서 잡은 모서리로 향하는 축에 커서를 정사영한 비율.
    /// 대각 방향 성분만 쓰므로 종횡비가 절대 변하지 않고, 커서가 앵커를 지나쳐도 부호가 자연스럽게 뒤집히는 대신
    /// 호출부가 <see cref="TransformMath.ClampGroupFactor"/>로 하한을 걸어 뒤집기를 막는다
    /// (그룹 뒤집기는 요소별 부호 규약 R14와 달리 프레임에 의미 있는 표현이 없다).
    ///
    /// 정사영 축이 <see cref="CornerCenter(GroupFrame, GroupHandleKind)"/>/
    /// <see cref="AnchorCorner(GroupFrame, GroupHandleKind)"/>에서 나오므로, 프레임에 각도가 붙으면
    /// 축도 함께 돌아 본문을 고칠 것이 없다.
    /// </summary>
    public static double ScaleFactor(GroupFrame frame, GroupHandleKind handle, Point to)
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

    public static double ScaleFactor(Rect frame, GroupHandleKind handle, Point to) =>
        ScaleFactor(new GroupFrame(frame, 0), handle, to);

    /// <summary>
    /// 제스처 도중 <b>화면에 그려질</b> 그룹 프레임. null이면 창은 매 그리기마다 살아있는 축 정렬 합집합을 쓴다.
    ///
    /// 계약 세 가지를 한 곳에 못박는다.
    /// (1) <b>회전만</b> 프레임을 민다. 등방 스케일은 축 정렬 사상이라 살아있는 합집합이 그대로 정답이고,
    ///     오히려 밀면 구성원 배율이 <see cref="TransformMath.MinScale"/>에 클램프될 때 이상적 사상과
    ///     실제 합집합이 갈라져 마우스 업에서 프레임이 튄다.
    /// (2) <b>크기는 동결</b>이다(<paramref name="frozen"/> = 제스처 시작 시점 합집합). 살아있는 합집합으로
    ///     되그리면 회전 중 프레임이 부풀어 잡은 핸들이 커서 밑에서 빠져나간다 — 동결의 원래 근거는 크기에 관한 것이다.
    /// (3) <b>각도만 살린다</b>. <paramref name="deltaDegrees"/>는 드래그 시작 기준으로 매 프레임 재계산된
    ///     누적 증분이어야 한다. 직전 프레임 각에 더해 나가면 "직전 프레임 결과 누적 금지" 규약이
    ///     깨져 부동소수 오차가 쌓인다.
    /// </summary>
    public static GroupFrame? GestureFrame(Rect frozen, bool rotating, double deltaDegrees) =>
        rotating && !frozen.IsEmpty ? new GroupFrame(frozen, deltaDegrees) : null;

    /// <summary>
    /// 그룹 회전각 <b>증분</b>. Shift는 결과 각이 아니라 증분을 15도 배수로 스냅한다 — 요소마다 시작 각이 달라
    /// 결과 각을 스냅하는 단일 선택 규칙(<see cref="TransformMath.Rotate"/>)을 그대로 쓸 수 없다.
    ///
    /// 가이드 프레임과 잉크는 <b>이 함수가 돌려준 값 하나</b>를 공유해야 한다. 두 번 호출하거나 프레임에
    /// 스냅 전 각을 쓰면 15도 경계마다 프레임이 잉크에서 떨어진다.
    /// 동결 프레임의 시작 각이 0이므로, 스냅된 증분이 곧 그려지는 프레임의 절대 각도다 —
    /// Shift로 스냅한 만큼 가이드가 정확히 그 각도에 선다.
    /// </summary>
    public static GroupRotateStep RotateStep(Rect frozen, Point from, Point to, bool shift)
    {
        if (frozen.IsEmpty)
        {
            // Rect.Empty는 좌표가 ±무한대라 피벗이 NaN이고, NaN 각도는 요소를 화면에서 증발시킨다 (R16).
            return new GroupRotateStep(default, 0, null);
        }
        var pivot = Center(frozen);
        double delta = RotationDelta(pivot, from, to, shift);
        return new GroupRotateStep(pivot, delta, GestureFrame(frozen, rotating: true, delta));
    }

    public static double RotationDelta(Point pivot, Point from, Point to, bool shift)
    {
        var before = from - pivot;
        var after = to - pivot;
        if (before.Length < TransformMath.MinScale || after.Length < TransformMath.MinScale)
        {
            return 0;
        }
        double delta =
            (Math.Atan2(after.Y, after.X) - Math.Atan2(before.Y, before.X)) * 180.0 / Math.PI;
        return shift ? ShiftConstraints.SnapDegrees(delta) : delta;
    }
}
