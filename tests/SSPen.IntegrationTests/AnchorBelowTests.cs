using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SSPen.Annotation;
using SSPen.Interop;
using SSPen.Pin;
using SSPen.Shell;
using Xunit;

namespace SSPen.IntegrationTests;

/// <summary>
/// z-밴드 항구 고정 회귀 검증 (사용자 조타 버그 수정: 도구 선택 후 툴바 상호작용 불가).
/// WindowStyling.AnchorBelow 훅이 걸린 창은 HWND_TOPMOST/HWND_TOP 올리기 시도가
/// 앵커 창 바로 아래로 돌려지는지 실제 SetWindowPos + z-순서 워크로 단언한다.
/// 54단계: 활성화된 적 있는 창(IME 창 소유자)의 상승과 밴드 적용의 형제 뒤 삽입이 자기 IME 창 바로 아래로 오는 병리(L1 소유 사슬 판정,
/// ApplyZBand 중 억제)와, 요청 단계 훅 없이 결과 단계 훅(KeepBelow, L2)만으로 복구되는지도 여기서 단언한다.
/// </summary>
public class AnchorBelowTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const uint SwpFlags = 0x0001 | 0x0002 | 0x0010; // NOSIZE | NOMOVE | NOACTIVATE

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WindowPos
    {
        public nint hwnd;
        public nint hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [Fact]
    public void HookedWindow_TopmostRaise_LandsBelowAnchor() => StaRunner.Run(() =>
    {
        var anchor = NewTestWindow(320, 320);
        var hooked = NewTestWindow(340, 340);
        try
        {
            anchor.Show();
            hooked.Show();
            nint anchorHwnd = WindowStyling.GetHwnd(anchor);
            nint hookedHwnd = WindowStyling.GetHwnd(hooked);
            var hook = WindowStyling.AnchorBelow(hookedHwnd, () => anchorHwnd);

            // 앵커를 톱모스트 최상단에 두고, 훅 걸린 창을 그 위로 올리려 시도한다.
            WindowStyling.ApplyZBand([anchorHwnd, hookedHwnd]);
            StaRunner.PumpMessages();

            // HWND_TOPMOST 올리기 → 앵커 바로 아래로 돌려져야 한다.
            NativeMethodsProbe.SetWindowPos(hookedHwnd, (nint)(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, SwpFlags);
            StaRunner.PumpMessages();
            Assert.Equal(hookedHwnd, NativeMethodsProbe.GetWindow(anchorHwnd, NativeMethodsProbe.GwHwndNext));

            // HWND_TOP 올리기도 동일하게 앵커 아래 유지.
            NativeMethodsProbe.SetWindowPos(hookedHwnd, 0 /* HWND_TOP */, 0, 0, 0, 0, SwpFlags);
            StaRunner.PumpMessages();
            Assert.Equal(hookedHwnd, NativeMethodsProbe.GetWindow(anchorHwnd, NativeMethodsProbe.GwHwndNext));

            // HWND_NOTOPMOST 분기 (gen-4 자문): 강등 시 OS가 톱모스트 밴드에서 빼므로 직하 고정은
            // 보장되지 않는다 — 불변식(앵커 위로 올라가지 않음)만 단언하고, 재상승으로 복구를 확인.
            NativeMethodsProbe.SetWindowPos(hookedHwnd, (nint)(-2) /* HWND_NOTOPMOST */, 0, 0, 0, 0, SwpFlags);
            StaRunner.PumpMessages();
            Assert.True(NativeMethodsProbe.IsBelowByNextWalk(anchorHwnd, hookedHwnd));

            NativeMethodsProbe.SetWindowPos(hookedHwnd, (nint)(-1) /* HWND_TOPMOST 재상승 */, 0, 0, 0, 0, SwpFlags);
            StaRunner.PumpMessages();
            Assert.Equal(hookedHwnd, NativeMethodsProbe.GetWindow(anchorHwnd, NativeMethodsProbe.GwHwndNext));

            // 훅 델리게이트는 검증 동안 살아 있어야 한다 (GC 핀 의도 명시, gen-4 자문).
            GC.KeepAlive(hook);
        }
        finally
        {
            hooked.Close();
            anchor.Close();
        }
    });

    /// <summary>
    /// 54단계 L1 회귀: 훅 걸린 창이 한 번 <b>활성화</b>되면(텍스트 도구의 Activate) 스레드 기본 IME 창의 소유자가 되고,
    /// 포그라운드를 잃었다가 다시 활성화될 때의 상승 요청은 hwndInsertAfter = 그 IME 창으로 온다(실측 S4). 상수만 보던 판정은
    /// 여기서 눈이 멀어 창이 앵커 위로 올라갔다. 소유 사슬 판정이 그 요청도 앵커 아래로 돌려야 한다.
    /// 실제 IME 창이 생기는지는 시스템 IME 구성에 달렸으므로 원시 hwndInsertAfter를 기록해 단언 메시지에 남긴다.
    /// </summary>
    [Fact]
    public void HookedWindow_ActivatedAfterForegroundLoss_LandsBelowAnchor() => StaRunner.Run(() =>
    {
        var anchor = NewTestWindow(360, 360);
        var hooked = NewTestWindow(380, 380);
        hooked.ShowActivated = true;
        var rawInsertAfter = new List<string>();
        try
        {
            anchor.Show();
            hooked.Show();
            nint anchorHwnd = WindowStyling.GetHwnd(anchor);
            nint hookedHwnd = WindowStyling.GetHwnd(hooked);
            var hook = WindowStyling.AnchorBelow(hookedHwnd, () => anchorHwnd);
            // 훅은 최신 순으로 돈다 — 나중에 붙인 기록기가 AnchorBelow보다 먼저 원시 값을 본다.
            System.Windows.Interop.HwndSourceHook recorder = (nint h, int msg, nint w, nint l, ref bool handled) =>
            {
                if (msg == 0x0046 /* WM_WINDOWPOSCHANGING */)
                {
                    var pos = System.Runtime.InteropServices.Marshal.PtrToStructure<WindowPos>(l);
                    if ((pos.flags & 0x0004 /* SWP_NOZORDER */) == 0)
                    {
                        nint owner = NativeMethodsProbe.GetWindow(pos.hwndInsertAfter, NativeMethodsProbe.GwOwner);
                        rawInsertAfter.Add($"0x{pos.hwndInsertAfter:X}(owner=0x{owner:X}{(owner == hookedHwnd ? "=self" : string.Empty)}) flags=0x{pos.flags:X}");
                    }
                }
                return 0;
            };
            System.Windows.Interop.HwndSource.FromHwnd(hookedHwnd)!.AddHook(recorder);

            WindowStyling.ApplyZBand([anchorHwnd, hookedHwnd]);
            StaRunner.PumpMessages();
            // 밴드 적용의 구체 삽입도 같은 모양(insertAfter=IME)으로 기록되므로 지운다 — 아래 단언은 상승 요청만 봐야 한다.
            rawInsertAfter.Clear();

            // S1: 첫 활성화 (IME 창의 소유자가 된다).
            hooked.Activate();
            StaRunner.PumpMessages();
            Assert.True(NativeMethodsProbe.IsBelowByNextWalk(anchorHwnd, hookedHwnd), $"첫 활성화 뒤 앵커 위: {string.Join(" | ", rawInsertAfter)}");

            // S4: 포그라운드를 남에게 넘긴 뒤 다시 활성화 — 상승 요청이 자기 IME 창 바로 아래로 온다.
            nint tray = NativeMethodsProbe.FindWindow("Shell_TrayWnd", null);
            if (tray != 0)
            {
                NativeMethodsProbe.SetForegroundWindow(tray);
                StaRunner.PumpMessages();
            }
            hooked.Activate();
            StaRunner.PumpMessages();
            Assert.True(NativeMethodsProbe.IsBelowByNextWalk(anchorHwnd, hookedHwnd), $"재활성화 뒤 앵커 위: {string.Join(" | ", rawInsertAfter)}");

            // S7: 활성 상태에서 명시적 TOPMOST 올리기도 같은 모양으로 온다.
            NativeMethodsProbe.SetWindowPos(hookedHwnd, (nint)(-1), 0, 0, 0, 0, SwpFlags);
            StaRunner.PumpMessages();
            Assert.True(NativeMethodsProbe.IsBelowByNextWalk(anchorHwnd, hookedHwnd), $"TOPMOST 올리기 뒤 앵커 위: {string.Join(" | ", rawInsertAfter)}");

            System.Windows.Interop.HwndSource.FromHwnd(hookedHwnd)!.RemoveHook(recorder);
            // 병리(자기 IME 창 바로 아래로 오는 상승)가 실제로 재현됐어야 이 테스트가 L1의 증인이다 — 대상 PC(CRIT-2)는 IME가 있다.
            // 포그라운드 양도(SetForegroundWindow)는 OS의 포그라운드 잠금 때문에 매번 성공하지 않으므로 활성화 상승(flags=0x3)은
            // 있으면 좋고, 명시적 TOPMOST 상승(flags=0x13)이 같은 모양(insertAfter=자기 IME 창)으로 오는 것은 항상 단언한다.
            output.WriteLine("원시 hwndInsertAfter: " + string.Join(" | ", rawInsertAfter));
            Assert.Contains(rawInsertAfter, r => r.Contains("=self) flags=0x13"));
            GC.KeepAlive(hook);
        }
        finally
        {
            hooked.Close();
            anchor.Close();
        }
    });

    /// <summary>
    /// 54단계 L1 회귀 2: 밴드 적용의 <b>구체 삽입</b>(이전 서피스 뒤)도 활성화된 적 있는 창에서는 자기 IME 창 바로 아래로 도착한다.
    /// 그것을 상승으로 읽어 앵커로 돌리면 서피스끼리의 순서가 뒤집힌다 — ApplyZBand 동안은 요청 단계 훅이 손대지 않아야 한다.
    /// </summary>
    [Fact]
    public void HookedActivatedWindow_ApplyZBandBehindSibling_KeepsSiblingOrder() => StaRunner.Run(() =>
    {
        var anchor = NewTestWindow(440, 440);
        var sibling = NewTestWindow(460, 460);
        var hooked = NewTestWindow(480, 480);
        hooked.ShowActivated = true;
        try
        {
            anchor.Show();
            sibling.Show();
            hooked.Show(); // 활성화 → IME 창 소유
            nint anchorHwnd = WindowStyling.GetHwnd(anchor);
            nint siblingHwnd = WindowStyling.GetHwnd(sibling);
            nint hookedHwnd = WindowStyling.GetHwnd(hooked);
            var hook = WindowStyling.AnchorBelow(hookedHwnd, () => anchorHwnd);
            var siblingHook = WindowStyling.AnchorBelow(siblingHwnd, () => anchorHwnd);
            // 전제 확인: 형제 뒤 삽입이 실제로 자기 IME 창 바로 아래로 도착했는가 — _applyingBand 억제가 존재하는 이유다.
            var seenOwnImeInsert = false;
            System.Windows.Interop.HwndSourceHook recorder = (nint h, int msg, nint w, nint l, ref bool handled) =>
            {
                if (msg == 0x0046 /* WM_WINDOWPOSCHANGING */)
                {
                    var pos = System.Runtime.InteropServices.Marshal.PtrToStructure<WindowPos>(l);
                    if ((pos.flags & 0x0004) == 0 && NativeMethodsProbe.GetWindow(pos.hwndInsertAfter, NativeMethodsProbe.GwOwner) == hookedHwnd)
                    {
                        seenOwnImeInsert = true;
                    }
                }
                return 0;
            };
            System.Windows.Interop.HwndSource.FromHwnd(hookedHwnd)!.AddHook(recorder);

            WindowStyling.ApplyZBand([anchorHwnd, siblingHwnd, hookedHwnd]);
            StaRunner.PumpMessages();

            System.Windows.Interop.HwndSource.FromHwnd(hookedHwnd)!.RemoveHook(recorder);
            Assert.True(seenOwnImeInsert, "형제 뒤 삽입이 자기 IME 창 바로 아래로 오지 않았다 — 이 머신에서는 병리가 재현되지 않는다.");
            Assert.True(NativeMethodsProbe.IsBelowByNextWalk(anchorHwnd, siblingHwnd));
            Assert.True(NativeMethodsProbe.IsBelowByNextWalk(siblingHwnd, hookedHwnd), "밴드 적용의 형제 뒤 삽입이 앵커 아래로 돌려져 순서가 뒤집혔다.");
            GC.KeepAlive(hook);
            GC.KeepAlive(siblingHook);
        }
        finally
        {
            hooked.Close();
            sibling.Close();
            anchor.Close();
        }
    });

    /// <summary>
    /// 54단계 L2: 요청 단계 훅 없이 결과 단계 훅(KeepBelow)만으로도 올라간 창이 앵커 아래로 돌아온다 —
    /// 요청 해석이 빗나가는 새 경우가 생겨도 결과 불변식으로 잡힌다는 뜻이다.
    /// </summary>
    [Fact]
    public void KeepBelowOnly_TopmostRaise_ReturnsBelowAnchor() => StaRunner.Run(() =>
    {
        var anchor = NewTestWindow(400, 400);
        var kept = NewTestWindow(420, 420);
        try
        {
            anchor.Show();
            kept.Show();
            nint anchorHwnd = WindowStyling.GetHwnd(anchor);
            nint keptHwnd = WindowStyling.GetHwnd(kept);
            var hook = WindowStyling.KeepBelow(keptHwnd, () => anchorHwnd, "테스트 창");

            WindowStyling.ApplyZBand([anchorHwnd, keptHwnd]);
            StaRunner.PumpMessages();
            Assert.True(NativeMethodsProbe.IsBelowByNextWalk(anchorHwnd, keptHwnd));

            NativeMethodsProbe.SetWindowPos(keptHwnd, (nint)(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, SwpFlags);
            StaRunner.PumpMessages();
            Assert.True(NativeMethodsProbe.IsBelowByNextWalk(anchorHwnd, keptHwnd));

            NativeMethodsProbe.SetWindowPos(keptHwnd, 0 /* HWND_TOP */, 0, 0, 0, 0, SwpFlags);
            StaRunner.PumpMessages();
            Assert.True(NativeMethodsProbe.IsBelowByNextWalk(anchorHwnd, keptHwnd));

            GC.KeepAlive(hook);
        }
        finally
        {
            kept.Close();
            anchor.Close();
        }
    });

    /// <summary>
    /// 71단계 사용자 결정(2026-09-23)의 실창 증인: 툴바 &gt; 핀 &gt; 서피스. 합성 루트와 같은 배선 — 핀 앵커 = 툴바,
    /// 서피스 앵커 = <see cref="ZBandOrder.SurfaceAnchor"/> — 으로 실제 서피스 창과 핀 창을 띄우고
    /// <see cref="ZBandOrder.Build"/> 순서로 밴드를 적용한 뒤, 서피스를 HWND_TOP/HWND_TOPMOST로 올려도
    /// 요청·결과 단계 훅(AnchorBelow/KeepBelow)이 핀 아래로 되돌리는지 실제 z-순서 워크로 단언한다.
    /// 핀을 올려도 툴바 위로는 못 가고 서피스 위는 지킨다. 툴바 자리는 톱모스트 테스트 창이 대신한다.
    /// </summary>
    [Fact]
    public void PinOverSurface_SurfaceRaised_ReturnsBelowPin() => StaRunner.Run(() =>
    {
        var toolbar = NewTestWindow(300, 300);
        toolbar.Show();
        nint toolbarHwnd = WindowStyling.GetHwnd(toolbar);
        PinWindow? pin = null;

        var monitor = MonitorTopology.Enumerate()[0];
        var state = new AppState { ActiveTool = ToolKind.Pen };
        var document = new AnnotationDocument(monitor.DeviceName);
        var selection = new SelectionModel();
        selection.AttachTo(document);
        var ledger = new UndoLedger(e => document.Elements.Contains(e) ? document : null, selection);
        var surface = SurfaceRigs.NewSurface(
            monitor, state, document, ledger, selection,
            zAnchor: () => ZBandOrder.SurfaceAnchor(toolbarHwnd, pin is null ? [] : [pin.Hwnd]));
        try
        {
            surface.Show();
            var region = new PhysicalRect(monitor.WorkArea.X + 200, monitor.WorkArea.Y + 200, 240, 180);
            pin = new PinWindow(SolidImage(region.Width, region.Height), region, () => toolbarHwnd, () => false);
            pin.Show();
            WindowStyling.PlacePhysical(pin.Hwnd, region);
            StaRunner.PumpMessages();
            nint pinHwnd = pin.Hwnd;
            nint surfaceHwnd = surface.Hwnd;

            WindowStyling.ApplyZBand(ZBandOrder.Build(0, 0, 0, toolbarHwnd, pins: [pinHwnd], surfaces: [surfaceHwnd]));
            StaRunner.PumpMessages();
            Assert.True(NativeMethodsProbe.IsBelowByNextWalk(toolbarHwnd, pinHwnd), "밴드 적용 뒤 핀이 툴바 위에 있다.");
            Assert.True(NativeMethodsProbe.IsBelowByNextWalk(pinHwnd, surfaceHwnd), "밴드 적용 뒤 서피스가 핀 위에 있다.");

            NativeMethodsProbe.SetWindowPos(surfaceHwnd, 0 /* HWND_TOP */, 0, 0, 0, 0, SwpFlags);
            StaRunner.PumpMessages();
            Assert.True(NativeMethodsProbe.IsBelowByNextWalk(pinHwnd, surfaceHwnd), "HWND_TOP 올리기 뒤 서피스가 핀을 덮었다.");

            NativeMethodsProbe.SetWindowPos(surfaceHwnd, (nint)(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, SwpFlags);
            StaRunner.PumpMessages();
            Assert.True(NativeMethodsProbe.IsBelowByNextWalk(pinHwnd, surfaceHwnd), "HWND_TOPMOST 올리기 뒤 서피스가 핀을 덮었다.");
            Assert.True(NativeMethodsProbe.IsBelowByNextWalk(toolbarHwnd, surfaceHwnd), "서피스가 툴바를 덮었다.");

            NativeMethodsProbe.SetWindowPos(pinHwnd, (nint)(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, SwpFlags);
            StaRunner.PumpMessages();
            Assert.True(NativeMethodsProbe.IsBelowByNextWalk(toolbarHwnd, pinHwnd), "핀 올리기 뒤 핀이 툴바를 덮었다.");
            Assert.True(NativeMethodsProbe.IsBelowByNextWalk(pinHwnd, surfaceHwnd), "핀 올리기 뒤 서피스가 핀 위에 있다.");
        }
        finally
        {
            pin?.ClosePin();
            surface.Close();
            toolbar.Close();
        }
    });

    private static BitmapSource SolidImage(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0xED;     // B
            pixels[i + 1] = 0x95; // G
            pixels[i + 2] = 0x64; // R
            pixels[i + 3] = 0xFF; // A
        }
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
    }

    private static Window NewTestWindow(double left, double top) => new()
    {
        WindowStyle = WindowStyle.None,
        ResizeMode = ResizeMode.NoResize,
        AllowsTransparency = true,
        Background = Brushes.DarkGray,
        Topmost = true,
        ShowInTaskbar = false,
        ShowActivated = false,
        Left = left,
        Top = top,
        Width = 120,
        Height = 120,
    };
}
