using System.Windows.Media.Imaging;
using SSPen.Diagnostics;
using SSPen.Interop;

namespace SSPen.Pin;

/// <summary>
/// 복수 핀 레지스트리 (WI-13). 핀 생성/닫기 전이는 z-밴드 재적용을 트리거한다 (R10).
/// </summary>
public sealed class PinManager
{
    private readonly List<PinWindow> _pins = [];
    private readonly PinClickThroughMonitor _monitor;
    private readonly Func<nint> _zAnchor;

    /// <param name="hooks">복귀 마우스 훅의 OS 이음매 (52단계) — 합성 루트가 <see cref="LowLevelHook.Native"/>를 준다.</param>
    public PinManager(Func<nint> zAnchor, IHookInstaller hooks)
    {
        _zAnchor = zAnchor;
        _monitor = new PinClickThroughMonitor(this, hooks);
    }

    public IReadOnlyList<PinWindow> Pins => _pins;

    /// <summary>핀 생성/닫기 시 발생 — 셸이 z-밴드를 재적용한다.</summary>
    public event Action? PinsChanged;

    public PinWindow CreatePin(BitmapSource image, PhysicalRect region)
    {
        var pin = new PinWindow(image, region, _zAnchor);
        pin.PinClosed += OnPinClosed;
        _pins.Add(pin);
        pin.Show();
        WindowStyling.PlacePhysical(pin.Hwnd, region);
        Log.Info($"핀 생성: {region} (총 {_pins.Count}개)");
        _monitor.Refresh();
        PinsChanged?.Invoke();
        return pin;
    }

    public void NotifyClickThroughChanged() => _monitor.Refresh();

    public void CloseAll()
    {
        foreach (var pin in _pins.ToArray())
        {
            pin.ClosePin();
        }
    }

    public void Dispose()
    {
        CloseAll();
        _monitor.Dispose();
    }

    private void OnPinClosed(PinWindow pin)
    {
        pin.PinClosed -= OnPinClosed;
        _pins.Remove(pin);
        Log.Info($"핀 닫힘 (남은 {_pins.Count}개)");
        _monitor.Refresh();
        PinsChanged?.Invoke();
    }
}
