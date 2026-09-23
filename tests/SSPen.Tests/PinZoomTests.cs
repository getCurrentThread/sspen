using SSPen.Interop;
using SSPen.Pin;
using Xunit;

namespace SSPen.Tests;

/// <summary>핀 창 커서 고정 확대/축소 (사용자 요청 15차).</summary>
public class PinZoomTests
{
    [Fact]
    public void NextScale_PositiveDelta_ZoomsIn()
    {
        Assert.Equal(1.1, PinZoom.NextScale(1.0, 120), 9);
    }

    [Fact]
    public void NextScale_NegativeDelta_ZoomsOut()
    {
        Assert.Equal(1.0 / 1.1, PinZoom.NextScale(1.0, -120), 9);
    }

    [Fact]
    public void NextScale_RoundTrip_ReturnsToOrigin()
    {
        // 한 칸 확대 후 한 칸 축소하면 정확히 원래 배율이어야 한다 (누적 드리프트 방어).
        double up = PinZoom.NextScale(1.0, 120);

        Assert.Equal(1.0, PinZoom.NextScale(up, -120), 9);
    }

    [Fact]
    public void NextScale_ClampsToRange()
    {
        double huge = 1.0;
        for (int i = 0; i < 200; i++) { huge = PinZoom.NextScale(huge, 120); }
        double tiny = 1.0;
        for (int i = 0; i < 200; i++) { tiny = PinZoom.NextScale(tiny, -120); }

        Assert.Equal(PinZoom.MaxScale, huge, 9);
        Assert.Equal(PinZoom.MinScale, tiny, 9);
    }

    [Fact]
    public void ZoomAtCursor_PointUnderCursor_StaysUnderCursor()
    {
        // 핵심 계약: 커서가 가리키던 그림 지점의 화면 좌표가 확대 후에도 같아야 한다.
        double left = 100, top = 50, baseW = 400, baseH = 300;
        double cursorX = 120, cursorY = 90;
        double scale = 1.0;

        var result = PinZoom.ZoomAtCursor(At(scale, left, top, baseW, baseH), 120, baseW, baseH, cursorX, cursorY);

        // 확대 전 커서가 가리키던 정규화 위치.
        double t = cursorX / (baseW * scale);
        double u = cursorY / (baseH * scale);
        // 확대 후 같은 정규화 위치의 화면 좌표.
        double afterX = result.Left + t * result.Width;
        double afterY = result.Top + u * result.Height;

        Assert.Equal(left + cursorX, afterX, 9);
        Assert.Equal(top + cursorY, afterY, 9);
    }

    [Fact]
    public void ZoomAtCursor_ShrinkingAlsoKeepsCursorAnchored()
    {
        // 사용자가 명시한 목적: 줄일 때 커서가 그림 밖으로 벗어나지 않아야 한다.
        double left = -300, top = 20, baseW = 640, baseH = 480;
        double cursorX = 500, cursorY = 400;
        double scale = 2.0;

        var result = PinZoom.ZoomAtCursor(At(scale, left, top, baseW, baseH), -120, baseW, baseH, cursorX, cursorY);

        double t = cursorX / (baseW * scale);
        double u = cursorY / (baseH * scale);

        Assert.True(result.Width < baseW * scale, "축소여야 한다");
        Assert.Equal(left + cursorX, result.Left + t * result.Width, 9);
        Assert.Equal(top + cursorY, result.Top + u * result.Height, 9);
    }

    [Fact]
    public void ZoomAtCursor_CursorAtTopLeft_LeavesOriginUnmoved()
    {
        // 좌상단이 고정점이면 원점은 움직이지 않는다 (기존 동작과 동일한 특수 케이스).
        var result = PinZoom.ZoomAtCursor(At(1.0, 200, 150, 400, 300), 120, 400, 300, cursorX: 0, cursorY: 0);

        Assert.Equal(200, result.Left, 9);
        Assert.Equal(150, result.Top, 9);
    }

