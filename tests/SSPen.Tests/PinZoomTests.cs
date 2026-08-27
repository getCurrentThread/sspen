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

        var result = PinZoom.ZoomAtCursor(scale, 120, left, top, baseW, baseH, cursorX, cursorY);

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

        var result = PinZoom.ZoomAtCursor(scale, -120, left, top, baseW, baseH, cursorX, cursorY);

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
        var result = PinZoom.ZoomAtCursor(1.0, 120, 200, 150, 400, 300, cursorX: 0, cursorY: 0);

        Assert.Equal(200, result.Left, 9);
        Assert.Equal(150, result.Top, 9);
    }

    [Fact]
    public void ZoomAtCursor_AtMaxScale_DoesNotMoveWindow()
    {
        // 배율이 클램프에 걸려 변하지 않으면 창도 그대로여야 한다 — 그렇지 않으면
        // 최대 배율에서 휠을 굴릴 때마다 창이 스르륵 밀려난다.
        var result = PinZoom.ZoomAtCursor(
            PinZoom.MaxScale, 120, 10, 20, 400, 300, cursorX: 300, cursorY: 200);

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

        var result = PinZoom.ZoomAtCursor(1.0, 120, left, top, baseW, baseH, cursorX, cursorY);

        double t = cursorX / baseW;
        double u = cursorY / baseH;

        Assert.Equal(left + cursorX, result.Left + t * result.Width, 9);
        Assert.Equal(top + cursorY, result.Top + u * result.Height, 9);
        Assert.True(result.Left < 0, "음수 원점이 유지되어야 한다");
    }

    [Fact]
    public void ZoomAtCursor_ScaleUnchanged_WidthMatchesBaseTimesScale()
    {
        var result = PinZoom.ZoomAtCursor(1.5, 120, 0, 0, 200, 100, 50, 25);

        Assert.Equal(200 * result.Scale, result.Width, 9);
        Assert.Equal(100 * result.Scale, result.Height, 9);
    }
}
