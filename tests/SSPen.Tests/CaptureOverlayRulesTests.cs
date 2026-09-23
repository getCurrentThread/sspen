using System.Windows;
using SSPen.Annotation;
using SSPen.Capture;
using SSPen.Interop;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="CaptureOverlayRules"/>의 증인 (WI-11). 지키는 것과 고치는 것을 함께 잠근다:
/// 도구모음 밖 <b>제자리 클릭</b>은 여전히 기본 동작(핀)으로 끝나고(사용자 요청 15차),
/// <b>드래그</b>는 다시 고르는 것이다 — 예전에는 둘 다 즉시 핀으로 확정됐다.
/// </summary>
public class CaptureOverlayRulesTests
{
    /// <summary>기본 동작의 단일 소유자 — 배지·Enter·바깥 클릭이 이 값을 함께 본다.</summary>
    [Fact]
    public void DefaultAction_IsPin() => Assert.Equal(CaptureAction.Pin, CaptureOverlayRules.DefaultAction);

    /// <summary>
    /// 정지 임계값의 <b>값</b>은 선택 계층과 같은 3px이다. 척도는 다르다 — 캡처는 스냅샷 픽셀 체비셰프(≤),
    /// 선택 계층은 논리 픽셀 유클리드(&lt;)다 (<see cref="MovedPixels_Diagonal2_5ThenPointerVerdict_CommitsDefault"/>).
    /// </summary>
    [Fact]
    public void ClickThreshold_MatchesTheSelectionLayer() =>
        Assert.Equal(SelectionGestureRules.ClickThresholdPixels, CaptureOverlayRules.ClickThresholdPixels);

    [Theory]
    [InlineData(0.0)]
    [InlineData(3.0)]
    public void PointerVerdict_StationaryClickOutsideBar_CommitsDefault(double moved)
    {
        var verdict = CaptureOverlayRules.PointerVerdict(barVisible: true, insideBar: false, moved);

        Assert.Equal(CapturePointerVerdict.CommitDefault, verdict);
    }

    /// <summary>영역을 잘못 잡아 다시 끄는 것이 원치 않는 핀 창으로 끝나지 않는다.</summary>
    [Theory]
    [InlineData(3.5)]
    [InlineData(200.0)]
    public void PointerVerdict_DragOutsideBar_RestartsSelection(double moved)
    {
        var verdict = CaptureOverlayRules.PointerVerdict(barVisible: true, insideBar: false, moved);

        Assert.Equal(CapturePointerVerdict.RestartSelection, verdict);
    }

    /// <summary>도구모음 안은 버튼 자신의 몫이다 — 기본 동작이 복사·저장·취소를 삼키면 누를 수 없다.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(50.0)]
    public void PointerVerdict_InsideBar_IsIgnored(double moved)
    {
        var verdict = CaptureOverlayRules.PointerVerdict(barVisible: true, insideBar: true, moved);

        Assert.Equal(CapturePointerVerdict.Ignore, verdict);
    }

    /// <summary>아직 고르는 중이면 무엇을 해도 새 선택이다 (기본 동작으로 샐 경로가 없다).</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(100.0)]
    public void PointerVerdict_BeforeTheBarAppears_IsAlwaysANewSelection(double moved)
    {
        var verdict = CaptureOverlayRules.PointerVerdict(barVisible: false, insideBar: false, moved);

        Assert.Equal(CapturePointerVerdict.RestartSelection, verdict);
    }

    // ── 68단계(A7-5): 창 안에 있던 좌표·크기 판정. 여기가 유일한 증인이다 — 오버레이에서 영역을 끄는 테스트는 어느 스위트에도 없다.
    //    E2E CaptureSessionE2ETests는 StartCapture를 부르지만 실제 CaptureOverlayWindow는 뜨지 않는다: 오버레이는 ContextIdle 연속체에서
    //    만들어지는데 E2E 펌프(Background 우선순위)는 그보다 먼저 빠져나온다(75단계 측정: 펌프를 거듭해도 OverlayHwnd == 0).
    //    헤드리스 CaptureSessionControllerTests는 ICaptureOverlay 가짜를 꽂으므로(75단계) 역시 이 판정들을 거치지 않는다.

    /// <summary>목표 토폴로지 3×1920×1080, 원점 −1920 (AGENTS "Coordinate spaces").</summary>
    private static readonly PhysicalRect VirtualScreen = new(-1920, 0, 5760, 1080);

