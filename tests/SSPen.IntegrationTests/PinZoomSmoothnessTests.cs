using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SSPen.Capture;
using SSPen.Interop;
using SSPen.Pin;
using Xunit;
using Xunit.Abstractions;

namespace SSPen.IntegrationTests;

/// <summary>
/// 핀 휠 확대/축소가 떨리지 않는다는 실측 증인 (82단계, 사용자 신고 2026-09-23: "휠을 굴릴 때마다 덜덜덜 떨린다").
///
/// 왜 통합인가: 떨림의 원인은 계산이 아니라 <b>창에 적용하는 방식</b>이다. 옛 코드는 Left·Top·Width·Height를 따로
/// 대입해 WPF가 칸마다 <c>SetWindowPos</c>를 네 번 불렀고, 레이어드(<c>AllowsTransparency</c>) 창에는 "이동만 된" "크기만
/// 바뀐" 중간 사각형이 그대로 화면에 나갔다. 또 WPF는 WM_MOVE를 받으면 Left/Top을 <b>반올림된 정수 위치</b>로 되써서,
/// 그 값에서 다음 칸을 계산하면 오차가 칸마다 쌓였다. 둘 다 실제 HWND의 WM_WINDOWPOSCHANGED와
/// <c>GetWindowRect</c>로만 볼 수 있다.
///
/// 휠은 <c>PostMessage(WM_MOUSEWHEEL)</c>로 핀 HWND에 직접 넣는다 — WPF가 실제 휠을 받는 경로(HwndSource 창 프로시저 →
/// HwndMouseInputProvider)를 그대로 타면서, 전역 <c>SendInput</c>처럼 커서 아래 다른 앱으로 새지 않는다.
/// 커서는 <c>SetCursorPos</c>로 핀 안의 치우친 지점(가로 30%, 세로 70%)에 두고 끝나면 되돌린다.
/// "커서 아래 이미지 점"은 창 사각형 기준으로 잰다: 처음 커서 아래였던 기준 좌표 u가 지금 화면 어디에 그려지는지
/// (<c>X + u·W/기준폭</c>)와 커서의 차이다.
/// </summary>
public class PinZoomSmoothnessTests(ITestOutputHelper output)
{
    private const int WmMouseWheel = 0x020A;
    private const int WmWindowPosChanged = 0x0047;
    private const int Notch = 120;

    /// <summary>WM_WINDOWPOSCHANGED 한 번이 남긴 창 사각형 변화 (그 시점의 <c>GetWindowRect</c>).</summary>
    private readonly record struct GeometryChange(PhysicalRect Before, PhysicalRect After)
    {
        public bool Moved => Before.X != After.X || Before.Y != After.Y;

        public bool Resized => Before.Width != After.Width || Before.Height != After.Height;

        public bool Changed => Moved || Resized;

        public override string ToString() =>
            $"{(Moved && Resized ? "이동+크기" : Moved ? "이동만" : Resized ? "크기만" : "변화없음")} {After}";
    }

    [Fact]
    public void Wheel_EachNotch_MovesAndResizesInOneWindowPosChange() => StaRunner.Run(() =>
    {
        using var rig = new Rig(TestImages.Solid(400, 300), tx: 0.3, ty: 0.7);
        var failures = new List<string>();

        // 한 칸씩 넣고 매번 끝까지 펌프한다 — 칸마다 창 적용이 몇 번 나가는지를 본다.
        int[] notches = [Notch, Notch, Notch, Notch, Notch, -Notch, -Notch, -Notch, -Notch, -Notch];
        for (int i = 0; i < notches.Length; i++)
        {
            rig.Changes.Clear();
            rig.PostWheel(notches[i]);
            StaRunner.PumpMessages();

            var geometric = rig.Changes.Where(c => c.Changed).ToList();
            output.WriteLine($"칸 {i + 1} ({notches[i]:+#;-#}): 사각형 변경 {geometric.Count}회 — {string.Join(" / ", geometric)}");
            if (geometric.Count != 1 || !geometric[0].Moved || !geometric[0].Resized)
            {
                failures.Add($"칸 {i + 1}: {geometric.Count}회 [{string.Join(" / ", geometric)}]");
            }
        }

        Assert.True(failures.Count == 0,
            "칸마다 위치·크기가 WM_WINDOWPOSCHANGED 한 번에 함께 와야 한다 (이동만/크기만 바뀐 중간 상태 금지):\n"
            + string.Join("\n", failures));
    });

