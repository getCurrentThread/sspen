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
/// 변형 수학 순수 헬퍼 (SEL-9). UI와 완전히 분리되어 헤드리스 유닛 테스트 대상이다
/// (<c>FadeSchedulerCore</c>/<c>ShiftConstraints</c>/<c>CoordinateSpace</c> 분리 관례).
/// 원장 페이로드 <see cref="TransformDelta"/>는 <c>TransformDelta.cs</c>에 있다 — 이 파일은
/// <see cref="ElementTransformState"/>·<see cref="Rect"/>·<see cref="Point"/>만 알고 요소·문서 타입을 모른다.
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
    /// <summary>
    /// 배율 절댓값 하한. 0/0 → NaN 행렬은 <c>Math.Clamp</c>를 통과하고 요소를 화면에서 증발시킨다 (R16).
    ///
    /// <b>이 상수는 이름보다 넓게 쓰인다.</b> 값을 바꾸려는 사람이 파급을 한눈에 보도록 실태를 적어 둔다.
    /// 아래 두 무리는 <b>차원이 다르다</b> — 배율은 무차원이고 퇴화 가드는 픽셀·픽셀²이다.
    /// 그런데도 오늘은 한 리터럴을 공유하므로 <b>독립 조정이 불가능하다</b>.
    /// 이 기록은 <b>보존이지 승인이 아니다</b>. 나누고 싶으면 아래 목록 전부를 한 커밋에서 옮겨라 —
    /// 절반만 새 이름을 붙이면 같은 값에 이름이 둘 생겨 드리프트가 오히려 늘어난다.
    ///
    /// <list type="bullet">
    /// <item><b>배율 하한(본래 의미, 무차원)</b> — <see cref="ClampMagnitude"/>(R14/D5),
    ///   <see cref="HitHandle"/>의 축별 도달거리 역산(0 나눗셈 방어),
    ///   <see cref="ClampGroupFactor"/>의 구성원 하한과 바닥값.</item>
    /// <item><b>퇴화 엡실론(재사용, 길이 px)</b> — <see cref="NonDegenerate"/>의 최소 폭,
    ///   <see cref="RotateHandleWorld"/>의 방향 벡터 길이(R5), <see cref="Rotate"/>의 회전 반경 2개,
    ///   <see cref="ScaleLocal"/>의 로컬 스팬 2개, <see cref="SelectionGroup.RotationDelta"/>의 회전 반경 2개.</item>
    /// <item><b>퇴화 엡실론(재사용, 길이제곱 px²)</b> — <c>SelectionGroup.ScaleFactor</c>의 정사영 축 길이제곱.</item>
    /// </list>
    ///
    /// 실측 기준: 두 파일 9개 메서드에서 16회 참조한다(선언·주석 줄 제외). <see cref="Rotate"/>와
    /// <see cref="SelectionGroup.RotationDelta"/>는 <b>의도상 쌍둥이</b>(단일 요소 회전 / 그룹 회전의 같은 퇴화 가드)이므로
    /// 한쪽만 다른 상수로 바꾸지 않는다. 위치가 아니라 <b>메서드 이름</b>으로 적은 이유는 줄 번호가 썩기 때문이다 —
    /// <c>grep -rn MinScale src/SSPen/Annotation/</c>가 이 목록과 어긋나면 목록을 고쳐라.
    /// </summary>
    public const double MinScale = 0.01;

    /// <summary>
    /// 배율 절댓값 상한 (D5). 하한만 있고 상한이 없으면 핸들 드래그 한 번이나 휠 몇 노치로
    /// 요소가 임의 크기까지 커지는데, 8개 크기 핸들은 회전 핸들과 달리 서피스 경계로 클램프되지
    /// 않으므로(<see cref="ClampRotateHandle"/> 참고) <b>전부 화면 밖으로 나가 마우스로 되돌릴 방법이 사라진다</b>.
    /// 값 자체는 폭주 방지선이다 — 실사용 범위(수 배)보다 훨씬 위에 둔다.
    /// </summary>
    public const double MaxScale = 100;

    /// <summary>회전 핸들이 로컬 상단 변에서 바깥으로 떨어지는 **화면** 거리 (배율과 무관하게 일정).</summary>
    public const double RotateHandleScreenOffset = 24;

    /// <summary>
    /// 크기 핸들 한 변의 **화면** 길이 (힌트 판정과 렌더가 공유).
    /// 8 → 10: 8px는 잡는 손이 자주 빗나간다(마우스 정밀도 한계 근처이고, 태블릿 펜에서는 더 나쁘다).
    /// 렌더와 히트가 같은 상수를 쓰므로 늘리면 <b>둘 다</b> 늘어난다 — "그려진 것 == 잡히는 것" 유지 (AGENTS L25).
    /// </summary>
    public const double HandleScreenSize = 10;

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

    /// <summary>
    /// 부호를 보존한 채 절댓값만 <see cref="MinScale"/>~<see cref="MaxScale"/>로 클램프
    /// (R14: 핸들을 반대편으로 끌어 뒤집기 허용 / D5: 상한 없는 폭주 차단).
    /// </summary>
    public static double ClampMagnitude(double scale)
    {
        if (double.IsNaN(scale))
        {
            return MinScale;
        }
        double sign = scale < 0 ? -1 : 1;
        return sign * Math.Clamp(Math.Abs(scale), MinScale, MaxScale);
    }

    // ---- 그룹 변형 (R1). 임의 피벗 기준이라 요소 자기 피벗을 쓰는 위 함수들과 재사용 관계가 없다. ----

    /// <summary>
    /// 임의 피벗 <paramref name="pivot"/> 기준 <b>등방</b> 확대/축소 (R1, R7).
    /// <c>S' = f·S</c>, <c>θ' = θ</c>, <c>t' = f·(c + t − P) + P − c</c> (c = 로컬 경계 중심).
    ///
    /// 왜 등방만인가: <c>p_world = (p−c)·S·R(θ) + c + t</c>에서 그룹 축 기준 비등방 D를 끼우면
    /// 선형부가 <c>S·R(θ)·D</c>가 되는데, 이것이 다시 <c>S'·R(θ')</c> 꼴로 분해되려면 두 행이
    /// 직교해야 한다. θ가 90도의 배수가 아니면 직교가 깨져 <b>전단이 필요해지고 A3에서는 표현 불가능</b>하다.
    /// 등방(D = fI)은 <c>R(θ)·fI = fI·R(θ)</c>로 교환되므로 각도가 제각각인 그룹에서도 정확하다.
    /// </summary>
    public static ElementTransformState ScaleAbout(
        ElementTransformState baseState, Rect localBounds, Point pivot, double factor)
    {
        var center = Center(localBounds);
        var position = new Point(center.X + baseState.Translation.X, center.Y + baseState.Translation.Y);
        var scaled = new Point(
            ((position.X - pivot.X) * factor) + pivot.X,
            ((position.Y - pivot.Y) * factor) + pivot.Y);
        return baseState with
        {
            ScaleX = ClampMagnitude(baseState.ScaleX * factor),
            ScaleY = ClampMagnitude(baseState.ScaleY * factor),
            Translation = new Vector(scaled.X - center.X, scaled.Y - center.Y),
        };
    }

    /// <summary>
    /// 임의 피벗 기준 회전 (R1). <c>θ' = θ + Δ</c>, <c>t' = R_Δ(c + t − P) + P − c</c>.
    /// 요소가 각자 자기 중심으로 도는 <see cref="Rotate"/>와 달리 <b>그룹 전체가 한 점을 축으로</b> 돈다 —
    /// 그래서 <c>Translation</c>이 같이 움직인다.
    /// </summary>
    public static ElementTransformState RotateAbout(
        ElementTransformState baseState, Rect localBounds, Point pivot, double deltaDegrees)
    {
        var center = Center(localBounds);
        var position = new Point(center.X + baseState.Translation.X, center.Y + baseState.Translation.Y);
        var rotated = RotateVector(position - pivot, deltaDegrees) + pivot;
        return baseState with
        {
            AngleDegrees = baseState.AngleDegrees + deltaDegrees,
            Translation = new Vector(rotated.X - center.X, rotated.Y - center.Y),
        };
    }

    /// <summary>
    /// 그룹 배율을 <b>구성 요소 전원이 견딜 수 있는</b> 범위로 재단한다 (D5).
    ///
    /// 왜 요소별 클램프가 아닌가: <see cref="ClampMagnitude"/>를 요소마다 적용하면 한계에 먼저 닿은
    /// 요소만 성장을 멈추고 나머지는 계속 커져 <b>그룹이 찢어진다</b>. 배율은 그룹 단위로 하나여야
    /// 하므로 재단도 그룹 단위여야 한다. 뒤집기(음수)는 그룹에 의미 있는 표현이 없으므로 차단한다.
    /// </summary>
    public static double ClampGroupFactor(double factor, IEnumerable<ElementTransformState> states)
    {
        if (double.IsNaN(factor))
        {
            return 1;
        }
        double upper = double.MaxValue;
        double lower = 0;
        bool any = false;
        foreach (var state in states)
        {
            any = true;
            double largest = Math.Max(Math.Abs(state.ScaleX), Math.Abs(state.ScaleY));
            double smallest = Math.Min(Math.Abs(state.ScaleX), Math.Abs(state.ScaleY));
            if (largest > 0)
            {
                upper = Math.Min(upper, MaxScale / largest);
            }
            // 이미 하한에 닿은 축은 하한 산출에서 **뺀다**. 넣으면 MinScale/MinScale == 1이 되어
            // 요소 하나의 납작해진 한 축이 그룹 전체의 축소를 통째로 봉쇄한다(측면 핸들로 한 요소를
            // 납작하게 만든 순간 그룹이 영원히 안 줄어드는 증상). 그 축은 ScaleAbout 안의
            // ClampMagnitude가 계속 MinScale에 붙들어 주므로 하한을 걸지 않아도 안전하다.
            if (smallest > MinScale)
            {
                lower = Math.Max(lower, MinScale / smallest);
            }
        }
        if (!any)
        {
            return 1;
        }
        // 하한은 **절대로 0까지 내려가지 않는다**. 구성원이 전부 바닥에 닿아 위 루프가 lower를
        // 한 번도 올리지 못하면 초기값 0이 남는데, 그때 커서를 앵커에 얹으면 factor가 0으로 통과한다.
        // f=0은 비가역 사상이라(t' = f(c+t−P)+P−c) 선택 요소들의 중심이 피벗 한 점으로 합쳐지고,
        // 이후 최대 배율로 되키워도 0×f = 0이라 제스처로는 영영 복구되지 않는다 (실행취소만이 유일한 길).
        double floor = Math.Max(lower, MinScale / MaxScale);
        // 하한이 상한을 넘으면(이미 한계에 걸린 요소가 섞임) 더 이상 키우지도 줄이지도 않는다.
        return floor > upper ? 1 : Math.Clamp(factor, floor, upper);
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
