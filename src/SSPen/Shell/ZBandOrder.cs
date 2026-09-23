namespace SSPen.Shell;

/// <summary>
/// z-밴드 HWND 순서 정책 (33단계, ARCH-5/R10): 토스트 &gt; 설정창 &gt; 캡처 오버레이(+액션바) &gt; 툴바 &gt; 핀들 &gt; 서피스들 &gt; 기타 앱.
///
/// 핀이 서피스 위인 것은 71단계 사용자 결정(2026-09-23)이다 — 예전 F5("잉크는 핀 위", 핀을 마지막 서피스 아래에 앵커)를 폐기했다.
/// 화이트/블랙 보드는 서피스 안의 사각형이라 서피스와 함께 맨 아래에 있고, 핀은 보드 위에서도 보인다.
/// 결과 동작: 판서 모드에서도 통과가 아닌 핀은 자기 영역의 클릭(드래그·휠)을 받고, 핀 영역에 그은 잉크는 핀에 가려진다.
/// 통과 핀(Ctrl+가운데 버튼)은 입력을 아래 서피스로 흘린다 — 되찾기 훅(<c>PinClickThroughMonitor</c>)과
/// 캡처의 as-seen 경로(BitBlt)는 순서와 무관해 그대로다.
///
/// 앵커(요청·결과 단계 훅이 창을 붙이는 자리)는 이 순서에서 파생된다: 핀은 툴바 아래, 서피스는 <see cref="SurfaceAnchor"/>.
///
/// 토스트가 맨 위인데도 안전한 이유: 그 창은 기본이 <c>WS_EX_TRANSPARENT</c>라 클릭을 삼킬 수 없다
/// (<see cref="ToastWindow"/> 문서 참조). 밴드에 넣지 않고 톱모스트로만 두면 매 <c>ApplyZBand</c>가
/// 오히려 토스트를 아래로 밀어 알림이 툴바 뒤로 숨는다 — 순서를 결정론으로 만들려면 목록의 멤버여야 한다.
/// 순서만 결정하고 <c>SetWindowPos</c>는 <c>WindowStyling.ApplyZBand</c>가, 언제 적용할지는 <c>AppController</c>가
/// 소유한다 — 호출 지점(AppState.Changed·PinsChanged·캡처·설정창·툴바 토글·일반 설정)은 이 파일이 늘리거나 줄이지 않는다
/// (AGENTS L14: 렌더 틱에서 부르는 것은 위반).
/// </summary>
public static class ZBandOrder
{
    /// <summary>위→아래 순서 — 인자 순서가 곧 밴드 순서다. 아직 만들어지지 않은 창(HWND 0)은 건너뛴다.</summary>
    public static List<nint> Build(nint toast, nint settings, nint overlay, nint toolbar, IEnumerable<nint> pins, IEnumerable<nint> surfaces)
    {
        var order = new List<nint>();
        Add(order, toast);
        Add(order, settings);
        Add(order, overlay);
        Add(order, toolbar);
        foreach (var hwnd in pins)
        {
            Add(order, hwnd);
        }
        foreach (var hwnd in surfaces)
        {
            Add(order, hwnd);
        }
        return order;
    }

    /// <summary>
    /// 서피스의 z-앵커 = 서피스 밴드 바로 위 창: HWND가 0이 아닌 마지막 핀(목록 순서 = 밴드 순서라 가장 아래 핀),
    /// 핀이 없으면 툴바, 툴바도 아직 없으면 0(훅 무동작). 툴바가 있으면 <see cref="Build"/>에서 첫 서피스 바로 위 항목과 같다.
    /// </summary>
    public static nint SurfaceAnchor(nint toolbar, IEnumerable<nint> pins)
    {
        nint anchor = toolbar;
        foreach (var hwnd in pins)
        {
            if (hwnd != 0)
            {
                anchor = hwnd;
            }
        }
        return anchor;
    }

    private static void Add(List<nint> order, nint hwnd)
    {
        if (hwnd != 0)
        {
            order.Add(hwnd);
        }
    }
}