    [Fact]
    public void Wheel_TenNotchBurst_KeepsImagePointUnderCursorWithinOnePixel() => StaRunner.Run(() =>
    {
        using var rig = new Rig(TestImages.Solid(400, 300), tx: 0.3, ty: 0.7);

        // 빠르게 굴린 휠: 열 칸이 한꺼번에 큐에 쌓인 뒤 처리된다.
        for (int i = 0; i < 10; i++)
        {
            rig.PostWheel(Notch);
        }
        StaRunner.PumpMessages();

        var bounds = rig.Pin.PhysicalBounds();
        var (dx, dy) = rig.AnchorDeviation(bounds);
        var geometric = rig.Changes.Where(c => c.Changed).ToList();
        output.WriteLine($"10칸 버스트 뒤 {bounds}, 커서 아래 점 이탈 ({dx:F2}, {dy:F2})px, 사각형 변경 {geometric.Count}회");
        foreach (var change in geometric)
        {
            output.WriteLine($"  {change}");
        }

        Assert.True(Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1,
            $"10칸 뒤 커서 아래 이미지 점이 ±1px 안에 있어야 한다: 이탈 ({dx:F2}, {dy:F2})px");
        Assert.True(geometric.All(c => c.Moved && c.Resized),
            $"버스트 중에도 이동만/크기만 바뀐 중간 상태가 없어야 한다: {string.Join(" / ", geometric)}");
        // 대기 중인 휠 여러 개는 한 번의 적용으로 묶인다 (Input 우선순위 예약 — 입력이 남아 있으면 기다린다).
        Assert.Single(geometric);
    });

    [Fact]
    public void Wheel_TenUpThenTenDownOneByOne_ReturnsToTheOriginalRect() => StaRunner.Run(() =>
    {
        using var rig = new Rig(TestImages.Solid(400, 300), tx: 0.3, ty: 0.7);
        var original = rig.Pin.PhysicalBounds();

        for (int i = 0; i < 10; i++)
        {
            rig.PostWheel(Notch);
            StaRunner.PumpMessages();
        }
        var zoomed = rig.Pin.PhysicalBounds();
        var (dx, dy) = rig.AnchorDeviation(zoomed);
        for (int i = 0; i < 10; i++)
        {
            rig.PostWheel(-Notch);
            StaRunner.PumpMessages();
        }
        var back = rig.Pin.PhysicalBounds();
        output.WriteLine($"처음 {original} → 10칸 확대 {zoomed} (이탈 {dx:F2},{dy:F2}) → 10칸 축소 {back}");

        Assert.True(Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1,
            $"한 칸씩 10칸 확대한 뒤에도 커서 아래 이미지 점이 ±1px 안이어야 한다: 이탈 ({dx:F2}, {dy:F2})px");
        // 반올림 오차가 쌓이지 않는다 — N칸 확대 뒤 N칸 축소는 정확히 제자리다.
        Assert.Equal(original, back);
    });

    [Fact]
    public void ResetZoom_AfterZooming_ReturnsToThePhysicalBaseSizeAroundTheCenter() => StaRunner.Run(() =>
    {
        using var rig = new Rig(TestImages.Solid(400, 300), tx: 0.3, ty: 0.7);
        for (int i = 0; i < 3; i++)
        {
            rig.PostWheel(Notch);
            StaRunner.PumpMessages();
        }
        var zoomed = rig.Pin.PhysicalBounds();

        rig.Changes.Clear();
        rig.Pin.ResetZoom();
        StaRunner.PumpMessages();
        var reset = rig.Pin.PhysicalBounds();
        var geometric = rig.Changes.Where(c => c.Changed).ToList();
        output.WriteLine($"확대 {zoomed} → 원래 크기 {reset}, 사각형 변경 {geometric.Count}회 [{string.Join(" / ", geometric)}]");

        Assert.Equal(rig.Region.Width, reset.Width);
        Assert.Equal(rig.Region.Height, reset.Height);
        // 중심 고정 (±1px — 이상적 중심은 반 픽셀일 수 있다).
        Assert.InRange((reset.X * 2 + reset.Width) - (zoomed.X * 2 + zoomed.Width), -2, 2);
        Assert.InRange((reset.Y * 2 + reset.Height) - (zoomed.Y * 2 + zoomed.Height), -2, 2);
        Assert.True(geometric.Count == 1 && geometric[0].Moved && geometric[0].Resized,
            $"원래 크기 복귀도 한 번의 이동+크기 적용이어야 한다: [{string.Join(" / ", geometric)}]");
        // 크롬 판정(PinChromeRules)이 읽는 Width/Height DP가 물리 적용을 따라왔다 — WPF가 WM_SIZE로 스스로 맞춘다.
        double dpi = VisualTreeHelper.GetDpi(rig.Pin).DpiScaleX;
        Assert.Equal(reset.Width / dpi, rig.Pin.Width, 3);
        Assert.Equal(reset.Height / dpi, rig.Pin.Height, 3);
    });

