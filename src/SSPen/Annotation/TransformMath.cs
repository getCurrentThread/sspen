using System.Windows;
using System.Windows.Media;

namespace SSPen.Annotation;

/// <summary>선택 장식의 조작 핸들 9종: 8방향 크기 핸들 + 상단 회전 핸들 (Round 3 상호작용 형태).</summary>
public enum HandleKind
{
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
    Rotate,
}

/// <summary>
/// 변형 1건의 페이로드 (SEL-12): 요소 참조 + 전/후 상태 + 전/후 소유 문서.
/// <c>long Id</c>를 쓰지 않는 이유: 문서에 id 인덱스가 없고, 소유권 복귀에 필요한 것은
/// undo 시점의 소유자가 아니라 **기록 시점의 원래 소유자**라 <paramref name="BeforeOwner"/>를 직접 들어야 한다.
/// </summary>
public readonly record struct TransformDelta(
    AnnotationElement Element,
    ElementTransformState Before,
    ElementTransformState After,
    AnnotationDocument BeforeOwner,
    AnnotationDocument AfterOwner);

/// <summary>
/// 변형 수학 순수 헬퍼 (SEL-9). UI와 완전히 분리되어 헤드리스 유닛 테스트 대상이다
/// (<c>FadeSchedulerCore</c>/<c>ShiftConstraints</c>/<c>CoordinateSpace</c> 분리 관례).
///
/// 좌표 프레임 계약 — 세 가지가 서로 다른 용도로 고정되어 있다:
/// <list type="bullet">
/// <item>로컬 경계 상자: 변형 전 모델 공간 축 정렬 <see cref="Rect"/>. 피벗·앵커·핸들 로컬 배치·스케일 기준.</item>
/// <item>로컬 프레임 4점(OBB): 위 상자의 꼭짓점을 <see cref="ToMatrix"/>로 사상한 것. 점선 경계·핸들 렌더.</item>
/// <item>축 정렬 월드 경계: <c>Rect.Transform</c> 결과. **마퀴 교차 판정 전용**(SEL-B-1).</item>
/// </list>
/// MI-1이 핸들을 로컬축(OBB)으로 확정했어도 마퀴는 축 정렬 그대로다. OBB 교차(SAT)를 도입하지 않는다.
/// </summary>
public static class TransformMath
{
    /// <summary>배율 절댓값 하한. 0/0 → NaN 행렬은 <c>Math.Clamp</c>를 통과하고 요소를 화면에서 증발시킨다 (R16).</summary>
    public const double MinScale = 0.01;

    /// <summary>회전 핸들이 로컬 상단 변에서 바깥으로 떨어지는 **화면** 거리 (배율과 무관하게 일정).</summary>
    public const double RotateHandleScreenOffset = 24;

    /// <summary>크기 핸들 한 변의 **화면** 길이 (힌트 판정과 렌더가 공유).</summary>
    public const double HandleScreenSize = 8;

    /// <summary>모서리 4개 → 변 4개 순서 (힌트 우선순위 고정).</summary>
    public static readonly HandleKind[] SizeHandlesCornersFirst =
    [
        HandleKind.TopLeft,
        HandleKind.TopRight,
        HandleKind.BottomRight,
        HandleKind.BottomLeft,
        HandleKind.Top,
        HandleKind.Right,
        HandleKind.Bottom,
        HandleKind.Left,
    ];

    /// <summary>
    /// 각 축을 최소 <paramref name="minExtent"/>까지 중심 기준으로 벌린다 (R16).
    /// 수평/수직 선과 단일 점 획은 한 축이 정확히 0이고, Shift 스냅이 0도/90도를 정확히 만들어낸다.
    /// </summary>
    public static Rect NonDegenerate(Rect bounds, double minExtent)
    {
        double min = Math.Max(minExtent, MinScale);
        double width = bounds.Width;
        double height = bounds.Height;
        double x = bounds.X;
        double y = bounds.Y;
        if (width < min)
        {
            x -= (min - width) / 2;
            width = min;
        }
        if (height < min)
        {
            y -= (min - height) / 2;
            height = min;
        }
        return new Rect(x, y, width, height);
    }

