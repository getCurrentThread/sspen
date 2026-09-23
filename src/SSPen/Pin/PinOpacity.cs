namespace SSPen.Pin;

/// <summary>
/// 핀 투명도 규칙 (AC-16, 81단계 A7-6) — Ctrl+휠 계단·클램프와 클릭 통과 때 어둡게 하는 상한.
/// 창(<see cref="PinWindow"/>) 안의 매직 넘버였던 것을 순수 코어로 뺐다. 확대 수학(<see cref="PinZoom"/>)과는 따로 둔다 —
/// PinZoom은 커서 고정 줌 수학만 소유한다.
/// </summary>
public static class PinOpacity
{
    /// <summary>Ctrl+휠 한 칸의 투명도 변화량.</summary>
    public const double Step = 0.05;

    /// <summary>가장 투명한 값 — 이보다 내려가면 핀이 보이지 않아 되찾을 수 없다.</summary>
    public const double Min = 0.15;

    public const double Max = 1.0;

    /// <summary>클릭 통과를 켤 때 적용하는 투명도 상한 (살짝 어둡게 하는 시각 힌트).</summary>
    public const double ClickThroughCeiling = 0.85;

    /// <summary>
    /// Ctrl+휠 한 칸 뒤의 투명도. <paramref name="wheelDelta"/>가 양수면 한 계단 불투명하게, 아니면 한 계단 투명하게 —
    /// 0도 투명 쪽이다(옛 창 코드의 <c>e.Delta &gt; 0 ? 0.05 : -0.05</c>와 같은 의미를 보존한다).
    /// </summary>
    public static double Next(double current, int wheelDelta) =>
        Math.Clamp(current + (wheelDelta > 0 ? Step : -Step), Min, Max);

    /// <summary>클릭 통과를 켤 때의 투명도 — 이미 더 투명하게 해 둔 값은 올리지 않는다.</summary>
    public static double DimForClickThrough(double current) => Math.Min(current, ClickThroughCeiling);
}