    [Fact]
    public void ZoomAtCursor_AtMaxScale_DoesNotMoveWindow()
    {
        // 배율이 클램프에 걸려 변하지 않으면 창도 그대로여야 한다 — 그렇지 않으면
        // 최대 배율에서 휠을 굴릴 때마다 창이 스르륵 밀려난다.
        var result = PinZoom.ZoomAtCursor(
            At(PinZoom.MaxScale, 10, 20, 400, 300), 120, 400, 300, cursorX: 300, cursorY: 200);

        Assert.Equal(PinZoom.MaxScale, result.Scale, 9);
        Assert.Equal(10, result.Left, 9);
        Assert.Equal(20, result.Top, 9);
    }

    [Fact]
    public void ZoomAtCursor_NegativeOrigin_HandlesLeftMonitor()
    {
        // 대상 토폴로지의 원점은 음수(-1920,0)다. 부호가 뒤집혀도 고정점이 유지되어야 한다.
        double left = -1800, top = 300, baseW = 500, baseH = 400;
        double cursorX = 250, cursorY = 200;

        var result = PinZoom.ZoomAtCursor(At(1.0, left, top, baseW, baseH), 120, baseW, baseH, cursorX, cursorY);

        double t = cursorX / baseW;
        double u = cursorY / baseH;

        Assert.Equal(left + cursorX, result.Left + t * result.Width, 9);
        Assert.Equal(top + cursorY, result.Top + u * result.Height, 9);
        Assert.True(result.Left < 0, "음수 원점이 유지되어야 한다");
    }

    [Fact]
    public void ZoomAtCursor_ScaleUnchanged_WidthMatchesBaseTimesScale()
    {
        var result = PinZoom.ZoomAtCursor(At(1.5, 0, 0, 200, 100), 120, 200, 100, 50, 25);

        Assert.Equal(200 * result.Scale, result.Width, 9);
        Assert.Equal(100 * result.Scale, result.Height, 9);
    }

    /// <summary>원래 크기 복귀는 배율 1과 기준 크기를 돌려준다.</summary>
    [Fact]
    public void ResetToOriginal_ReturnsBaseSizeAtScaleOne()
    {
        var result = PinZoom.ResetToOriginal(At(3.0, 100, 50, 200, 100), baseWidth: 200, baseHeight: 100);

        Assert.Equal(1.0, result.Scale);
        Assert.Equal(200, result.Width);
        Assert.Equal(100, result.Height);
    }

    /// <summary>
    /// 중심을 고정한다 — 좌상단 고정이면 크게 확대해 둔 핀이 되돌아갈 때 화면 반대편으로 물러나
    /// 사용자가 다시 찾아야 한다.
    /// </summary>
    [Fact]
    public void ResetToOriginal_KeepsTheCenterInPlace()
    {
        double left = 100, top = 50, baseW = 200, baseH = 100, scale = 3.0;
        double centerX = left + baseW * scale / 2;
        double centerY = top + baseH * scale / 2;

        var result = PinZoom.ResetToOriginal(At(scale, left, top, baseW, baseH), baseW, baseH);

        Assert.Equal(centerX, result.Left + result.Width / 2, 9);
        Assert.Equal(centerY, result.Top + result.Height / 2, 9);
    }

    /// <summary>이미 100%면 아무것도 움직이지 않는다.</summary>
    [Fact]
    public void ResetToOriginal_AlreadyOriginal_IsIdentity()
    {
        var result = PinZoom.ResetToOriginal(At(1.0, 100, 50, 200, 100), 200, 100);

        Assert.Equal(100, result.Left, 9);
        Assert.Equal(50, result.Top, 9);
    }

    // ---- 82단계 (사용자 신고: 휠마다 떨림) — 이상적 사각형과 적용 시점 반올림 ----

