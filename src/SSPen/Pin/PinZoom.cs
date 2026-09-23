using SSPen.Interop;

namespace SSPen.Pin;

/// <summary>
/// 확대/축소 후 창 배치 결과 — 반올림하지 않은 <b>이상적</b> 사각형. 단위는 호출자가 정하며 <see cref="PinZoomController"/>는
/// 물리 픽셀을 준다 (82단계). 정수 사각형은 적용할 때만 <see cref="PinZoom.ToPhysicalRect"/>로 만든다.
/// 보통은 크기 = 기준 × 배율이지만, <see cref="PinZoom.Resync"/>가 범위 밖 크기에서 다시 잡으면 배율만 범위 안이고 크기는
/// 보이는 그대로다 (95단계).
/// </summary>
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
///
/// 82단계(사용자 신고: 휠마다 떨림): 계산은 물리 픽셀의 이상적 double 사각형 위에서 하고, 반올림은 창에 적용할 때
/// 한 번만 한다(<see cref="ToPhysicalRect"/>). 반올림된 창 위치에서 다음 칸을 계산하면 오차가 칸마다 쌓인다 —
/// 옛 PinWindow는 WPF가 WM_MOVE로 되쓴 정수 Left/Top에서 다음 칸을 계산해 10칸 만에 커서 아래 점이 3px 달아났다(실측).
/// 이 클래스에는 DPI 항이 없다 — 핀은 물리 픽셀 캡처이고, DPI 환산이 끼면 100%가 아닌 배율에서 첫 칸이 배율만큼 튄다.
/// </summary>
public static class PinZoom
{
    public const double MinScale = 0.1;
    public const double MaxScale = 8.0;

    /// <summary>배율 1.0의 최소 변 길이 (물리 px) — 너무 작은 캡처도 잡고 굴릴 수 있게.</summary>
    public const int MinBaseExtent = 8;

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
    /// 중심은 현재 사각형의 <b>실제</b> 크기에서 잰다 — <see cref="Resync"/>가 범위 밖 크기에서 배율만 클램프한 사각형
    /// (배율 8, 폭 12배)을 배율로 재면 중심이 어긋난다 (95단계).
    /// </summary>
    public static PinZoomResult ResetToOriginal(PinZoomResult current, double baseWidth, double baseHeight) => new(
        1.0,
        current.Left + (current.Width - baseWidth) / 2.0,
        current.Top + (current.Height - baseHeight) / 2.0,
        baseWidth,
        baseHeight);

    /// <summary>
    /// 커서를 고정점으로 삼아 확대/축소한 창 사각형을 계산한다.
    ///
    /// 95단계(최종 리뷰): 현재 사각형의 크기는 기준 × 배율과 다를 수 있다 — 8배 핀을 150% 모니터로 옮기면 보이는 폭이
    /// 기준의 12배가 되고, <see cref="Resync"/>는 그 사각형을 지키면서 배율만 범위로 클램프한다. 그래서
    /// <list type="bullet">
    ///   <item>한 칸은 <b>클램프한</b> 배율에서 걷는다(범위 밖 배율 12에서 걸으면 13.2 → 8로 잘려 확대 칸이 핀을 1/3 줄였다).</item>
    ///   <item>확대 칸은 절대 줄이지 않고 축소 칸은 절대 키우지 않는다 — 새 폭이 보이는 폭의 반대쪽이면(이미 한계) 창은 그대로다.
    ///     보이는 배율은 폭으로 정의한다(<see cref="Resync"/>와 같은 정의).</item>
    ///   <item>커서 고정의 비율은 배율 비가 아니라 <b>보이는 크기</b>에 대한 새 크기의 비다 — 둘이 다르면 그림이 커서에서 달아난다.
    ///     크기가 기준 × 배율인 보통 상태에서는 배율 비와 같다.</item>
    /// </list>
    /// </summary>
    /// <param name="current">현재 (이상적) 창 사각형과 배율.</param>
    /// <param name="wheelDelta">휠 델타 (양수=확대).</param>
    /// <param name="baseWidth">배율 1.0일 때 폭.</param>
    /// <param name="baseHeight">배율 1.0일 때 높이.</param>
    /// <param name="cursorX">커서의 <b>창 내부</b> X — (이상적) 좌상단 기준. 반올림된 실제 창 기준이 아니다.</param>
    /// <param name="cursorY">커서의 <b>창 내부</b> Y — (이상적) 좌상단 기준.</param>
    public static PinZoomResult ZoomAtCursor(
        PinZoomResult current,
        int wheelDelta,
        double baseWidth,
        double baseHeight,
        double cursorX,
        double cursorY)
    {
        double from = Math.Clamp(current.Scale, MinScale, MaxScale);
        double newScale = NextScale(from, wheelDelta);
        double newWidth = baseWidth * newScale;
        double newHeight = baseHeight * newScale;

        // 크기가 없는 사각형에서는 비율이 없다 — 0으로 나누지 않고 새 크기만 준다.
        if (current.Width <= 0 || current.Height <= 0)
        {
            return new PinZoomResult(newScale, current.Left, current.Top, newWidth, newHeight);
        }

        // 한계에 걸린 칸: 클램프한 목표가 보이는 크기의 반대쪽이면 창은 그대로다(배율만 범위 안으로).
        bool wrongWay = wheelDelta > 0 ? newWidth < current.Width : newWidth > current.Width;
        if (wrongWay)
        {
            return current with { Scale = from };
        }

        double ratioX = newWidth / current.Width;
        double ratioY = newHeight / current.Height;
        // 커서의 화면 좌표는 left + cursorX. 새 원점은 그 지점에서 축척된 오프셋을 뺀 값이다.
        // 클램프에 걸려 배율이 그대로면(비율 1) 창도 그대로다 — 최대 배율에서 굴려도 창이 밀려나지 않는다.
        double newLeft = current.Left + cursorX - cursorX * ratioX;
        double newTop = current.Top + cursorY - cursorY * ratioY;
        return new PinZoomResult(newScale, newLeft, newTop, newWidth, newHeight);
    }