    /// <summary>
    /// 캔버스 좌표 → 물리 영역의 첫 변환(R2). 캔버스 단위 == 스냅샷 픽셀이므로 원점만 더한다 —
    /// 누가 DPI 곱셈을 "고치면" 여기서 빨갛게 된다.
    /// </summary>
    [Fact]
    public void ToPhysicalRegion_NegativeOrigin_AddsVirtualScreenOrigin()
    {
        var region = CaptureOverlayRules.ToPhysicalRegion(new Rect(20, 50, 200, 150), VirtualScreen);

        Assert.Equal(new PhysicalRect(-1900, 50, 200, 150), region);
    }

    /// <summary>빈 선택(취소·아무것도 안 고름)은 원점 보정 없이 0 사각형이다 — 원점을 더하면 (-1920,0)짜리 가짜 영역이 된다.</summary>
    [Fact]
    public void ToPhysicalRegion_Empty_ReturnsZeroRect()
    {
        var region = CaptureOverlayRules.ToPhysicalRegion(Rect.Empty, VirtualScreen);

        Assert.Equal(new PhysicalRect(0, 0, 0, 0), region);
    }

    /// <summary>반올림은 <see cref="Math.Round(double)"/> 기본값(짝수 쪽)이다: 2.5 → 2, 3.5 → 4. 위치와 크기가 같은 규칙을 탄다.</summary>
    [Theory]
    [InlineData(2.5, 2, 102)]
    [InlineData(3.5, 4, 104)]
    public void ToPhysicalRegion_HalfPixel_RoundsToEven(double value, int expectedOffset, int expectedExtent)
    {
        var region = CaptureOverlayRules.ToPhysicalRegion(new Rect(value, value, 100 + value, 100 + value), VirtualScreen);

        Assert.Equal(new PhysicalRect(-1920 + expectedOffset, expectedOffset, expectedExtent, expectedExtent), region);
    }

    /// <summary>
    /// 두 단계 왕복: 오버레이가 만든 물리 영역을 <see cref="CaptureService.RegionToBitmapOffset"/>에 넣으면
    /// 스냅샷 비트맵 안의 선택 원점이 그대로 돌아와야 한다 (사용자가 본 곳 == 잘라 내는 곳).
    /// </summary>
    [Fact]
    public void ToPhysicalRegion_ThenRegionToBitmapOffset_ReturnsSelectionOrigin()
    {
        var region = CaptureOverlayRules.ToPhysicalRegion(new Rect(20, 50, 200, 150), VirtualScreen);

        var (x, y) = CaptureService.RegionToBitmapOffset(region, VirtualScreen);

        Assert.Equal((20, 50), (x, y));
    }

    /// <summary>4px 미만은 폭이나 높이 <b>한쪽만</b> 작아도 무시한다. 정확히 4px는 유효한 선택이다.</summary>
    [Theory]
    [InlineData(3.9, 100.0, true)]
    [InlineData(100.0, 3.9, true)]
    [InlineData(4.0, 4.0, false)]
    [InlineData(200.0, 150.0, false)]
    public void IsTooSmall_WidthOrHeightUnderFourPixels_IsTooSmall(double width, double height, bool expected)
    {
        Assert.Equal(expected, CaptureOverlayRules.IsTooSmall(new Rect(10, 10, width, height)));
    }

    /// <summary>
    /// 빈 선택도 "너무 작다"이다 (<see cref="Rect.Empty"/>의 폭·높이는 음의 무한대). 도구모음이 뜨기 전 제자리 클릭은
    /// 선택이 빈 채로 이 검사에 닿으므로, 이 행이 "다시 고르기"로 떨어지는 경로를 지킨다.
    /// </summary>
    [Fact]
    public void IsTooSmall_EmptySelection_IsTrue() => Assert.True(CaptureOverlayRules.IsTooSmall(Rect.Empty));

    [Fact]
    public void MinSelectionPixels_IsFour() => Assert.Equal(4.0, CaptureOverlayRules.MinSelectionPixels);

    /// <summary>이동 거리는 체비셰프(축별 차이의 최댓값)다 — 방향과 무관하다.</summary>
    [Theory]
    [InlineData(0.0, 0.0, 2.0, 2.0, 2.0)]
    [InlineData(0.0, 0.0, 3.0, 0.0, 3.0)]
    [InlineData(5.0, 5.0, 2.0, 4.0, 3.0)]
    [InlineData(10.0, 10.0, 10.0, 10.0, 0.0)]
    public void MovedPixels_IsChebyshevDistance(double downX, double downY, double upX, double upY, double expected)
    {
        Assert.Equal(expected, CaptureOverlayRules.MovedPixels(new Point(downX, downY), new Point(upX, upY)));
    }

