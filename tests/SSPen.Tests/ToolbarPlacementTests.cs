using System.Windows;
using SSPen.Interop;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="ToolbarPlacement"/>의 증인 (34단계, AC-21/CRIT-17). E2E 시동은 툴바 위치를 단언하지 않으므로 이 표가 유일한 증인이다.
/// 회귀 대상: 물리 픽셀을 DIP에 대입해 150% 화면에서 첫 실행 툴바가 화면 밖에 놓이던 버그와,
/// 손으로 유지하던 <c>StripHeight</c> 상수가 틀어질 때 중앙이 밀리던 문제.
/// </summary>
public class ToolbarPlacementTests
{
    private static readonly PhysicalRect Primary = new(0, 0, 1920, 1080);
    private static readonly Rect VirtualScreenDip = new(0, 0, 1920, 1080);

    [Fact]
    public void Initial_NoSavedPosition_RightEdgeVerticalCenter()
    {
        var (left, top) = ToolbarPlacement.Initial(null, null, Primary);

        Assert.Equal(1920 - 34 - 12, left);
        Assert.Equal((1080 - 524) / 2.0, top);
    }

    [Theory]
    [InlineData(100.0, null)]
    [InlineData(null, 200.0)]
    public void Initial_OnlyOneSaved_UsesDefault(double? savedLeft, double? savedTop)
    {
        var (left, top) = ToolbarPlacement.Initial(savedLeft, savedTop, Primary);

        Assert.Equal(1874, left);
        Assert.Equal(278, top);
    }

    /// <summary>저장 위치가 하나라도 없으면 복원이 아니다 — 호출자는 실측 배치로 넘어간다.</summary>
    [Theory]
    [InlineData(100.0, null)]
    [InlineData(null, 200.0)]
    [InlineData(null, null)]
    public void Restored_IncompleteSavedPosition_IsNull(double? savedLeft, double? savedTop)
    {
        Assert.Null(ToolbarPlacement.Restored(savedLeft, savedTop, VirtualScreenDip));
    }

    /// <summary>마이그레이션 방지 트립와이어: 화면 안의 정상 값은 바이트 그대로 나온다.</summary>
    [Fact]
    public void Restored_SavedPositionOnScreen_IsReturnedUnchanged()
    {
        var restored = ToolbarPlacement.Restored(123.5, 40.25, VirtualScreenDip);

        Assert.Equal((123.5, 40.25), restored);
    }

    /// <summary>
    /// 150% 배율에서 옛 코드가 남긴 값(물리 1874를 DIP로 저장)은 1280 DIP 화면 밖이다 — 끌어온다.
    /// 사라진 모니터·줄어든 해상도의 저장 위치도 같은 경로로 자가 치유된다.
    /// </summary>
    [Fact]
    public void Restored_SavedPositionOffTheVirtualScreen_IsClampedBackInside()
    {
        var dipScreen = new Rect(0, 0, 1280, 720); // 1920×1080 물리 화면의 150% DIP 크기.

        var (left, top) = ToolbarPlacement.Restored(1874, 278, dipScreen)!.Value;

        Assert.Equal(1280 - ToolbarPlacement.StripWidth, left);
        Assert.InRange(top, 0, 720 - ToolbarPlacement.MinVisibleHeight);
    }

    /// <summary>음수 원점 토폴로지에서 왼쪽 화면의 위치는 유효하다 — 0으로 끌어오면 안 된다.</summary>
    [Fact]
    public void Restored_NegativeOriginPosition_StaysOnTheLeftMonitor()
    {
        var dipScreen = new Rect(-1920, 0, 3840, 1080);

        var (left, _) = ToolbarPlacement.Restored(-1000, 100, dipScreen)!.Value;

        Assert.Equal(-1000, left);
    }

    /// <summary>토폴로지를 모르면(빈 사각형) 사용자의 값을 건드리지 않는다.</summary>
    [Fact]
    public void Restored_EmptyVirtualScreen_LeavesTheValueAlone()
    {
        Assert.Equal((900.0, 500.0), ToolbarPlacement.Restored(900, 500, Rect.Empty));
    }

    /// <summary>실측 배치는 물리 좌표로 계산한다 — 배율이 섞일 여지가 없다.</summary>
    [Fact]
    public void PhysicalOnPrimary_UsesMeasuredSize_NotTheFallbackConstant()
    {
        // 150% 화면: 스트립 34×600 DIP → 51×900 물리.
        var (x, y) = ToolbarPlacement.PhysicalOnPrimary(new PhysicalRect(0, 0, 1920, 1040), width: 51, height: 900, rightMargin: 18);

        Assert.Equal(1920 - 51 - 18, x);
        Assert.Equal((1040 - 900) / 2, y);
        Assert.NotEqual((int)ToolbarPlacement.StripHeight, 900); // 폴백 상수와 무관하다는 표시.
    }

    /// <summary>작업 영역을 쓴다 — 작업 표시줄을 덮지 않는다 (서피스 배치와 같은 규칙).</summary>
    [Fact]
    public void PhysicalOnPrimary_RespectsTheWorkAreaOrigin()
    {
        var (x, y) = ToolbarPlacement.PhysicalOnPrimary(new PhysicalRect(-1920, 40, 1920, 1000), width: 34, height: 500, rightMargin: 12);

        Assert.Equal(-1920 + 1920 - 34 - 12, x);
        Assert.Equal(40 + (1000 - 500) / 2, y);
    }

    /// <summary>스트립이 작업 영역보다 높아도 화면 밖으로 나가지 않는다 (세로 해상도가 낮은 화면).</summary>
    [Fact]
    public void PhysicalOnPrimary_StripTallerThanTheWorkArea_ClampsToTheTop()
    {
        var (_, y) = ToolbarPlacement.PhysicalOnPrimary(new PhysicalRect(0, 0, 1024, 400), width: 34, height: 900, rightMargin: 12);

        Assert.Equal(0, y);
    }

    /// <summary>34·12는 다른 두 양이라 46으로 합치지 않는다. StripHeight는 이제 레이아웃 전 폴백일 뿐이다.</summary>
    [Fact]
    public void Constants_AreTheMeasuredStripToday()
    {
        Assert.Equal(524, ToolbarPlacement.StripHeight);
        Assert.Equal(34, ToolbarPlacement.StripWidth);
        Assert.Equal(12, ToolbarPlacement.RightMargin);
        Assert.Equal(60, ToolbarPlacement.MinVisibleHeight);
    }
}
