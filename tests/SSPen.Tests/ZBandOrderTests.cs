using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="ZBandOrder"/>의 증인 (33단계, ARCH-5/R10). 설정창 &gt; 오버레이 &gt; 툴바 &gt; 서피스들 &gt; 핀들 순서와
/// HWND 0 제외를 고정한다. 통합 TopmostGuard/AnchorBelow 테스트는 SetWindowPos 쪽만 보므로 순서 정책은 여기가 유일한 증인이다.
/// </summary>
public class ZBandOrderTests
{
    [Fact]
    public void Build_FullSet_IsSettingsOverlayToolbarSurfacesPins()
    {
        var order = ZBandOrder.Build(settings: 1, overlay: 2, toolbar: 3, surfaces: [4, 5, 6], pins: [7, 8]);

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], order.Select(h => (int)h).ToArray());
    }

    [Fact]
    public void Build_ZeroHandles_AreDropped()
    {
        var order = ZBandOrder.Build(settings: 0, overlay: 0, toolbar: 3, surfaces: [4, 0, 6], pins: [0]);

        Assert.Equal([3, 4, 6], order.Select(h => (int)h).ToArray());
    }

    [Fact]
    public void Build_Nothing_IsEmpty() => Assert.Empty(ZBandOrder.Build(0, 0, 0, [], []));

    [Fact]
    public void Build_PreservesSurfaceAndPinEnumerationOrder()
    {
        var order = ZBandOrder.Build(0, 0, 0, surfaces: [30, 10, 20], pins: [3, 1, 2]);

        Assert.Equal([30, 10, 20, 3, 1, 2], order.Select(h => (int)h).ToArray());
    }
}