    [Fact]
    public void Wheel_AfterExternalMove_AnchorsOnTheMovedWindow() => StaRunner.Run(() =>
    {
        using var rig = new Rig(TestImages.Solid(400, 300), tx: 0.3, ty: 0.7);
        rig.PostWheel(Notch);
        StaRunner.PumpMessages();

        // DragMove처럼 앱 밖에서 창을 옮긴다 — 다음 칸은 옮겨진 실제 창에서 다시 잡아야 한다.
        var before = rig.Pin.PhysicalBounds();
        NativeMethodsProbe.SetWindowPos(
            rig.Pin.Hwnd, 0, before.X + 37, before.Y - 23, 0, 0, 0x0001 | 0x0004 | 0x0010 /* NOSIZE|NOZORDER|NOACTIVATE */);
        StaRunner.PumpMessages();
        var moved = rig.Pin.PhysicalBounds();
        rig.Rebase(moved);

        rig.PostWheel(Notch);
        StaRunner.PumpMessages();
        var after = rig.Pin.PhysicalBounds();
        var (dx, dy) = rig.AnchorDeviation(after);
        output.WriteLine($"외부 이동 {before} → {moved}, 한 칸 뒤 {after}, 이탈 ({dx:F2}, {dy:F2})px");

        Assert.True(Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1,
            $"외부 이동 뒤 첫 칸도 커서 아래 이미지 점을 지켜야 한다: 이탈 ({dx:F2}, {dy:F2})px");
    });

