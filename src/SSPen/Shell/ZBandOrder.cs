namespace SSPen.Shell;

/// <summary>
/// z-밴드 HWND 순서 정책 (33단계, ARCH-5/R10): 설정창 &gt; 캡처 오버레이(+액션바) &gt; 툴바 &gt; 서피스들 &gt; 핀들 &gt; 기타 앱.
/// 순서만 결정하고 <c>SetWindowPos</c>는 <c>WindowStyling.ApplyZBand</c>가, 언제 적용할지는 <c>AppController</c>가
/// 소유한다 — 호출 지점(AppState.Changed·PinsChanged·캡처·설정창·툴바 토글·일반 설정)은 이 파일이 늘리거나 줄이지 않는다
/// (AGENTS L14: 렌더 틱에서 부르는 것은 위반).
/// </summary>
public static class ZBandOrder
{
    /// <summary>위→아래 순서. 아직 만들어지지 않은 창(HWND 0)은 건너뛴다.</summary>
    public static List<nint> Build(nint settings, nint overlay, nint toolbar, IEnumerable<nint> surfaces, IEnumerable<nint> pins)
    {
        var order = new List<nint>();
        Add(order, settings);
        Add(order, overlay);
        Add(order, toolbar);
        foreach (var hwnd in surfaces)
        {
            Add(order, hwnd);
        }
        foreach (var hwnd in pins)
        {
            Add(order, hwnd);
        }
        return order;
    }

    private static void Add(List<nint> order, nint hwnd)
    {
        if (hwnd != 0)
        {
            order.Add(hwnd);
        }
    }
}
