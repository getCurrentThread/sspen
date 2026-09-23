using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="ZBandOrder"/>의 증인 (33단계, ARCH-5/R10; 71단계 사용자 결정으로 핀이 서피스 위).
/// 토스트 &gt; 설정창 &gt; 오버레이 &gt; 툴바 &gt; 핀들 &gt; 서피스들 순서와 HWND 0 제외를 고정한다.
/// 통합 TopmostGuard/AnchorBelow 테스트는 SetWindowPos 쪽만 보므로 순서 정책은 여기가 유일한 증인이다.
/// </summary>
public class ZBandOrderTests
{
    [Fact]
    public void Build_FullSet_IsToastSettingsOverlayToolbarPinsSurfaces()
    {
        var order = ZBandOrder.Build(toast: 1, settings: 2, overlay: 3, toolbar: 4, pins: [5, 6], surfaces: [7, 8, 9]);

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9], order.Select(h => (int)h).ToArray());
    }

    /// <summary>토스트는 설정창보다도 위다 — 설정 창을 띄운 채로도 알림이 보여야 한다.</summary>
    [Fact]
    public void Build_Toast_SitsAboveEverything()
    {
        var order = ZBandOrder.Build(toast: 99, settings: 1, overlay: 2, toolbar: 3, pins: [4], surfaces: [5]);

        Assert.Equal(99, (int)order[0]);
    }

    /// <summary>
    /// 71단계 사용자 결정(2026-09-23): 핀 고정 캡처는 툴바 바로 아래, 판서 서피스(보드 포함)보다 위다 —
    /// 예전 "잉크는 핀 위" 규칙을 뒤집는다. 툴바는 여전히 핀보다 위라 핀이 툴바를 가리지 못한다.
    /// </summary>
    [Fact]
    public void Build_PinsSitBetweenToolbarAndSurfaces()
    {
        var order = ZBandOrder.Build(toast: 0, settings: 0, overlay: 0, toolbar: 10, pins: [20, 21], surfaces: [30, 31]);
        int toolbar = order.IndexOf(10);
        int lastPin = order.IndexOf(21);
        int firstSurface = order.IndexOf(30);

        Assert.True(toolbar < order.IndexOf(20));
        Assert.True(lastPin < firstSurface);
    }

    [Fact]
    public void Build_ZeroHandles_AreDropped()
    {
        var order = ZBandOrder.Build(toast: 0, settings: 0, overlay: 0, toolbar: 3, pins: [0], surfaces: [4, 0, 6]);

        Assert.Equal([3, 4, 6], order.Select(h => (int)h).ToArray());
    }

    [Fact]
    public void Build_Nothing_IsEmpty() => Assert.Empty(ZBandOrder.Build(0, 0, 0, 0, [], []));

    [Fact]
    public void Build_PreservesPinAndSurfaceEnumerationOrder()
    {
        var order = ZBandOrder.Build(0, 0, 0, 0, pins: [3, 1, 2], surfaces: [30, 10, 20]);

        Assert.Equal([3, 1, 2, 30, 10, 20], order.Select(h => (int)h).ToArray());
    }

    // ---- 서피스 z-앵커 (71단계): 서피스 밴드 바로 위 창 = Hwnd가 0이 아닌 마지막 핀, 없으면 툴바 ----

    [Fact]
    public void SurfaceAnchor_NoPins_IsToolbar() => Assert.Equal(4, (int)ZBandOrder.SurfaceAnchor(toolbar: 4, pins: []));

    /// <summary>아직 HWND가 없는 핀(Show 전)은 앵커가 될 수 없다 — 0으로 돌리면 서피스 훅이 통째로 쉰다.</summary>
    [Fact]
    public void SurfaceAnchor_ZeroHandlePin_IsSkipped()
    {
        Assert.Equal(7, (int)ZBandOrder.SurfaceAnchor(toolbar: 4, pins: [7, 0]));
        Assert.Equal(4, (int)ZBandOrder.SurfaceAnchor(toolbar: 4, pins: [0]));
    }

    /// <summary>목록의 마지막 핀이 밴드에서 가장 아래 핀이다 — 서피스는 그 바로 아래에 붙는다.</summary>
    [Fact]
    public void SurfaceAnchor_ManyPins_IsLastInList() =>
        Assert.Equal(7, (int)ZBandOrder.SurfaceAnchor(toolbar: 4, pins: [5, 6, 7]));

    /// <summary>시동 중 툴바·핀이 아직 없으면 0 — AnchorBelow/KeepBelow는 앵커 0에서 아무것도 하지 않는다.</summary>
    [Fact]
    public void SurfaceAnchor_NoToolbarNoPins_IsZero()
    {
        Assert.Equal(0, (int)ZBandOrder.SurfaceAnchor(toolbar: 0, pins: []));
        Assert.Equal(0, (int)ZBandOrder.SurfaceAnchor(toolbar: 0, pins: [0, 0]));
    }

    /// <summary>
    /// 교차 증인: 앵커는 <see cref="ZBandOrder.Build"/>가 만든 순서에서 첫 서피스 바로 위 항목과 같다 —
    /// 요청·결과 단계 훅(AnchorBelow/KeepBelow)이 붙이는 자리와 ApplyZBand가 놓는 자리가 어긋나면 사후 검증이 헛돈다.
    /// </summary>
    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 5 })]
    [InlineData(new[] { 5, 0, 6 })]
    [InlineData(new[] { 6, 5, 0 })]
    public void SurfaceAnchor_EqualsEntryDirectlyAboveFirstSurface(int[] pins)
    {
        var pinHwnds = pins.Select(p => (nint)p).ToArray();
        var order = ZBandOrder.Build(toast: 1, settings: 2, overlay: 3, toolbar: 4, pins: pinHwnds, surfaces: [30, 31]);

        Assert.Equal(order[order.IndexOf(30) - 1], ZBandOrder.SurfaceAnchor(toolbar: 4, pins: pinHwnds));
    }
}