    /// <summary>
    /// N칸 확대한 뒤 N칸 축소하면 이상적 사각형이 제자리로 돌아온다 — 반올림하지 않은 값에서 계산하므로
    /// 부동소수 잡음 말고는 드리프트가 없고, 정수로 적용한 사각형은 비트 단위로 같다.
    /// </summary>
    [Fact]
    public void ZoomAtCursor_TenInThenTenOut_ReturnsTheIdealRectExactly()
    {
        var start = new PinZoomResult(1.0, 760, 366, 400, 300);
        double cursorX = 880, cursorY = 576;

        var ideal = start;
        for (int i = 0; i < 10; i++) { ideal = StepAtScreen(ideal, 120, cursorX, cursorY, 400, 300); }
        for (int i = 0; i < 10; i++) { ideal = StepAtScreen(ideal, -120, cursorX, cursorY, 400, 300); }

        Assert.Equal(start.Scale, ideal.Scale, 9);
        Assert.Equal(start.Left, ideal.Left, 9);
        Assert.Equal(start.Top, ideal.Top, 9);
        Assert.Equal(PinZoom.ToPhysicalRect(start), PinZoom.ToPhysicalRect(ideal));
    }

    /// <summary>칸마다 커서 아래 이미지 점(기준 좌표)이 그대로다 — 열 칸 연속이어도 이상적 사각형에서는 변하지 않는다.</summary>
    [Fact]
    public void ZoomAtCursor_TenNotchesAtAFixedScreenCursor_KeepsTheImagePointUnderTheCursor()
    {
        double baseW = 400, baseH = 300, cursorX = 880, cursorY = 576;
        var ideal = new PinZoomResult(1.0, 760, 366, baseW, baseH);
        double u = (cursorX - ideal.Left) / ideal.Scale;
        double v = (cursorY - ideal.Top) / ideal.Scale;

        for (int i = 0; i < 10; i++)
        {
            ideal = StepAtScreen(ideal, 120, cursorX, cursorY, baseW, baseH);

            Assert.Equal(u, (cursorX - ideal.Left) / ideal.Scale, 9);
            Assert.Equal(v, (cursorY - ideal.Top) / ideal.Scale, 9);
        }
    }

    /// <summary>
    /// 옛 결함의 특성화: 반올림된 창 위치에서 다음 칸을 계산하면(WPF가 WM_MOVE로 Left를 정수로 되쓴 경로) 10칸 만에
    /// 커서 아래 점이 1px 넘게 달아난다(실측 3.1px). 이상적 사각형을 이어 가고 적용할 때만 반올림하면 반 픽셀 안이다.
    /// </summary>
    [Fact]
    public void ToPhysicalRect_RoundingOnlyAtApply_DoesNotAccumulateButFeedbackDoes()
    {
        double baseW = 400, baseH = 300, cursorX = 880, cursorY = 576;
        var ideal = new PinZoomResult(1.0, 760, 366, baseW, baseH);
        var fedBack = ideal;

        for (int i = 0; i < 10; i++)
        {
            ideal = StepAtScreen(ideal, 120, cursorX, cursorY, baseW, baseH);
            fedBack = StepAtScreen(fedBack, 120, cursorX, cursorY, baseW, baseH);
            var rounded = PinZoom.ToPhysicalRect(fedBack);
            fedBack = fedBack with { Left = rounded.X, Top = rounded.Y };
        }
        double exactLeft = cursorX - (cursorX - 760) * ideal.Scale;

        Assert.InRange(PinZoom.ToPhysicalRect(ideal).X - exactLeft, -0.5, 0.5);
        Assert.True(Math.Abs(fedBack.Left - exactLeft) > 1,
            $"반올림 되먹임 경로는 오차가 쌓여야 한다(옛 결함의 특성화): {fedBack.Left - exactLeft:F2}px");
    }

