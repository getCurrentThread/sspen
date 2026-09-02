namespace SSPen.Annotation;

/// <summary>
/// 굵기 단계 → 값 환산표 (30단계, R9). AppState에 흩어져 있던 세 표(펜 px, 텍스트 크기, 0..4 단계 클램프)를 한 곳에 둔다.
/// 펜 px(2/4/6/10/16)와 텍스트 크기(12/16/24/36/48)는 같은 <see cref="ThicknessStep"/>에서 나오지만 <b>다른 양</b>이므로
/// 표를 합치지 않는다 (f70c3fb 교훈: 같은 double이라도 다른 양이면 하나로 묶지 않는다).
/// </summary>
public static class ThicknessScale
{
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
