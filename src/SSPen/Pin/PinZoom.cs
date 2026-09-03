namespace SSPen.Pin;

/// <summary>확대/축소 후 창 배치 결과 (논리 단위).</summary>
public readonly record struct PinZoomResult(double Scale, double Left, double Top, double Width, double Height);

/// <summary>
/// 핀 창 휠 확대/축소 계산 (순수 로직).
///
/// 커서 고정 확대(사용자 요청 15차): 크기가 변해도 <b>커서 아래에 있던 그림 지점이 그대로 커서 아래</b>에
/// 남아야 한다. 좌상단 고정으로 스케일하면 축소할 때 그림이 커서에서 달아나 계속 마우스를 옮겨야 했다.
///
/// 원리: 커서의 창 내부 상대 위치 <c>t = (cursor - origin) / size</c> 는 스케일과 무관한 불변량이다.
/// 새 크기에서도 같은 <c>t</c>가 커서를 가리키도록 원점을 <c>origin' = cursor - t * newSize</c> 로 옮긴다.
/// 이는 <c>origin' = cursor - (cursor - origin) * r</c> (<c>r</c> = 크기 비율)과 같다.
/// </summary>
public static class PinZoom
{
    public const double MinScale = 0.1;
    public const double MaxScale = 8.0;

    /// <summary>휠 한 칸당 배율. 양수 델타=확대, 음수=축소이며 왕복하면 원래 배율로 돌아온다.</summary>
    public const double StepFactor = 1.1;

    /// <summary>휠 델타로 다음 배율을 구한다 (범위 클램프 포함).</summary>
    public static double NextScale(double current, int wheelDelta)
    {
        double factor = wheelDelta > 0 ? StepFactor : 1.0 / StepFactor;
        return Math.Clamp(current * factor, MinScale, MaxScale);
    }

    /// <summary>
    /// 원래 크기(100%)로 되돌린 창 사각형. <b>중심을 고정</b>한다 — 좌상단을 고정하면 크게 확대해 둔 핀이
    /// 되돌아갈 때 화면 반대편으로 훌쩍 물러나 사용자가 다시 찾아야 한다.
    /// </summary>
    public static PinZoomResult ResetToOriginal(
        double currentScale, double left, double top, double baseWidth, double baseHeight)
    {
        double currentWidth = baseWidth * currentScale;
        double currentHeight = baseHeight * currentScale;
        return new PinZoomResult(
            1.0,
            left + (currentWidth - baseWidth) / 2.0,
            top + (currentHeight - baseHeight) / 2.0,
            baseWidth,
            baseHeight);
    }

    /// <summary>
    /// 커서를 고정점으로 삼아 확대/축소한 창 사각형을 계산한다.
    /// </summary>
    /// <param name="currentScale">현재 배율.</param>
    /// <param name="wheelDelta">휠 델타 (양수=확대).</param>
    /// <param name="left">현재 창 좌측 (논리 좌표).</param>
    /// <param name="top">현재 창 상단 (논리 좌표).</param>
    /// <param name="baseWidth">배율 1.0일 때 폭.</param>
    /// <param name="baseHeight">배율 1.0일 때 높이.</param>
    /// <param name="cursorX">커서의 <b>창 내부</b> X (좌상단 기준).</param>
    /// <param name="cursorY">커서의 <b>창 내부</b> Y (좌상단 기준).</param>
    public static PinZoomResult ZoomAtCursor(
        double currentScale,
        int wheelDelta,
        double left,
        double top,
        double baseWidth,
        double baseHeight,
        double cursorX,
        double cursorY)
    {
        double newScale = NextScale(currentScale, wheelDelta);
        double newWidth = baseWidth * newScale;
        double newHeight = baseHeight * newScale;

        // 클램프에 걸려 배율이 그대로면 창도 그대로다 — 0으로 나누는 경로도 함께 막힌다.
        if (currentScale <= 0)
        {
            return new PinZoomResult(newScale, left, top, newWidth, newHeight);
        }

        double ratio = newScale / currentScale;
        // 커서의 화면 좌표는 left + cursorX. 새 원점은 그 지점에서 축척된 오프셋을 뺀 값이다.
        double newLeft = left + cursorX - cursorX * ratio;
        double newTop = top + cursorY - cursorY * ratio;
        return new PinZoomResult(newScale, newLeft, newTop, newWidth, newHeight);
    }
}