    /// <summary>반 픽셀은 언제나 +∞ 쪽 — 음수 원점(왼쪽 모니터)에서도 같다.</summary>
    [Theory]
    [InlineData(100.5, 101)]
    [InlineData(101.5, 102)]
    [InlineData(100.49, 100)]
    [InlineData(-1800.5, -1800)]
    [InlineData(-1800.51, -1801)]
    [InlineData(0.5, 1)]
    [InlineData(-0.5, 0)]
    public void RoundEdge_HalfPixel_RoundsTowardPositiveInfinity(double value, int expected)
    {
        Assert.Equal(expected, PinZoom.RoundEdge(value));
    }

    /// <summary>
    /// 정수만큼 평행 이동한 값의 반올림은 반올림의 평행 이동이다 — 은행가 반올림(Math.Round)은 100.5→100, 101.5→102라
    /// 이 성질이 깨져 드래그로 옮긴 핀의 다음 칸이 1px 튄다.
    /// </summary>
    [Theory]
    [InlineData(100.5)]
    [InlineData(-1919.5)]
    [InlineData(37.25)]
    [InlineData(-0.75)]
    public void RoundEdge_IntegerTranslation_TranslatesTheRoundedValue(double value)
    {
        for (int k = -3; k <= 3; k++)
        {
            Assert.Equal(PinZoom.RoundEdge(value) + k, PinZoom.RoundEdge(value + k));
        }
    }

