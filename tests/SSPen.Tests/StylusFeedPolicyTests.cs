using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 스타일러스 패킷 단일 채널 정책 (54단계, R8 회귀). 잠그는 것: 스타일러스가 붙은 이벤트는 <b>정확히 한 채널</b>만
/// 주입한다(StylusMove), 실제 마우스는 승격 채널만 주입한다, 비접촉(공중·버튼 뗌)은 어느 채널도 주입하지 않는다.
/// </summary>
public class StylusFeedPolicyTests
{
    [Theory]
    [InlineData(StylusFeedChannel.StylusMove, true, true, true)]
    [InlineData(StylusFeedChannel.PromotedMouseMove, true, true, false)] // 같은 패킷 배치의 사본 — 주입하면 역주행
    [InlineData(StylusFeedChannel.StylusMove, false, true, false)]
    [InlineData(StylusFeedChannel.PromotedMouseMove, false, true, true)] // 실제 마우스
    [InlineData(StylusFeedChannel.StylusMove, true, false, false)] // 공중 이동
    [InlineData(StylusFeedChannel.PromotedMouseMove, true, false, false)]
    [InlineData(StylusFeedChannel.StylusMove, false, false, false)]
    [InlineData(StylusFeedChannel.PromotedMouseMove, false, false, false)]
    public void Feeds_ChannelAndDevice_DecidesInjection(StylusFeedChannel channel, bool stylusBacked, bool contact, bool expected)
    {
        Assert.Equal(expected, StylusFeedPolicy.Feeds(channel, stylusBacked, contact));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Feeds_ContactEvent_ExactlyOneChannelFeeds(bool stylusBacked)
    {
        // 두 채널이 모두 주입하면 배치마다 P1..Pn,P1..Pn 역주행이 생기고, 둘 다 안 하면 획이 멈춘다 — 정확히 하나여야 한다.
        int feeding = new[] { StylusFeedChannel.StylusMove, StylusFeedChannel.PromotedMouseMove }
            .Count(c => StylusFeedPolicy.Feeds(c, stylusBacked, contact: true));

        Assert.Equal(1, feeding);
    }
}
