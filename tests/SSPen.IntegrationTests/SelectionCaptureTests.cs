using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SSPen.Annotation;
using SSPen.Capture;
using SSPen.Interop;
using Xunit;

namespace SSPen.IntegrationTests;

/// <summary>
/// 캡처 결과물에 선택 장식이 들어가지 않는지 실기 픽셀 검증 (SEL-17, SEL-AC-15).
///
/// as-seen 인텐트: 잉크는 **찍히고** 장식은 **안 찍힌다**. 서피스 창 자체를 숨기면 잉크까지
/// 사라지므로 장식 레이어만 숨기는 것이 계약이다 — 그래서 양방향으로 어서트한다.
/// </summary>
public class SelectionCaptureTests
{
    /// <summary>장식 강조색 #FF00ADEF. 잉크와 확실히 구분되도록 잉크는 순수 빨강을 쓴다.</summary>
    private static readonly Color DecorationColor = Color.FromRgb(0x00, 0xAD, 0xEF);
    private static readonly Color InkColor = Color.FromRgb(0xFF, 0x00, 0x00);

    private sealed record Rig(
        ContentSurfaceWindow Surface,
        AnnotationDocument Document,
        SelectionModel Selection,
        PhysicalRect Bounds);

    private static Rig CreateRig()
    {
        // 주 모니터를 쓴다: 캡처 좌표계가 가장 단순하고 음수 원점 변수를 배제한다.
        var monitor = MonitorTopology.Enumerate().First(m => m.IsPrimary);
        var state = new AppState { ActiveTool = ToolKind.Select };
        var document = new AnnotationDocument(monitor.DeviceName);
        var selection = new SelectionModel();
        selection.AttachTo(document);
        var ledger = new UndoLedger(
            e => document.Elements.Contains(e) ? document : null, selection);
        var surface = new ContentSurfaceWindow(
            monitor, state, document, ledger,
            new FadingInkController(new FadeSchedulerCore()),
            selection,
            e => document.Elements.Contains(e) ? document : null,
            (deltas, _) => ledger.RecordTransform(deltas),
            () => 0);
        return new Rig(surface, document, selection, monitor.Bounds);
    }

    /// <summary>굵고 긴 수평 획: 캡처 영역 안에서 확실히 픽셀을 남긴다.</summary>
    private static StrokeElement NewThickStroke() =>
        new([new Point(400, 400), new Point(700, 400)], InkColor, 24, isHighlighter: false);

    /// <summary>
    /// 조건이 참이 될 때까지 캡처를 재시도한다 (최대 <paramref name="timeoutMs"/>).
    ///
    /// 고정 <c>Thread.Sleep</c>은 동기화가 아니라 경합이다: DWM 합성이 늦어지는 순간
    /// (다른 창의 애니메이션, GPU 부하, 전원 관리 전환) 픽셀 단언이 간헐적으로 깨진다.
    /// 조건이 충족되는 즉시 진행하므로 정상 경로는 오히려 빨라지고, 느린 경로만 기다린다.
    /// </summary>
    private static BitmapSource CaptureUntil(
        PhysicalRect region, Func<BitmapSource, bool> condition, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        BitmapSource shot;
        do
        {
            StaRunner.PumpMessages();
            shot = CaptureService.CaptureRegion(region);
            if (condition(shot))
            {
                return shot;
            }
            Thread.Sleep(50);
        }
        while (DateTime.UtcNow < deadline);
        return shot; // 마지막 시도본을 돌려 호출부가 의미 있는 메시지로 단언하게 한다.
    }

    /// <summary>영역 안에 지정 색과 충분히 가까운 픽셀이 있는가.</summary>
    private static bool ContainsColor(BitmapSource source, Color target, int tolerance)
    {
        int width = source.PixelWidth;
        int height = source.PixelHeight;
        int stride = width * 4;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);

        for (int i = 0; i < pixels.Length; i += 4)
        {
            int b = pixels[i];
            int g = pixels[i + 1];
            int r = pixels[i + 2];
            if (Math.Abs(r - target.R) <= tolerance
                && Math.Abs(g - target.G) <= tolerance
                && Math.Abs(b - target.B) <= tolerance)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>선택 상태로 캡처했을 때 잉크는 있고 장식은 없다 — 한 번의 캡처로 양쪽을 동시에 본다.</summary>
    [Fact]
    public void Capture_WithActiveSelection_HasInkButNoDecorationPixels() => StaRunner.Run(() =>
    {
        var rig = CreateRig();
        try
        {
            rig.Surface.Show();
            StaRunner.PumpMessages();

            var element = NewThickStroke();
            rig.Document.Add(element);
            rig.Selection.Set([element]);
            StaRunner.PumpMessages();

            // 장식이 실제로 그려있는지 먼저 확인 — 아니면 이 테스트는 아무것도 증명하지 못한다.
            var region = new PhysicalRect(rig.Bounds.X + 350, rig.Bounds.Y + 330, 420, 160);
            var withDecorations = CaptureUntil(
                region, shot => ContainsColor(shot, DecorationColor, tolerance: 40));
            Assert.True(
                ContainsColor(withDecorations, DecorationColor, tolerance: 40),
                "사전 조건 실패: 장식이 애초에 화면에 없으면 '장식 없음' 검증이 무의미하다.");

            // SEL-17 경로: 장식만 숨기고 캡처.
            rig.Surface.SetDecorationsVisible(false);
            StaRunner.PumpMessages();

            // 장식이 사라진 프레임이 합성될 때까지 기다린다 (고정 슬립이 아니라 조건 충족).
            var captured = CaptureUntil(
                region, shot => !ContainsColor(shot, DecorationColor, tolerance: 40));

            Assert.True(
                ContainsColor(captured, InkColor, tolerance: 60),
                "as-seen 인텐트: 잉크는 캡처에 남아야 한다.");
            Assert.False(
                ContainsColor(captured, DecorationColor, tolerance: 40),
                "SEL-AC-15: 선택 장식은 캡처 결과물에 들어가면 안 된다.");
        }
        finally
        {
            rig.Surface.Close();
        }
    });

    /// <summary>R12: 캡처 후 장식이 복원되고 선택은 그대로다 (QA-6 체감의 기계 절반).</summary>
    [Fact]
    public void Capture_AfterSession_DecorationsRestoredAndSelectionKept() => StaRunner.Run(() =>
    {
        var rig = CreateRig();
        try
        {
            rig.Surface.Show();
            StaRunner.PumpMessages();

            var element = NewThickStroke();
            rig.Document.Add(element);
            rig.Selection.Set([element]);
            StaRunner.PumpMessages();

            rig.Surface.SetDecorationsVisible(false);
            StaRunner.PumpMessages();
            rig.Surface.SetDecorationsVisible(true);
            StaRunner.PumpMessages();

            Assert.True(rig.Selection.Contains(element), "캡처 왕복이 선택을 해제하면 안 된다.");

            var region = new PhysicalRect(rig.Bounds.X + 350, rig.Bounds.Y + 330, 420, 160);
            var restored = CaptureUntil(
                region, shot => ContainsColor(shot, DecorationColor, tolerance: 40));
            Assert.True(
                ContainsColor(restored, DecorationColor, tolerance: 40),
                "캡처 종료 후 장식이 복원되어야 한다 (R12).");
        }
        finally
        {
            rig.Surface.Close();
        }
    });
}