    /// <summary>크기는 배율만의 함수다 — 같은 배율이면 소수부 위치가 달라도 같은 크기로 적용된다.</summary>
    [Fact]
    public void ToPhysicalRect_SameScaleAtDifferentFractions_HasTheSameSize()
    {
        var a = PinZoom.ToPhysicalRect(new PinZoomResult(1.21, 10.49, 20.51, 484.0000001, 363));
        var b = PinZoom.ToPhysicalRect(new PinZoomResult(1.21, -1900.5, 7.5, 484.0000001, 363));

        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Height, b.Height);
        Assert.Equal(new PhysicalRect(10, 21, 484, 363), a);
    }

    [Fact]
    public void ToPhysicalRect_TinyIdeal_KeepsAtLeastOnePixel()
    {
        var rect = PinZoom.ToPhysicalRect(new PinZoomResult(PinZoom.MinScale, 5, 5, 0.8, 0.3));

        Assert.Equal(1, rect.Width);
        Assert.Equal(1, rect.Height);
    }

    [Fact]
    public void Resync_WindowUnchanged_KeepsTheIdealRect()
    {
        var ideal = new PinZoomResult(1.1, 747.6, 345.3, 440, 330);
        var applied = PinZoom.ToPhysicalRect(ideal);

        Assert.Equal(ideal, PinZoom.Resync(ideal, applied, applied, 400));
    }

    /// <summary>드래그 이동: 정수 이동량만큼 옮기고 반올림 전 소수부를 지킨다 — 실제 창 위치로 덮어쓰면 반 픽셀을 잃는다.</summary>
    [Fact]
    public void Resync_MovedSameSize_TranslatesKeepingTheFraction()
    {
        var ideal = new PinZoomResult(1.1, 747.6, 345.3, 440, 330);
        var applied = PinZoom.ToPhysicalRect(ideal);
        var moved = applied with { X = applied.X + 37, Y = applied.Y - 23 };

        var resynced = PinZoom.Resync(ideal, applied, moved, 400);

        Assert.Equal(784.6, resynced.Left, 9);
        Assert.Equal(322.3, resynced.Top, 9);
        Assert.Equal(1.1, resynced.Scale);
        Assert.Equal(moved, PinZoom.ToPhysicalRect(resynced));
    }

    /// <summary>
    /// 줌 밖에서 크기까지 바뀌었다(DPI가 다른 모니터로 옮길 때의 크기 조정 등): 실제 창에서 다시 잡는다 —
    /// 배율은 보이는 폭 / 기준 폭이라 다음 칸이 보이는 그대로에서 이어진다.
    /// </summary>
    [Fact]
    public void Resync_ResizedOutsideZoom_RederivesFromTheWindow()
    {
        var ideal = new PinZoomResult(1.0, 100, 100, 400, 300);
        var applied = new PhysicalRect(100, 100, 400, 300);
        var resized = new PhysicalRect(1900, 80, 600, 450);

        var resynced = PinZoom.Resync(ideal, applied, resized, 400);

        Assert.Equal(1.5, resynced.Scale, 9);
        Assert.Equal(resized, PinZoom.ToPhysicalRect(resynced));
    }

    // ---- 95단계 (최종 리뷰: 82단계 회귀) — 범위 밖 크기에서 다시 잡은 배율 ----

    /// <summary>
    /// 8배 핀을 150% 모니터로 옮기면 WPF가 창을 DPI 비율만큼 키워 보이는 폭이 기준의 12배가 된다. 보이는 사각형은 그대로
    /// 두되 배율은 범위로 클램프한다 — 옛 코드는 배율 12, 라벨 1200%였다.
    /// </summary>
    [Fact]
    public void Resync_ResizedBeyondMaxScale_ClampsTheScaleAndKeepsTheVisibleRect()
    {
        var ideal = new PinZoomResult(PinZoom.MaxScale, 100, 100, 3200, 2400);
        var applied = PinZoom.ToPhysicalRect(ideal);
        var resized = new PhysicalRect(1900, 80, 4800, 3600);

        var resynced = PinZoom.Resync(ideal, applied, resized, 400);

        Assert.Equal(PinZoom.MaxScale, resynced.Scale);
        Assert.Equal(resized, PinZoom.ToPhysicalRect(resynced));
    }

    /// <summary>최소 배율 핀을 DPI가 낮은 모니터로 옮긴 대칭 경우 — 보이는 폭은 기준의 0.0675배, 배율은 최소로 클램프한다.</summary>
    [Fact]
    public void Resync_ResizedBelowMinScale_ClampsTheScaleAndKeepsTheVisibleRect()
    {
        var ideal = new PinZoomResult(PinZoom.MinScale, 100, 100, 40, 30);
        var applied = PinZoom.ToPhysicalRect(ideal);
        var resized = new PhysicalRect(1900, 80, 27, 20);

        var resynced = PinZoom.Resync(ideal, applied, resized, 400);

        Assert.Equal(PinZoom.MinScale, resynced.Scale);
        Assert.Equal(resized, PinZoom.ToPhysicalRect(resynced));
    }

    /// <summary>
    /// 확대 칸은 절대 줄이지 않는다 — 보이는 크기(기준의 12배)가 최대 배율 밖이면 배율은 이미 한계라 창은 그대로다.
    /// 클램프한 배율(8, Resync의 결과)과 클램프하지 않은 배율(12) 둘 다 받친다. 옛 코드는 둘 다 핀을 1/3 줄였다.
    /// </summary>
    [Theory]
    [InlineData(8.0)]
    [InlineData(12.0)]
    public void ZoomAtCursor_VisibleSizeAboveMaxScale_ZoomInNeverShrinks(double scale)
    {
        var current = new PinZoomResult(scale, 1900, 80, 4800, 3600);

        var result = PinZoom.ZoomAtCursor(current, 120, 400, 300, cursorX: 2400, cursorY: 1800);

        Assert.Equal(current with { Scale = PinZoom.MaxScale }, result);
    }

    /// <summary>축소 칸은 절대 키우지 않는다 — 보이는 크기가 최소 배율 밖인 대칭 경우.</summary>
    [Theory]
    [InlineData(0.1)]
    [InlineData(0.0675)]
    public void ZoomAtCursor_VisibleSizeBelowMinScale_ZoomOutNeverGrows(double scale)
    {
        var current = new PinZoomResult(scale, 1900, 80, 27, 20);

        var result = PinZoom.ZoomAtCursor(current, -120, 400, 300, cursorX: 13, cursorY: 10);

        Assert.Equal(current with { Scale = PinZoom.MinScale }, result);
    }

    /// <summary>
    /// 범위 안쪽으로 가는 칸은 클램프한 배율에서 한 칸 걷는다(8 → 8/1.1). 커서 아래 점은 <b>보이는</b> 창 기준으로 고정한다 —
    /// 배율 비율(8/1.1 ÷ 8)로 원점을 옮기면 실제 크기 비율(÷ 12)과 달라 그림이 커서에서 달아난다.
    /// </summary>
    [Fact]
    public void ZoomAtCursor_VisibleSizeAboveMaxScale_ZoomOutStepsFromTheClampedScaleAroundTheCursor()
    {
        var current = new PinZoomResult(PinZoom.MaxScale, 1900, 80, 4800, 3600);
        double cursorX = 1200, cursorY = 900;

        var result = PinZoom.ZoomAtCursor(current, -120, 400, 300, cursorX, cursorY);

        Assert.Equal(PinZoom.MaxScale / PinZoom.StepFactor, result.Scale, 9);
        Assert.Equal(400 * result.Scale, result.Width, 9);
        Assert.Equal(300 * result.Scale, result.Height, 9);
        Assert.Equal(1900 + cursorX, result.Left + cursorX / current.Width * result.Width, 9);
        Assert.Equal(80 + cursorY, result.Top + cursorY / current.Height * result.Height, 9);
    }

    /// <summary>최소 배율 밖에서 확대하는 대칭 경우 — 0.1에서 한 칸(0.11), 보이는 창 기준으로 커서 아래 점 고정.</summary>
    [Fact]
    public void ZoomAtCursor_VisibleSizeBelowMinScale_ZoomInStepsFromTheClampedScaleAroundTheCursor()
    {
        var current = new PinZoomResult(PinZoom.MinScale, 1900, 80, 27, 20);
        double cursorX = 13, cursorY = 10;

        var result = PinZoom.ZoomAtCursor(current, 120, 400, 300, cursorX, cursorY);

        Assert.Equal(PinZoom.MinScale * PinZoom.StepFactor, result.Scale, 9);
        Assert.Equal(1900 + cursorX, result.Left + cursorX / current.Width * result.Width, 9);
        Assert.Equal(80 + cursorY, result.Top + cursorY / current.Height * result.Height, 9);
    }

    /// <summary>원래 크기 복귀는 <b>보이는</b> 사각형의 중심을 고정한다 — 클램프한 배율(8)로 잰 폭이 아니라 실제 폭(12배)에서.</summary>
    [Fact]
    public void ResetToOriginal_VisibleSizeAboveMaxScale_KeepsTheVisibleCenter()
    {
        var current = new PinZoomResult(PinZoom.MaxScale, 1900, 80, 4800, 3600);

        var result = PinZoom.ResetToOriginal(current, 400, 300);

        Assert.Equal(1900 + 2400, result.Left + result.Width / 2, 9);
        Assert.Equal(80 + 1800, result.Top + result.Height / 2, 9);
        Assert.Equal(400, result.Width);
    }

    /// <summary>크기가 배율과 맞는(기준 × 배율) 이상적 사각형 — 줌만 거친 보통 상태.</summary>
    private static PinZoomResult At(double scale, double left, double top, double baseW, double baseH) =>
        new(scale, left, top, baseW * scale, baseH * scale);

    /// <summary>화면 좌표 커서로 한 칸 — PinZoomController와 같은 호출 모양 (커서는 이상적 좌상단 기준으로 넘긴다).</summary>
    private static PinZoomResult StepAtScreen(
        PinZoomResult ideal, int delta, double cursorX, double cursorY, double baseW, double baseH) =>
        PinZoom.ZoomAtCursor(
            ideal, delta, baseW, baseH, cursorX - ideal.Left, cursorY - ideal.Top);
}