    /// <summary>
    /// 남은 프레임 지연의 실측 기록 (82단계). 창 적용은 한 번이 됐지만, 이동·크기는 <c>SetWindowPos</c> 즉시 반영되고
    /// 새 내용은 WPF가 다시 렌더해 <c>UpdateLayeredWindow</c>로 올린 뒤에야 보인다. 그 사이 화면에 무엇이 비치는지를
    /// 적용 직후(WM_WINDOWPOSCHANGED 안에서 UI 스레드를 잡아 WPF 렌더를 막은 채) BitBlt로 빠르게 찍어 기록한다.
    /// 그림은 왼쪽 빨강·오른쪽 파랑이라 커서 줄에서 경계의 x만 보면 된다 — 옛 내용이 새 원점에 그대로 그려진 프레임이면
    /// 경계가 <c>새 X + 기준폭/2</c>에, 새 내용이면 <c>새 X + 새 폭/2</c>에 있다. 단언은 "결국 새 내용으로 수렴한다"뿐이고,
    /// 중간 프레임과 걸린 시간은 출력으로 남긴다 (구조 변경 없이는 없앨 수 없는 잔여 지연이다).
    /// </summary>
    [Fact]
    public void Wheel_OneNotch_LayeredContentSettlesAndLagIsRecorded() => StaRunner.Run(() =>
    {
        using var rig = new Rig(HalfAndHalf(400, 300), tx: 0.3, ty: 0.7);
        var band = new PhysicalRect(rig.Region.X - 60, rig.CursorY, rig.Region.Width + 120, 1);
        var baseline = PixelProbe.CaptureUntil(band, shot => Boundary(shot) >= 0);
        Assert.NotNull(baseline);
        int before = Boundary(baseline!);
        Assert.True(before >= 0, "기준 프레임에서 빨강→파랑 경계를 찾지 못했다 — 핀이 다른 창에 가려졌는지 확인");

        var timeline = new List<(double Ms, int X)>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool sampled = false;
        rig.OnGeometryChanged = () =>
        {
            if (sampled)
            {
                return;
            }
            sampled = true;
            // UI 스레드를 쥔 채 찍는다 — WPF는 아직 새 크기로 렌더하지 못했다. DWM 합성 몇 프레임 분량(약 80ms).
            clock.Restart();
            while (clock.ElapsedMilliseconds < 80)
            {
                timeline.Add((clock.Elapsed.TotalMilliseconds, Boundary(CaptureService.CaptureRegion(band))));
                Thread.Sleep(4);
            }
        };

        rig.PostWheel(Notch);
        StaRunner.PumpMessages();
        var after = rig.Pin.PhysicalBounds();
        int expected = after.X + after.Width / 2 - band.X;
        int movedOld = after.X + rig.Region.Width / 2 - band.X;

        clock.Restart();
        var settled = PixelProbe.CaptureUntil(band, shot => Math.Abs(Boundary(shot) - expected) <= 2, 2000);
        double settleMs = clock.Elapsed.TotalMilliseconds;
        int final = settled is null ? -1 : Boundary(settled);

        output.WriteLine($"적용 전 경계 x={before}, 적용 뒤 창 {after}: 새 내용이면 x={expected}, 옛 내용이 새 원점에 그려졌으면 x={movedOld}");
        foreach (var (ms, x) in timeline.Where((t, i) => i == 0 || t.X != timeline[i - 1].X))
        {
            string kind = x == before ? "적용 전 프레임" : Math.Abs(x - movedOld) <= 1 ? "옛 내용·새 원점(중간 프레임)"
                : Math.Abs(x - expected) <= 2 ? "새 내용" : x < 0 ? "경계 없음" : "기타";
            output.WriteLine($"  UI 스레드 정지 중 +{ms:F1}ms: x={x} — {kind}");
        }
        output.WriteLine($"펌프 재개 뒤 새 내용 수렴까지 {settleMs:F0}ms (최종 x={final})");
        Assert.True(Math.Abs(final - expected) <= 2,
            $"한 칸 뒤 화면이 새 크기의 내용으로 수렴해야 한다: 기대 x={expected}, 최종 x={final}");

        // 2부: UI 스레드를 쥐지 않은 평소 흐름. 다른 스레드가 쉬지 않고 찍는 동안 다섯 칸을 한 칸씩 넣고,
        // 적용 시각부터 새 내용이 보일 때까지 "옛 내용·새 원점" 프레임이 실제로 화면에 나갔는지 센다.
        rig.OnGeometryChanged = null;
        var samples = new List<(double Ms, int X)>();
        bool stop = false;
        var sampler = new Thread(() =>
        {
            while (!Volatile.Read(ref stop))
            {
                int x = Boundary(CaptureService.CaptureRegion(band));
                lock (samples)
                {
                    samples.Add((clock.Elapsed.TotalMilliseconds, x));
                }
            }
        }) { IsBackground = true };
        clock.Restart();
        sampler.Start();
        var notches = new List<(double AppliedMs, int MovedOld, int Expected)>();
        for (int i = 0; i < 5; i++)
        {
            var prior = rig.Pin.PhysicalBounds();
            double appliedMs = -1;
            rig.OnGeometryChanged = () => appliedMs = clock.Elapsed.TotalMilliseconds;
            rig.PostWheel(Notch);
            var until = DateTime.UtcNow.AddMilliseconds(120);
            while (DateTime.UtcNow < until)
            {
                StaRunner.PumpMessages();
                Thread.Sleep(1);
            }
            var now = rig.Pin.PhysicalBounds();
            notches.Add((appliedMs, now.X + prior.Width / 2 - band.X, now.X + now.Width / 2 - band.X));
        }
        Volatile.Write(ref stop, true);
        sampler.Join();

        int shown = 0;
        (double Ms, int X)[] all;
        lock (samples)
        {
            all = [.. samples];
        }
        for (int i = 0; i < notches.Count; i++)
        {
            var (appliedMs, movedOldX, expectedX) = notches[i];
            double end = i + 1 < notches.Count ? notches[i + 1].AppliedMs : double.MaxValue;
            var window = all.Where(t => t.Ms >= appliedMs && t.Ms < end).ToList();
            var firstNew = window.FirstOrDefault(t => Math.Abs(t.X - expectedX) <= 2);
            var intermediate = window.Where(t => Math.Abs(t.X - movedOldX) <= 1 && (firstNew == default || t.Ms < firstNew.Ms)).ToList();
            if (intermediate.Count > 0)
            {
                shown++;
            }
            string lag = firstNew == default ? "새 내용 못 봄" : $"새 내용까지 {firstNew.Ms - appliedMs:F1}ms";
            string mid = intermediate.Count == 0 ? "중간 프레임 없음"
                : $"중간 프레임 {intermediate[^1].Ms - intermediate[0].Ms + 0.0:F1}ms 동안 {intermediate.Count}샘플";
            output.WriteLine($"평소 흐름 칸 {i + 1}: {lag}, {mid} (샘플 {window.Count}개)");
        }
        output.WriteLine($"평소 흐름 5칸 중 옛 내용·새 원점 프레임이 화면에 나간 칸: {shown}");
    });

