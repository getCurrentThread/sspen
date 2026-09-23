namespace SSPen.Annotation;

/// <summary>
/// 굵기 단계 → 값 환산표 (30단계, R9). AppState에 흩어져 있던 세 표(펜 px, 텍스트 크기, 0..4 단계 클램프)를 한 곳에 둔다.
/// 단계 클램프는 증감 <see cref="Step"/>과 저장값 복원 <see cref="FromStored"/> 둘 다 열거 길이로 계산하고,
/// 도구 그룹 공통 기본 단계 <see cref="Default"/>도 여기 있다 (66단계, A4-5·A9-2).
/// 펜 px(2/4/6/10/16)와 텍스트 크기(12/16/24/36/48)는 같은 <see cref="ThicknessStep"/>에서 나오지만 <b>다른 양</b>이므로
/// 표를 합치지 않는다 (f70c3fb 교훈: 같은 double이라도 다른 양이면 하나로 묶지 않는다).
/// </summary>
public static class ThicknessScale
{
    /// <summary>
    /// 도구 그룹(펜·형광펜·도형) 공통 기본 굵기 단계 = 보통 (66단계). <see cref="AppState"/> 초기값이 읽는다.
    /// <c>AppSettings</c>의 정수 기본값 <c>2</c>는 JSON 표기 호환 때문에 리터럴로 남기고, 일치는 증인 테스트(ThicknessScaleTests)가 잠근다.
    /// </summary>
    public const ThicknessStep Default = ThicknessStep.Medium;

    /// <summary>
    /// 설정 파일의 저장값(정수) → 단계 (66단계). 범위 밖 값(손상·구버전 파일)은 양끝 단계로 재단한다.
    /// 상한을 리터럴 4가 아니라 열거 길이로 계산하므로, 단계가 늘어도 복원이 옛 상한에서 잘리지 않는다 (<see cref="Step"/>과 같은 규칙).
    /// </summary>
    public static ThicknessStep FromStored(int stored) =>
        (ThicknessStep)Math.Clamp(stored, 0, Enum.GetValues<ThicknessStep>().Length - 1);

    /// <summary>단계 → 펜 굵기 (논리 px), 5단계: 2/4/6/10/16.</summary>
    public static double PenPixels(ThicknessStep step) => step switch
    {
        ThicknessStep.XSmall => 2,
        ThicknessStep.Small => 4,
        ThicknessStep.Medium => 6,
        ThicknessStep.Large => 10,
        _ => 16,
    };

    /// <summary>형광펜 굵기 배수 — 펜 px의 3배.</summary>
    public const double HighlighterFactor = 3;

    /// <summary>단계 → 형광펜 굵기 (논리 px) = 펜 px × <see cref="HighlighterFactor"/>.</summary>
    public static double HighlighterPixels(ThicknessStep step) => PenPixels(step) * HighlighterFactor;

    /// <summary>단계 → 텍스트 크기 (도형 그룹 연동), 5단계: 12/16/24/36/48.</summary>
    public static double FontSize(ThicknessStep step) => step switch
    {
        ThicknessStep.XSmall => 12,
        ThicknessStep.Small => 16,
        ThicknessStep.Medium => 24,
        ThicknessStep.Large => 36,
        _ => 48,
    };

    /// <summary>한 단계 증감 (휠/핫키). 양끝(XSmall/XLarge)에서 멈춘다 — 열거 순서가 곧 단계 순서다.</summary>
    public static ThicknessStep Step(ThicknessStep current, int direction)
    {
        int last = Enum.GetValues<ThicknessStep>().Length - 1;
        return (ThicknessStep)Math.Clamp((int)current + direction, 0, last);
    }
}