    /// <summary>
    /// 상태 → 행렬 **단일 합성 지점**. 이 순서가 곧 MI-1의 수학적 내용 전부다:
    /// <c>T(-pivot) · S(ScaleX, ScaleY) · R(AngleDegrees) · T(pivot) · T(Translation)</c>.
    /// 스케일이 회전보다 **먼저** 오므로 스케일이 요소 로컬 축을 따른다.
    /// 월드 사상: <c>p_world = (p_local − pivot)·S·R(θ) + pivot + t</c>.
    /// </summary>
    public static Matrix ToMatrix(ElementTransformState state, Point pivot)
    {
        var m = Matrix.Identity;
        m.Translate(-pivot.X, -pivot.Y);
        m.Scale(state.ScaleX, state.ScaleY);
        m.Rotate(state.AngleDegrees);
        m.Translate(pivot.X, pivot.Y);
        m.Translate(state.Translation.X, state.Translation.Y);
        return m;
    }

    /// <summary>월드 점을 로컬(변형 전) 공간으로 되돌린다. 힌트 판정과 스케일 계산의 1단계.</summary>
    public static Point ToLocal(ElementTransformState state, Rect localBounds, Point world)
    {
        var inverse = ToMatrix(state, Center(localBounds));
        if (!inverse.HasInverse)
        {
            return world;
        }
        inverse.Invert();
        return inverse.Transform(world);
    }

    /// <summary>핸들 드래그 시 **월드 위치가 고정되어야 하는** 반대편 지점 (로컬 좌표).</summary>
    public static Point AnchorLocal(Rect bounds, HandleKind handle) => handle switch
    {
        HandleKind.TopLeft => bounds.BottomRight,
        HandleKind.Top => new Point(CenterX(bounds), bounds.Bottom),
        HandleKind.TopRight => bounds.BottomLeft,
        HandleKind.Right => new Point(bounds.Left, CenterY(bounds)),
        HandleKind.BottomRight => bounds.TopLeft,
        HandleKind.Bottom => new Point(CenterX(bounds), bounds.Top),
        HandleKind.BottomLeft => bounds.TopRight,
        HandleKind.Left => new Point(bounds.Right, CenterY(bounds)),
        _ => Center(bounds),
    };

    /// <summary>8방향 크기 핸들의 로컬 중심. 회전 핸들은 화면 오프셋을 쓰므로 <see cref="RotateHandleWorld"/>로 간다.</summary>
    public static Point HandleCenterLocal(Rect bounds, HandleKind handle) => handle switch
    {
        HandleKind.TopLeft => bounds.TopLeft,
        HandleKind.Top => new Point(CenterX(bounds), bounds.Top),
        HandleKind.TopRight => bounds.TopRight,
        HandleKind.Right => new Point(bounds.Right, CenterY(bounds)),
        HandleKind.BottomRight => bounds.BottomRight,
        HandleKind.Bottom => new Point(CenterX(bounds), bounds.Bottom),
        HandleKind.BottomLeft => bounds.BottomLeft,
        HandleKind.Left => new Point(bounds.Left, CenterY(bounds)),
        _ => throw new ArgumentOutOfRangeException(nameof(handle), handle, "회전 핸들은 RotateHandleWorld로 계산한다."),
    };

    /// <summary>로컬 상단 변 중앙의 월드 위치 (회전 핸들 스템의 시작점).</summary>
    public static Point TopCenterWorld(ElementTransformState state, Rect localBounds)
    {
        var m = ToMatrix(state, Center(localBounds));
        return m.Transform(new Point(CenterX(localBounds), localBounds.Top));
    }

    /// <summary>
    /// 회전 핸들의 월드 위치. 로컬 상단 변 중앙에서 **로컬 −Y 방향**으로 화면 거리만큼 떨어진다.
    /// 요소가 180도 돌면 이 점이 화면 아래로 가므로 '상단' 가정을 쓰지 않는다 (R5).
    /// </summary>
    public static Point RotateHandleWorld(
        ElementTransformState state, Rect localBounds, double screenOffset = RotateHandleScreenOffset)
    {
        var m = ToMatrix(state, Center(localBounds));
        var top = m.Transform(new Point(CenterX(localBounds), localBounds.Top));
        var outward = m.Transform(new Vector(0, -1));
        double length = outward.Length;
        if (length < MinScale)
        {
            return new Point(top.X, top.Y - screenOffset);
        }
        outward /= length;
        return top + outward * screenOffset;
    }

