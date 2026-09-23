using System.Windows.Media;
using System.Windows.Media.Imaging;
using SSPen.Capture;
using SSPen.Interop;

namespace SSPen.IntegrationTests;

/// <summary>
/// 통합 스위트의 픽셀 캡처·판정 공용 도구 (61단계, A8-7). <c>SelectionCaptureTests</c>가 1.3.2에서 고정 대기를
/// 조건 대기로 고치며 만든 것을 승격했다 — BitBlt 픽셀 단언을 하는 모든 클래스가 같은 대기 규칙을 쓰게 하려는 것이다.
/// </summary>
internal static class PixelProbe
{
    /// <summary>
    /// 조건이 참이 될 때까지 캡처를 재시도한다 (최대 <paramref name="timeoutMs"/>).
    ///
    /// 고정 <c>Thread.Sleep</c>은 동기화가 아니라 경합이다: DWM 합성이 늦어지는 순간
    /// (다른 창의 애니메이션, GPU 부하, 전원 관리 전환) 픽셀 단언이 간헐적으로 깨진다.
    /// 조건이 충족되는 즉시 진행하므로 정상 경로는 오히려 빨라지고, 느린 경로만 기다린다.
    /// BitBlt가 막힌 세션에서는 <c>null</c>을 돌려준다 — 호출부가 대체 경로로 간다.
    /// </summary>
    internal static BitmapSource? CaptureUntil(
        PhysicalRect region, Func<BitmapSource, bool> condition, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        BitmapSource? shot = null;
        do
        {
            StaRunner.PumpMessages();
            try
            {
                shot = CaptureService.CaptureRegion(region);
                if (condition(shot))
                {
                    return shot;
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("BitBlt"))
            {
                return null;
            }
            Thread.Sleep(50);
        }
        while (DateTime.UtcNow < deadline);
        return shot; // 마지막 시도본을 돌려 호출부가 의미 있는 메시지로 단언하게 한다.
    }

    /// <summary>
    /// 영역 안에서 지정 색에 가까운 픽셀 <b>개수</b>.
    ///
    /// 왜 개수인가: 서피스는 투명이라 캡처에는 <b>사용자 화면이 그대로 함께 찍힌다</b>. 그래서
    /// "이 색 픽셀이 하나라도 있는가"는 장식이 아니라 배경에 대한 질문이 되기 쉽다 —
    /// 실제로 강조색이 대비 기준을 맞추려 #00ADEF(밝은 하늘색)에서 #0071A8(짙은 파랑)로 바뀌자,
    /// 흔한 파랑 UI와 겹쳐 <b>장식을 숨긴 뒤에도</b> 같은 색 픽셀이 남아 스위트가 죽었다.
    /// 배경은 장식을 켜든 끄든 같은 만큼 기여하므로, 같은 영역의 개수 차이는 장식만의 몫이다.
    /// </summary>
    internal static int CountColor(BitmapSource source, Color target, int tolerance)
    {
        int width = source.PixelWidth;
        int height = source.PixelHeight;
        int stride = width * 4;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);

        int count = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int b = pixels[i];
            int g = pixels[i + 1];
            int r = pixels[i + 2];
            if (Math.Abs(r - target.R) <= tolerance
                && Math.Abs(g - target.G) <= tolerance
                && Math.Abs(b - target.B) <= tolerance)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>영역 안에 지정 색과 충분히 가까운 픽셀이 있는가 — <see cref="CountColor"/>에 위임한다(같은 루프를 두 벌 두지 않는다).</summary>
    internal static bool ContainsColor(BitmapSource source, Color target, int tolerance) =>
        CountColor(source, target, tolerance) > 0;
}
