using System.Windows;

namespace SSPen.Annotation;

/// <summary>
/// 도형 제스처의 순수 판정 (Round 13). 미리보기와 커밋이 <b>같은 판정</b>을 쓰도록 강제하는 단일 소유 지점이다.
///
/// 이전에는 Shift 종점 해석이 <c>OnMouseMove</c>의 미리보기 갱신과 <c>CommitShape</c>에 <b>두 벌</b>로 적혀 있었다.
/// 한쪽만 고치면 화면에 그려진 정사각형이 마우스를 뗀 순간 직사각형으로 확정되는 식으로 조용히 어긋난다.
/// </summary>
public static class ShapeGestureRules
{
    /// <summary>
    /// 드래그 종점에 Shift 제약을 적용한 <b>실효 종점</b>. 미리보기와 커밋이 반드시 이 함수 하나를 경유한다 (D3).
    /// <paramref name="shift"/>는 호출부가 <c>KeyboardState.Shift</c>로 읽어 넘긴다 — 이 파일은 수식키를 직접 읽지 않는다.
    ///
    /// 도형별 분기(선·화살표 = 15도 스냅, 사각형·타원 = 정사각형/정원)는 <b>여기 한 곳</b>이다 —
    /// <see cref="ShiftConstraints"/>는 각도·정규화 수학만 갖고 도형 어휘를 모른다 (22단계). 4단계(834abba)의
    /// 리뷰 게이트 "판정이 하나뿐"은 이제 "호출자 한 곳"이 아니라 "함수 한 곳"이다.
    /// </summary>
    public static Point ResolveEnd(ShapeKind kind, Point start, Point raw, bool shift) =>
        !shift ? raw : kind switch
        {
            ShapeKind.Line or ShapeKind.Arrow => ShiftConstraints.SnapAngle(start, raw),
            ShapeKind.Rectangle or ShapeKind.Ellipse => ShiftConstraints.NormalizeSquare(start, raw),
            _ => raw,
        };

    /// <summary>
    /// 이 드래그가 도형을 만들 만큼 움직였는가. <b>클릭만으로는 도형을 만들지 않는다.</b>
    /// 임계는 선택 제스처의 '제자리 클릭' 임계와 같은 값을 <b>읽는다</b> —
    /// 리터럴을 다시 적으면 같은 양에 이름이 둘 생겨 드리프트가 늘어난다.
    /// </summary>
    public static bool ShouldCommit(Point start, Point end) =>
        (end - start).Length >= SelectionGestureRules.ClickThresholdPixels;
}

/// <summary>
/// 텍스트 커밋의 순수 판정 (ARCH-2 / Round 13). <b>TextBox 수명·NOACTIVATE 핸드셰이크·포커스 순서는
/// 이 파일의 관심사가 아니며</b> 전부 <see cref="SurfaceInputController"/>의 얇은 UI 어댑터에 남는다.
/// </summary>
public static class TextCommitRules
{
    /// <summary>
    /// 텍스트 편집기·측정·확정 후 렌더가 <b>모두 같은 글꼴</b>이어야 한다.
    /// 측정에 쓴 글꼴과 그린 글꼴이 갈리면 <see cref="TextElement.MeasuredSize"/>에서 유도되는
    /// <c>Bounds</c>가 실제 글자와 어긋나 히트테스트·선택 프레임이 빗나간다.
    /// 사용자에게 보이는 문자열이 아니므로 <c>Shell/Strings.cs</c>가 아니라 여기가 제자리다.
    /// </summary>
    public const string FontFamilyName = "맑은 고딕";

    /// <summary>측정 결과의 축별 하한 (논리 픽셀). 0 크기 상자는 다시 고르지도 지우지도 못한다.</summary>
    public const double MinMeasuredExtent = 8;

    /// <summary>
    /// 편집 상자의 배경색.
    ///
    /// 예전 값은 <c>#22FFFFFF</c>(13% 흰색) 하나였다. 그 아래는 <b>사용자의 화면 아무거나</b>이므로,
    /// 흰 글자를 밝은 바탕 위에 치면 자기가 무엇을 쓰고 있는지 보이지 않는다 — 확정하기 전까지
    /// 확인할 방법이 없다는 뜻이다. 글자색이 밝으면 어두운 스크림을, 어두우면 밝은 스크림을 깐다.
    ///
    /// 밝기 판정에 WCAG 상대 휘도를 쓰지 않는 이유: 여기서 필요한 것은 두 값 중 하나를 고르는
    /// 이분 판정뿐이고, 이 계층은 셸(<c>ShellPalette</c>)을 참조할 수 없다.
    /// </summary>
    public static System.Windows.Media.Color EditorBackdrop(System.Windows.Media.Color textColor) =>
        IsLight(textColor)
            ? System.Windows.Media.Color.FromArgb(0xB8, 0x1E, 0x1E, 0x1E)
            : System.Windows.Media.Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF);

    /// <summary>지각 밝기 근사 (Rec. 601) — 0.5를 경계로 밝음/어두움을 가른다.</summary>
    public static bool IsLight(System.Windows.Media.Color color) =>
        (((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255.0) >= 0.5;

    /// <summary>입력이 실제 텍스트 요소를 만들 자격이 있는가 — 공백만 친 편집은 버린다.</summary>
    public static bool ProducesElement(string? text) => !string.IsNullOrWhiteSpace(text);

    /// <summary>
    /// 측정치에 하한을 씌운다. 호출부는 반드시 <c>WidthIncludingTrailingWhitespace</c>를 넘긴다
    /// (뒤따르는 공백도 상자에 포함되어야 한다 — <c>Width</c>로 바꾸지 말 것).
    /// </summary>
    public static Size FloorMeasured(Size measured) =>
        new(Math.Max(measured.Width, MinMeasuredExtent), Math.Max(measured.Height, MinMeasuredExtent));
}