    /// <summary>
    /// 이상적 사각형 → 창에 적용할 정수 사각형 (82단계). 위치는 모서리 반올림, 크기는 배율만의 함수라
    /// 같은 배율이면 어디에 있든 같은 크기가 나온다(원래 크기 복귀·N칸 왕복이 정확히 제 크기로 돌아온다). 최소 1px.
    /// 입력(이상적 값)은 건드리지 않으므로 반올림 오차가 다음 칸으로 넘어가지 않는다.
    /// </summary>
    public static PhysicalRect ToPhysicalRect(PinZoomResult ideal) => new(
        RoundEdge(ideal.Left),
        RoundEdge(ideal.Top),
        Math.Max(1, RoundEdge(ideal.Width)),
        Math.Max(1, RoundEdge(ideal.Height)));

    /// <summary>
    /// 반 픽셀은 언제나 +∞ 쪽으로 올린다 (<c>floor(v + 0.5)</c>). 기본 <c>Math.Round</c>(은행가 반올림)는
    /// 100.5→100, 101.5→102처럼 짝수 쪽으로 가서 정수만큼 평행 이동한 사각형의 반올림이 같은 만큼 옮겨지지 않는다 —
    /// 드래그로 옮긴 핀의 다음 칸이 1px 튄다. 음수 원점(왼쪽 모니터)에서도 같은 규칙이다.
    /// </summary>
    public static int RoundEdge(double value) => (int)Math.Floor(value + 0.5);

    /// <summary>
    /// 줌 밖에서 창이 바뀌었을 때 이상적 사각형을 실제 창에 다시 맞춘다 (82단계) — 드래그 이동(<c>DragMove</c>)이나
    /// 다른 DPI 모니터로 옮길 때 OS·WPF가 하는 크기 조정이다. 마지막으로 적용한 사각형과 같으면 그대로 둔다.
    /// 크기가 같으면 정수 이동량만큼 평행 이동해 반올림 전 소수부를 지키고, 크기까지 바뀌었으면 실제 창에서 다시 잡는다
    /// (배율 = 실제 폭 / 기준 폭 — 보이는 그대로에서 다음 칸이 이어지게).
    /// 그 배율은 [<see cref="MinScale"/>, <see cref="MaxScale"/>]로 클램프하고 사각형은 보이는 그대로 둔다 (95단계) — 8배 핀을
    /// 150% 모니터로 옮기면 보이는 폭이 기준의 12배라 라벨이 1200%를 보였다. 범위 밖 크기에서의 칸은 <see cref="ZoomAtCursor"/>가 다룬다.
    /// </summary>
    public static PinZoomResult Resync(PinZoomResult ideal, PhysicalRect lastApplied, PhysicalRect actual, double baseWidth)
    {
        if (actual == lastApplied)
        {
            return ideal;
        }
        if (actual.Width == lastApplied.Width && actual.Height == lastApplied.Height)
        {
            return ideal with
            {
                Left = ideal.Left + (actual.X - lastApplied.X),
                Top = ideal.Top + (actual.Y - lastApplied.Y),
            };
        }
        double visible = Math.Clamp(actual.Width / baseWidth, MinScale, MaxScale);
        return new PinZoomResult(visible, actual.X, actual.Y, actual.Width, actual.Height);
    }
}