    /// <summary>평행이동. <see cref="ElementTransformState.Translation"/>은 변위이므로 그대로 더한다.</summary>
    public static ElementTransformState Translate(ElementTransformState baseState, Vector delta) =>
        baseState with { Translation = baseState.Translation + delta };

    /// <summary>
    /// 로컬 경계 중심의 월드 사상점(<c>pivot + Translation</c>)을 축으로 하는 회전.
    /// A3에서는 <c>AngleDegrees += Δ</c>와 정확히 동치이며 <see cref="ElementTransformState.Translation"/>은 불변이다.
    /// <paramref name="shift"/>면 결과 각을 15도 배수로 스냅한다 (f10, X1).
    /// </summary>
    public static ElementTransformState Rotate(
        ElementTransformState baseState, Rect localBounds, Point from, Point to, bool shift)
    {
        var pivot = Center(localBounds) + baseState.Translation;
        var before = from - pivot;
        var after = to - pivot;
        if (before.Length < MinScale || after.Length < MinScale)
        {
            return baseState;
        }
        double delta = Degrees(Math.Atan2(after.Y, after.X) - Math.Atan2(before.Y, before.X));
        double angle = baseState.AngleDegrees + delta;
        return baseState with { AngleDegrees = shift ? ShiftConstraints.SnapDegrees(angle) : angle };
    }

    /// <summary>
    /// 로컬 축 기준 크기 조절 (MI-1). 6단계 계약:
    /// (1) 앵커의 월드 위치를 기준점으로 삼고 (2) 커서를 회전 역보정해 앵커 기준 변위를 얻은 뒤
    /// (3) 축별 비율을 산출하되 분모 절댓값이 <see cref="MinScale"/> 미만이면 그 축은 기존 배율 유지,
    /// (4) 측면 핸들은 해당 축만 바꾸고 (5) 배율은 **절댓값만** 하한 클램프하며 부호는 보존하고 (R14)
    /// (6) <c>t' = t + (a−c)·(S−S')·R(θ)</c>로 앵커를 월드 고정한다 (R21).
    ///
    /// (6)이 없으면 피벗이 중심 고정이라 반대편 앵커가 함께 밀려나며 **중심 기준 양방향 확대**가 된다.
    /// 회전 0도에서는 R=I라 오류가 상쇄되어 드러나지 않으므로 증인은 0도와 30도 양쪽이 필요하다.
    /// </summary>
    public static ElementTransformState ScaleLocal(
        ElementTransformState baseState, Rect localBounds, HandleKind handle, Point to)
    {
        if (handle == HandleKind.Rotate)
        {
            return baseState;
        }

        var center = Center(localBounds);
        var anchor = AnchorLocal(localBounds, handle);
        var grip = HandleCenterLocal(localBounds, handle);

        // 앵커의 현재 월드 위치 — (6)에 의해 드래그 내내 불변인 기준점.
        var anchorWorld = ToMatrix(baseState, center).Transform(anchor);

        // 커서를 앵커 기준 변위로 바꾸고 회전을 역보정하면 축별 비교가 가능해진다.
        var displacement = RotateVector(to - anchorWorld, -baseState.AngleDegrees);

        double spanX = grip.X - anchor.X;
        double spanY = grip.Y - anchor.Y;

        double scaleX = Math.Abs(spanX) < MinScale ? baseState.ScaleX : displacement.X / spanX;
        double scaleY = Math.Abs(spanY) < MinScale ? baseState.ScaleY : displacement.Y / spanY;

        // (4) 측면 핸들은 한 축만 건드린다.
        if (handle is HandleKind.Left or HandleKind.Right)
        {
            scaleY = baseState.ScaleY;
        }
        else if (handle is HandleKind.Top or HandleKind.Bottom)
        {
            scaleX = baseState.ScaleX;
        }

        scaleX = ClampMagnitude(scaleX);
        scaleY = ClampMagnitude(scaleY);

        var scaled = baseState with { ScaleX = scaleX, ScaleY = scaleY };
        return scaled with { Translation = PinAnchor(baseState, scaled, localBounds, handle) };
    }

