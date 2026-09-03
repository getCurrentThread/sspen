namespace SSPen.Pin;

/// <summary>핀 크롬 한 상태: 호버 도구모음을 보일지, 통과 표식을 보일지, 배율을 뭐라고 적을지.</summary>
public readonly record struct PinChromeState(bool ShowChrome, bool ShowClickThroughBadge, string ZoomPercent);

/// <summary>
/// 핀 창 어포던스 판정 (AC-14..18).
///
/// 이전에는 핀의 <b>모든</b> 조작이 문서화되지 않은 제스처였다 — 드래그=이동, 더블클릭=닫기,
/// 휠=확대, Ctrl+휠=투명도, Ctrl+가운데=클릭 통과. 화면에는 1px 테두리 말고 아무 단서가 없고
/// 작업 표시줄 항목조차 없어, 제스처를 모르면 창을 닫을 방법이 없었다.
///
/// 두 표식을 구분하는 이유:
/// <list type="bullet">
///   <item><b>호버 크롬</b>은 마우스가 올라왔을 때만 나온다 — 핀은 보라고 띄운 그림이라 상시 크롬은 그림을 가린다.</item>
///   <item><b>통과 표식</b>은 상시다. 클릭 통과 상태에서는 창이 마우스를 아예 받지 못해 호버가 성립하지 않는다 —
///     호버 크롬으로 알리려는 시도는 정의상 실패한다. 예전의 유일한 단서는 <c>Opacity</c>를 0.85로 낮추는 것이었는데,
///     사용자가 이미 그보다 투명하게 해 두었으면 <b>아무 변화도 없었다</b>.</item>
/// </list>
/// </summary>
public static class PinChromeRules
{
    /// <summary>크롬 세 버튼이 그림을 통째로 덮지 않으려면 필요한 최소 크기 (물리 픽셀 아님 — 창의 논리 크기).</summary>
    public const double MinChromeWidth = 96;
    public const double MinChromeHeight = 48;

    public static PinChromeState Resolve(bool mouseOver, bool clickThrough, double scale, double width, double height)
    {
        bool roomy = width >= MinChromeWidth && height >= MinChromeHeight;
        return new PinChromeState(
            // 통과 중에는 호버 자체가 성립하지 않으므로 크롬을 그리지 않는다 (그려도 누를 수 없다).
            ShowChrome: mouseOver && !clickThrough && roomy,
            ShowClickThroughBadge: clickThrough,
            ZoomPercent: FormatZoom(scale));
    }

    /// <summary>"100%" 형태의 배율 표기. 사용자가 지금 몇 배로 보고 있는지는 화면 어디에도 없던 정보다.</summary>
    public static string FormatZoom(double scale) => $"{Math.Round(scale * 100)}%";
}