    /// <summary>왼쪽 절반 빨강, 오른쪽 절반 파랑 — 경계 x 하나로 그려진 배율과 원점을 읽는다.</summary>
    private static BitmapSource HalfAndHalf(int width, int height)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Red, null, new System.Windows.Rect(0, 0, width / 2, height));
            dc.DrawRectangle(Brushes.Blue, null, new System.Windows.Rect(width / 2, 0, width - width / 2, height));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    /// <summary>한 줄 캡처에서 빨강 바로 뒤(2px 안)에 파랑이 오는 첫 x. 없으면 -1 (배경은 사용자 화면이라 색 하나만으론 못 믿는다).</summary>
    private static int Boundary(BitmapSource row)
    {
        int width = row.PixelWidth;
        var pixels = new byte[width * 4];
        row.CopyPixels(new System.Windows.Int32Rect(0, 0, width, 1), pixels, width * 4, 0);
        bool IsRed(int x) => pixels[x * 4 + 2] > 200 && pixels[x * 4 + 1] < 60 && pixels[x * 4] < 60;
        bool IsBlue(int x) => pixels[x * 4] > 200 && pixels[x * 4 + 1] < 60 && pixels[x * 4 + 2] < 60;
        for (int x = 1; x < width; x++)
        {
            if (IsBlue(x) && (IsRed(x - 1) || (x >= 2 && IsRed(x - 2))))
            {
                return x;
            }
        }
        return -1;
    }

    private static nint WheelParam(int delta) => (nint)(int)((uint)(ushort)(short)delta << 16);

    private static nint PointParam(int x, int y) => (nint)(int)(((uint)(ushort)(short)y << 16) | (ushort)(short)x);

    /// <summary>
    /// 주 모니터 작업 영역 가운데에 핀을 띄우고(<c>PinManager.CreatePin</c>과 같은 Show → PlacePhysical 순서),
    /// WM_WINDOWPOSCHANGED 기록기와 커서를 건다. 끝나면 핀을 닫고 커서를 되돌린다.
    /// </summary>
    private sealed class Rig : IDisposable
    {
        private readonly HwndSourceHook _recorder;
        private readonly HwndSource _source;
        private readonly bool _cursorSaved;
        private readonly NativeMethodsProbe.ProbePoint _savedCursor;
        private PhysicalRect _last;
        private PhysicalRect _anchorFrame;

        public Rig(BitmapSource image, double tx, double ty)
        {
            var monitor = MonitorTopology.Enumerate().FirstOrDefault(m => m.IsPrimary)
                ?? MonitorTopology.Enumerate()[0];
            var work = monitor.WorkArea;
            Region = new PhysicalRect(
                work.X + (work.Width - image.PixelWidth) / 2,
                work.Y + (work.Height - image.PixelHeight) / 2,
                image.PixelWidth,
                image.PixelHeight);
            Pin = new PinWindow(image, Region, () => 0, () => false);
            Pin.Show();
            WindowStyling.PlacePhysical(Pin.Hwnd, Region);
            StaRunner.PumpMessages();

            _last = Pin.PhysicalBounds();
            _anchorFrame = _last;
            _recorder = (nint h, int msg, nint w, nint l, ref bool handled) =>
            {
                if (msg == WmWindowPosChanged)
                {
                    NativeMethodsProbe.GetWindowRect(h, out var r);
                    var now = PhysicalRect.FromLtrb(r.Left, r.Top, r.Right, r.Bottom);
                    var change = new GeometryChange(_last, now);
                    Changes.Add(change);
                    _last = now;
                    if (change.Changed)
                    {
                        OnGeometryChanged?.Invoke();
                    }
                }
                return 0;
            };
            _source = HwndSource.FromHwnd(Pin.Hwnd)!;
            _source.AddHook(_recorder);

            _cursorSaved = NativeMethodsProbe.GetCursorPos(out _savedCursor);
            CursorX = Region.X + (int)(Region.Width * tx);
            CursorY = Region.Y + (int)(Region.Height * ty);
            NativeMethodsProbe.SetCursorPos(CursorX, CursorY);
            // 사용자가 커서를 핀 위로 옮긴 상태를 만든다. WPF 마우스 공급자는 이 창에서 마우스 이동을 받아 활성이 되기 전까지
            // 휠을 "마우스가 있던 다른 소스"로 보고해 핀에 주지 않는다 (실측: 첫 칸 유실). 게시한 WM_MOUSEMOVE만으로는 모자라다 —
            // OS 쪽 추적 창이 아직 핀이 아니면 TrackMouseEvent가 곧바로 WM_MOUSELEAVE를 보내 다시 비활성이 된다.
            // 그래서 SetCursorPos가 합성한 진짜 이동이 도착해 핀이 호버 상태가 될 때까지 기다린다.
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!Pin.IsMouseOver && DateTime.UtcNow < deadline)
            {
                StaRunner.PumpMessages();
                Thread.Sleep(10);
            }
            Assert.True(Pin.IsMouseOver, "커서를 핀 위에 두었는데 WPF가 호버를 보고하지 않는다 — 다른 창이 핀을 덮고 있는지 확인");
            Changes.Clear();
        }

        public PinWindow Pin { get; }

        public PhysicalRect Region { get; }

        public int CursorX { get; }

        public int CursorY { get; }

        public List<GeometryChange> Changes { get; } = [];

        /// <summary>창 사각형이 바뀐 WM_WINDOWPOSCHANGED 안에서 불린다 (UI 스레드를 쥔 채 — 프레임 지연 실측용).</summary>
        public Action? OnGeometryChanged { get; set; }

        public void PostWheel(int delta) =>
            Assert.True(NativeMethodsProbe.PostMessage(Pin.Hwnd, WmMouseWheel, WheelParam(delta), PointParam(CursorX, CursorY)));

        /// <summary>이상적 고정점의 기준 틀을 지금 창으로 다시 잡는다 (외부 이동 뒤).</summary>
        public void Rebase(PhysicalRect frame) => _anchorFrame = frame;

        /// <summary>
        /// 기준 틀에서 커서 아래였던 기준 좌표(이미지 좌표)가 지금 <paramref name="bounds"/>에서 화면 어디에 그려지는지와
        /// 커서의 차이 (물리 px).
        /// </summary>
        public (double Dx, double Dy) AnchorDeviation(PhysicalRect bounds)
        {
            // 고정점은 앱이 읽은 실제 커서다 — 측정 중 진짜 마우스가 움직였으면 이탈은 결함이 아니라 교란이다
            // (전체 스위트 실행 중 한 번 (-12.9, -9.4)px로 관측 — 커서가 약 (+8, +6)px 밀렸다고 보면 맞는 값이라 교란으로 추정). 구분해서 알린다.
            Assert.True(NativeMethodsProbe.GetCursorPos(out var now) && now.X == CursorX && now.Y == CursorY,
                $"측정 중 커서가 ({CursorX}, {CursorY})에서 ({now.X}, {now.Y})로 움직였다 — 실제 마우스 입력이 끼어든 것으로 보인다. 손을 떼고 다시 돌려라.");
            double u = (CursorX - _anchorFrame.X) * (double)Region.Width / _anchorFrame.Width;
            double v = (CursorY - _anchorFrame.Y) * (double)Region.Height / _anchorFrame.Height;
            double drawnX = bounds.X + u * bounds.Width / Region.Width;
            double drawnY = bounds.Y + v * bounds.Height / Region.Height;
            return (drawnX - CursorX, drawnY - CursorY);
        }

        public void Dispose()
        {
            _source.RemoveHook(_recorder);
            Pin.ClosePin();
            StaRunner.PumpMessages();
            if (_cursorSaved)
            {
                NativeMethodsProbe.SetCursorPos(_savedCursor.X, _savedCursor.Y);
            }
        }
    }
}
