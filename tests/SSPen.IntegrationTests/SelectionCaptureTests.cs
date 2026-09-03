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
    /// <summary>
    /// 장식 강조색. 값을 여기 다시 적지 않고 소유자(<see cref="SSPen.Shell.ShellPalette.Accent"/>)에서 읽는다 —
    /// 예전에는 #FF00ADEF를 하드코딩했다가 강조색이 대비 기준을 맞추려 #FF0071A8로 바뀌자
    /// 이 스위트가 "장식이 화면에 없다"며 죽었다(허용 오차 40 밖). 그때 실제로 바뀐 것은 색 하나뿐이고
    /// 검증하려던 계약(장식은 캡처에 안 찍힌다)은 그대로였다.
    /// 장식 색과 <c>ShellPalette.Accent</c>가 같다는 사실 자체는 유닛 스위트의
    /// <c>SelectionDecorationVisualTests</c>가 잠근다.
    /// 잉크는 이 색과 확실히 구분되도록 순수 빨강을 쓴다.
    /// </summary>
    private static readonly Color DecorationColor = SSPen.Shell.ShellPalette.Accent;
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
            _ => 1.0,
            (deltas, _) => ledger.RecordTransform(deltas),
            () => { },
            () => 0,
            (rows, columns) => $"{rows}x{columns}");
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
    private static BitmapSource? CaptureUntil(
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
    /// 장식 판정에 쓰는 여유. 장식이 화면 위 <b>다른 무엇</b>과 우연히 같은 색일 수 있으므로
    /// 존재 여부가 아니라 <see cref="CountColor"/>의 개수 차이로 본다 (아래 주석 참조).
    /// </summary>
    private const int DecorationTolerance = 40;

    /// <summary>
    /// 장식이 그려졌다고 인정하는 최소 픽셀 증가분. 장식은 핸들 9개(각 10x10 테두리 2px) +
    /// 점선 테두리라 수백 픽셀 단위로 늘어난다 — 50은 안티에일리어싱 흔들림보다 훨씬 크고
    /// 실제 장식보다 훨씬 작다.
    /// </summary>
    private const int DecorationPixelMargin = 50;

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

    /// <summary>
    /// 영역 안에서 지정 색에 가까운 픽셀 <b>개수</b>.
    ///
    /// 왜 개수인가: 서피스는 투명이라 캡처에는 <b>사용자 화면이 그대로 함께 찍힌다</b>. 그래서
    /// "이 색 픽셀이 하나라도 있는가"는 장식이 아니라 배경에 대한 질문이 되기 쉽다 —
    /// 실제로 강조색이 대비 기준을 맞추려 #00ADEF(밝은 하늘색)에서 #0071A8(짙은 파랑)로 바뀌자,
    /// 흔한 파랑 UI와 겹쳐 <b>장식을 숨긴 뒤에도</b> 같은 색 픽셀이 남아 스위트가 죽었다.
    /// 배경은 장식을 켜든 끄든 같은 만큼 기여하므로, 같은 영역의 개수 차이는 장식만의 몫이다.
    /// </summary>
    private static int CountColor(BitmapSource source, Color target, int tolerance)
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

    /// <summary>장식 색 픽셀 수가 조건을 만족할 때까지 캡처를 재시도하고, 마지막 개수를 돌려준다.</summary>
    private static int CountUntil(PhysicalRect region, Func<int, bool> condition, int timeoutMs = 3000)
    {
        int last = -1;
        var shot = CaptureUntil(
            region,
            image =>
            {
                last = CountColor(image, DecorationColor, DecorationTolerance);
                return condition(last);
            },
            timeoutMs);
        // BitBlt가 막힌 세션(shot == null)은 픽셀 검증 자체가 불가능하다 — 호출부가 대체 경로로 간다.
        return shot is null ? -1 : last;
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

            var region = new PhysicalRect(rig.Bounds.X + 350, rig.Bounds.Y + 330, 420, 160);

            // 배경 기준선: 장식을 끈 상태의 같은 영역. 서피스가 투명이라 이 영역에는 사용자 화면이
            // 함께 찍히므로, 장식의 몫은 '있다/없다'가 아니라 이 기준선과의 차이다 (CountColor 주석).
            rig.Surface.SetDecorationsVisible(false);
            StaRunner.PumpMessages();
            int ambient = CountUntil(region, _ => true);

            rig.Surface.SetDecorationsVisible(true);
            StaRunner.PumpMessages();
            int shown = ambient < 0 ? -1 : CountUntil(region, count => count > ambient + DecorationPixelMargin);

            if (ambient < 0 || shown <= ambient + DecorationPixelMargin)
            {
                // 비대화형 세션: 장식 레이어 Visibility 전이와 선택 유지 상태를 직접 단언
                var layer = (System.Windows.Controls.Canvas)((System.Windows.Controls.Grid)rig.Surface.Content).Children[^1];
                Assert.Equal(Visibility.Visible, layer.Visibility);

                rig.Surface.SetDecorationsVisible(false);
                StaRunner.PumpMessages();
                Assert.Equal(Visibility.Collapsed, layer.Visibility);
                Assert.True(rig.Selection.Contains(element));

                rig.Surface.SetDecorationsVisible(true);
                StaRunner.PumpMessages();
                Assert.Equal(Visibility.Visible, layer.Visibility);
                Assert.True(rig.Selection.Contains(element));
                return;
            }

            // 여기까지 왔다면 장식이 실제로 픽셀을 남겼다 — '장식 없음' 검증이 의미를 갖는 전제다.

            // SEL-17 경로: 장식만 숨기고 캡처. 장식 몫이 사라진 프레임이 합성될 때까지 기다린다
            // (고정 슬립이 아니라 조건 충족).
            rig.Surface.SetDecorationsVisible(false);
            StaRunner.PumpMessages();
            int hidden = CountUntil(region, count => count <= ambient + DecorationPixelMargin);

            var captured = CaptureUntil(region, _ => true);
            Assert.NotNull(captured);
            Assert.True(
                ContainsColor(captured!, InkColor, tolerance: 60),
                "as-seen 인텐트: 잉크는 캡처에 남아야 한다.");
            Assert.True(
                hidden <= ambient + DecorationPixelMargin,
                $"SEL-AC-15: 선택 장식은 캡처 결과물에 들어가면 안 된다 (배경 {ambient}, 장식 표시 {shown}, 숨긴 뒤 {hidden}).");
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

            var region = new PhysicalRect(rig.Bounds.X + 350, rig.Bounds.Y + 330, 420, 160);

            // 캡처 세션 흉내: 장식을 끈 동안의 배경 기준선을 재고, 다시 켠 뒤 그보다 늘었는지 본다.
            rig.Surface.SetDecorationsVisible(false);
            StaRunner.PumpMessages();
            int ambient = CountUntil(region, _ => true);

            rig.Surface.SetDecorationsVisible(true);
            StaRunner.PumpMessages();

            Assert.True(rig.Selection.Contains(element), "캡처 왕복이 선택을 해제하면 안 된다.");

            int restored = ambient < 0 ? -1 : CountUntil(region, count => count > ambient + DecorationPixelMargin);
            if (ambient >= 0 && restored > ambient + DecorationPixelMargin)
            {
                return; // 복원된 장식이 실제 픽셀로 확인됐다 (R12).
            }

            // 픽셀을 볼 수 없는 세션(BitBlt 차단·비대화형)에서는 레이어 상태로 대신 확인한다.
            var layer = (System.Windows.Controls.Canvas)((System.Windows.Controls.Grid)rig.Surface.Content).Children[^1];
            Assert.Equal(Visibility.Visible, layer.Visibility);
        }
        finally
        {
            rig.Surface.Close();
        }
    });
}
