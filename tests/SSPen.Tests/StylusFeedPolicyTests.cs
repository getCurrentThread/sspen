using System.Windows.Input;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 스타일러스 패킷 단일 채널 정책 (54단계, R8 회귀). 잠그는 것: 스타일러스가 붙은 이벤트는 <b>정확히 한 채널</b>만
/// 주입한다(StylusMove), 실제 마우스는 승격 채널만 주입한다, 비접촉(공중·버튼 뗌)은 어느 채널도 주입하지 않는다.
/// 64단계(A2-1)부터 다운 필압 규칙(<see cref="StylusFeedPolicy.DownPressure"/>)도 여기서 본다.
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

    /// <summary>
    /// 트립와이어 (57단계, A2-7): 승격 마우스 채널 어댑터는 필압을 싣지 않는다. 스타일러스 패킷의 유일한 채널은 창의
    /// OnStylusMove이고(54단계 단일 채널), 이 어댑터에 필압 인자를 되살리면 승격 이동으로 같은 배치를 다시 넣던
    /// 1.3.3 역주행 경로가 API 차원에서 다시 열린다.
    /// </summary>
    [Fact]
    public void OnMouseMove_TakesOnlyMouseEventArgs_ByReflection()
    {
        var method = typeof(SurfaceInputController).GetMethod(nameof(SurfaceInputController.OnMouseMove));

        Assert.NotNull(method);
        var parameter = Assert.Single(method!.GetParameters());
        Assert.Equal(typeof(MouseEventArgs), parameter.ParameterType);
    }

    // ---- 다운 필압 (64단계, A2-1): 창이 꺼내던 규칙을 순수 함수로. StylusPointCollection은 MTA에서 만들어진다 (AGENTS L121). ----

    [Fact]
    public void DownPressure_Null_ReturnsDefaultPressure() =>
        Assert.Equal(StrokeGeometry.DefaultPressure, StylusFeedPolicy.DownPressure(null));

    [Fact]
    public void DownPressure_Empty_ReturnsDefaultPressure() =>
        Assert.Equal(StrokeGeometry.DefaultPressure, StylusFeedPolicy.DownPressure(new StylusPointCollection()));

    /// <summary>마지막 패킷의 필압을 쓴다. 클램프는 여기서 하지 않는다 — 0.05–1.0 클램프는 StrokeGeometry 단독 소유다 (31단계).</summary>
    [Fact]
    public void DownPressure_UsesLastPacket()
    {
        var points = new StylusPointCollection
        {
            new StylusPoint(0, 0, 0.2f),
            new StylusPoint(1, 1, 0.7f),
        };

        Assert.Equal(0.7f, StylusFeedPolicy.DownPressure(points));
    }

    /// <summary>
    /// 트립와이어 (64단계, A2-1): 다운 어댑터는 필압 인자를 받지 않는다 — 뒤집힘과 필압을 같은 <c>StylusDevice</c>에서 어댑터가 꺼낸다.
    /// 인자를 되살리면 창이 필압을, 컨트롤러가 뒤집힘을 꺼내던 이중 구조가 돌아온다.
    /// </summary>
    [Fact]
    public void OnMouseLeftButtonDown_TakesOnlyMouseButtonEventArgs_ByReflection()
    {
        var method = typeof(SurfaceInputController).GetMethod(nameof(SurfaceInputController.OnMouseLeftButtonDown));

        Assert.NotNull(method);
        var parameter = Assert.Single(method!.GetParameters());
        Assert.Equal(typeof(MouseButtonEventArgs), parameter.ParameterType);
    }
}
