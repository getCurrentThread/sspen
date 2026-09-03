using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="ZBandOrder"/>의 증인 (33단계, ARCH-5/R10). 토스트 &gt; 설정창 &gt; 오버레이 &gt; 툴바 &gt; 서피스들 &gt; 핀들 순서와
/// HWND 0 제외를 고정한다. 통합 TopmostGuard/AnchorBelow 테스트는 SetWindowPos 쪽만 보므로 순서 정책은 여기가 유일한 증인이다.
/// </summary>
public class ZBandOrderTests
{
    [Fact]
    public void Build_FullSet_IsToastSettingsOverlayToolbarSurfacesPins()
    {
        var order = ZBandOrder.Build(toast: 1, settings: 2, overlay: 3, toolbar: 4, surfaces: [5, 6, 7], pins: [8, 9]);

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9], order.Select(h => (int)h).ToArray());
    }

    /// <summary>토스트는 설정창보다도 위다 — 설정 창을 띄운 채로도 알림이 보여야 한다.</summary>
    [Fact]
    public void Build_Toast_SitsAboveEverything()
    {
        var order = ZBandOrder.Build(toast: 99, settings: 1, overlay: 2, toolbar: 3, surfaces: [4], pins: [5]);

        Assert.Equal(99, (int)order[0]);
    }

    [Fact]
    public void Build_ZeroHandles_AreDropped()
    {
        var order = ZBandOrder.Build(toast: 0, settings: 0, overlay: 0, toolbar: 3, surfaces: [4, 0, 6], pins: [0]);

        Assert.Equal([3, 4, 6], order.Select(h => (int)h).ToArray());
    }

    [Fact]
    public void Build_Nothing_IsEmpty() => Assert.Empty(ZBandOrder.Build(0, 0, 0, 0, [], []));

    [Fact]
    public void Build_PreservesSurfaceAndPinEnumerationOrder()
    {
        var order = ZBandOrder.Build(0, 0, 0, 0, surfaces: [30, 10, 20], pins: [3, 1, 2]);

        Assert.Equal([30, 10, 20, 3, 1, 2], order.Select(h => (int)h).ToArray());
    }
}
