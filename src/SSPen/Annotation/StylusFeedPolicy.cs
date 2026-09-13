namespace SSPen.Annotation;

/// <summary>스타일러스 패킷이 들어올 수 있는 두 창 이벤트 채널.</summary>
public enum StylusFeedChannel
{
    /// <summary><c>OnStylusMove</c> — WISP가 주는 원본 패킷 배치 (필압 포함).</summary>
    StylusMove,

    /// <summary><c>OnMouseMove</c> — 처리되지 않은 스타일러스 이동을 WISP가 마우스로 승격한 것. <c>e.StylusDevice</c>가 채워진다.</summary>
    PromotedMouseMove,
}

/// <summary>
/// 스타일러스 패킷 <b>단일 채널</b> 정책 (54단계, R8 회귀 수정).
///
/// 배경: 승격된 <c>MouseMove</c>의 <c>StylusDevice.GetStylusPoints</c>는 직전 <c>StylusMove</c>와 <b>같은 패킷 배치의 사본</b>이다
/// (WispLogic.PromoteMainToMouse — 별도 이벤트지 별도 데이터가 아니다). 두 채널이 모두 주입하면 배치마다 P1..Pn,P1..Pn의
/// 역주행이 생기고, <c>StrokeAccumulator</c>는 마지막 채택점 기준 1.5px만 거르므로 배치 폭이 1.5px를 넘는(빠른) 구간마다
/// 그 역주행이 살아남아 <c>FitToCurve</c>가 작은 고리로 렌더한다 (사용자 보고: 와콤 자유선 부풀음·회전 잔상).
///
/// 규칙: 스타일러스가 붙은 이벤트는 <see cref="StylusFeedChannel.StylusMove"/>만 주입한다. 승격 마우스 채널은 실제 마우스만
/// 주입한다. 승격 이벤트를 <c>Handled</c>로 막지는 않는다 — 제스처 수명(다운/업·캡처)은 승격된 마우스 이벤트에 기대고 있다.
/// </summary>
public static class StylusFeedPolicy
{
    /// <summary>
    /// 이 채널의 이 이벤트가 <c>PointerMove</c>에 패킷을 주입해야 하는가.
    /// <paramref name="stylusBacked"/>: 이벤트에 스타일러스 장치가 붙어 있는가.
    /// <paramref name="contact"/>: 접촉 중인가 (스타일러스는 <c>!InAir</c>, 마우스는 왼쪽 버튼 눌림).
    /// </summary>
    public static bool Feeds(StylusFeedChannel channel, bool stylusBacked, bool contact)
    {
        if (!contact)
        {
            return false;
        }
        return channel switch
        {
            StylusFeedChannel.StylusMove => stylusBacked,
            StylusFeedChannel.PromotedMouseMove => !stylusBacked,
            _ => false,
        };
    }
}