    /// <summary>
    /// 현행 특성화: 대각선 (2.5, 2.5) 이동은 체비셰프로 2.5 ≤ 3이라 기본 동작(핀)으로 확정된다. 선택 계층의 유클리드 척도였다면
    /// 3.54 &gt; 3으로 "다시 고르기"다 — 두 계층의 척도가 다르다는 사실을 여기서 잠근다 (클래스 문서 참조).
    /// </summary>
    [Fact]
    public void MovedPixels_Diagonal2_5ThenPointerVerdict_CommitsDefault()
    {
        double moved = CaptureOverlayRules.MovedPixels(new Point(0, 0), new Point(2.5, 2.5));

        var verdict = CaptureOverlayRules.PointerVerdict(barVisible: true, insideBar: false, moved);

        Assert.Equal(CapturePointerVerdict.CommitDefault, verdict);
    }

    /// <summary>평상시: 도구모음 오른쪽 끝을 선택 오른쪽 변 근처(−240)에, 선택 아래 8px에 둔다.</summary>
    [Fact]
    public void ActionBarOrigin_RoomBelow_SitsUnderSelectionRightAligned()
    {
        var origin = CaptureOverlayRules.ActionBarOrigin(new Rect(100, 100, 400, 200), VirtualScreen);

        Assert.Equal(new Point(260, 308), origin);
    }

    /// <summary>아래 가장자리에 걸리면(y &gt; H−44) 선택 위로 44px 뒤집는다.</summary>
    [Fact]
    public void ActionBarOrigin_NearBottomEdge_FlipsAboveSelection()
    {
        var origin = CaptureOverlayRules.ActionBarOrigin(new Rect(100, 1000, 400, 40), VirtualScreen);

        Assert.Equal(new Point(260, 956), origin);
    }

    /// <summary>뒤집기 조건은 엄격한 부등호다 — 아래 여유가 정확히 44px면 그대로 아래에 둔다.</summary>
    [Fact]
    public void ActionBarOrigin_ExactlyAtFlipLine_StaysBelow()
    {
        var origin = CaptureOverlayRules.ActionBarOrigin(new Rect(100, 928, 400, 100), VirtualScreen);

        Assert.Equal(new Point(260, 1036), origin);
    }

    [Fact]
    public void ActionBarOrigin_NearLeftEdge_ClampsToZero()
    {
        var origin = CaptureOverlayRules.ActionBarOrigin(new Rect(0, 100, 100, 100), VirtualScreen);

        Assert.Equal(new Point(0, 208), origin);
    }

    /// <summary>오른쪽 끝에서는 왼쪽 좌표가 W−250에서 멈춘다 (도구모음이 화면 밖으로 나가지 않는다).</summary>
    [Fact]
    public void ActionBarOrigin_NearRightEdge_ClampsToMaxLeft()
    {
        var origin = CaptureOverlayRules.ActionBarOrigin(new Rect(5700, 100, 60, 100), VirtualScreen);

        Assert.Equal(new Point(5510, 208), origin);
    }

    /// <summary>화면 높이를 거의 다 쓴 선택은 뒤집어도 위쪽 여유가 없다 — 0에서 멈춘다.</summary>
    [Fact]
    public void ActionBarOrigin_NearTop_WhenFlipped_ClampsToZero()
    {
        var origin = CaptureOverlayRules.ActionBarOrigin(new Rect(0, 20, 500, 1050), VirtualScreen);

        Assert.Equal(new Point(260, 0), origin);
    }

    /// <summary>도구모음은 캔버스 좌표다 — 가상 스크린의 원점은 쓰지 않고 크기만 본다.</summary>
    [Fact]
    public void ActionBarOrigin_VirtualScreenOrigin_DoesNotShiftTheBar()
    {
        var selection = new Rect(100, 100, 400, 200);

        var negative = CaptureOverlayRules.ActionBarOrigin(selection, new PhysicalRect(-1920, 0, 5760, 1080));
        var zero = CaptureOverlayRules.ActionBarOrigin(selection, new PhysicalRect(0, 0, 5760, 1080));

        Assert.Equal(zero, negative);
    }
}