    /// <summary>
    /// 앵커 월드 고정 보정 (R21): <c>t' = t + (a−c)·(S−S')·R(θ)</c>.
    /// 회전각이 스케일 연산에서 불변이라 A3 안에서 닫힌 형태이며 행렬 분해가 필요 없다.
    /// </summary>
    public static Vector PinAnchor(
        ElementTransformState before, ElementTransformState after, Rect localBounds, HandleKind handle)
    {
        var offset = AnchorLocal(localBounds, handle) - Center(localBounds);
        var difference = new Vector(
            offset.X * (before.ScaleX - after.ScaleX),
            offset.Y * (before.ScaleY - after.ScaleY));
        return before.Translation + RotateVector(difference, before.AngleDegrees);
    }

    /// <summary>
    /// 커서 아래의 핸들 (SEL-7). 회전 핸들은 **월드** 좌표에서 서피스 경계 안쪽으로 클램프한 위치와 비교하고,
    /// 8개 크기 핸들은 클램프 없이 **로컬** 공간에서 비교한다 (R5). 두 경로의 분기를 여기 한 곳에 둔다.
    /// </summary>
    /// <param name="surfaceBounds">서피스 논리 경계. <see cref="Rect.Empty"/>면 회전 핸들을 클램프하지 않는다.</param>
    public static HandleKind? HitHandle(
        ElementTransformState state,
        Rect localBounds,
        Point world,
        Rect surfaceBounds,
        double handleScreenSize = HandleScreenSize,
        double rotateScreenOffset = RotateHandleScreenOffset)
    {
        double reach = handleScreenSize / 2;

        var rotate = ClampRotateHandle(
            RotateHandleWorld(state, localBounds, rotateScreenOffset), surfaceBounds, reach);
        if ((world - rotate).Length <= reach)
        {
            return HandleKind.Rotate;
        }

        var local = ToLocal(state, localBounds, world);
        double reachX = reach / Math.Max(Math.Abs(state.ScaleX), MinScale);
        double reachY = reach / Math.Max(Math.Abs(state.ScaleY), MinScale);

        // 모서리 우선: 작은 요소에서 모서리와 변 핸들이 겹칠 때 모서리가 이긴다 (QA-1).
        foreach (var handle in SizeHandlesCornersFirst)
        {
            var center = HandleCenterLocal(localBounds, handle);
            if (Math.Abs(local.X - center.X) <= reachX && Math.Abs(local.Y - center.Y) <= reachY)
            {
                return handle;
            }
        }
        return null;
    }

    /// <summary>회전 핸들만 서피스 경계 안쪽으로 당긴다. 렌더와 힌트가 같은 위치를 쓰도록 공개한다.</summary>
    public static Point ClampRotateHandle(Point handle, Rect surfaceBounds, double inset)
    {
        if (surfaceBounds.IsEmpty)
        {
            return handle;
        }
        double left = surfaceBounds.Left + inset;
        double right = surfaceBounds.Right - inset;
        double top = surfaceBounds.Top + inset;
        double bottom = surfaceBounds.Bottom - inset;
        return new Point(
            left <= right ? Math.Clamp(handle.X, left, right) : CenterX(surfaceBounds),
            top <= bottom ? Math.Clamp(handle.Y, top, bottom) : CenterY(surfaceBounds));
    }

    /// <summary>부호를 보존한 채 절댓값만 하한 클램프 (R14: 핸들을 반대편으로 끌어 뒤집기 허용).</summary>
    public static double ClampMagnitude(double scale)
    {
        if (double.IsNaN(scale))
        {
            return MinScale;
        }
        double sign = scale < 0 ? -1 : 1;
        return sign * Math.Max(Math.Abs(scale), MinScale);
    }

    /// <summary>벡터를 지정 각도(도)만큼 회전.</summary>
    public static Vector RotateVector(Vector v, double degrees)
    {
        double radians = degrees * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        return new Vector(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }

    private static double Degrees(double radians) => radians * 180.0 / Math.PI;

    private static Point Center(Rect r) => new(CenterX(r), CenterY(r));

    private static double CenterX(Rect r) => r.X + r.Width / 2;

    private static double CenterY(Rect r) => r.Y + r.Height / 2;
}
