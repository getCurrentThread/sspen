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

    /// <summary>
    /// Ctrl 읽기의 단일 소스 (D3, 81단계 A7-6) — 복귀 훅(<see cref="PinClickThroughMonitor"/>)과 핀 창의
    /// Ctrl+휠·Ctrl+가운데 버튼이 <b>같은 인스턴스</b>를 읽는다. 핀은 <c>ShowActivated=false</c>라 포그라운드가 아닐 때가
    /// 보통인데, 스레드 로컬 <c>Keyboard.Modifiers</c>는 그때 None이라 같은 제스처를 두 경로가 다르게 읽던 이중 기준이었다.
    /// </summary>
    private readonly Func<bool> _controlDown = () => KeyboardState.Control;

    /// <param name="hooks">복귀 마우스 훅의 OS 이음매 (52단계) — 합성 루트가 <see cref="LowLevelHook.Native"/>를 준다.</param>
    public PinManager(Func<nint> zAnchor, IHookInstaller hooks)
    {
        _zAnchor = zAnchor;
        // Ctrl 읽기는 KeyboardState(D3)를 주입한다 — 모니터의 헤드리스 증인이 OS를 읽지 않게 (53단계).
        // List<PinWindow> → IReadOnlyList<IClickThroughPin>은 IReadOnlyList<out T> 공변성이다 (핀을 구조체 컬렉션에 담으면 깨진다).
        _monitor = new PinClickThroughMonitor(
            pins: () => _pins,
            controlDown: _controlDown,
            clickThroughChanged: NotifyClickThroughChanged,
            hooks: hooks);
    }

    public IReadOnlyList<PinWindow> Pins => _pins;

    /// <summary>핀 생성/닫기 시 발생 — 셸이 z-밴드를 재적용한다.</summary>
    public event Action? PinsChanged;

    /// <summary>
    /// 어떤 핀의 클릭 통과가 켜졌다 — 셸이 되찾는 제스처를 알리는 계기다.
    /// 상시 배지가 상태는 보여 주지만, 되돌리는 방법은 어딘가에서 한 번은 말해 줘야 한다.
    /// </summary>
    public event Action? ClickThroughEngaged;

    public PinWindow CreatePin(BitmapSource image, PhysicalRect region)
    {
        var pin = new PinWindow(image, region, _zAnchor, _controlDown);
        Adopt(pin);
        pin.Show();
        WindowStyling.PlacePhysical(pin.Hwnd, region);
        Log.Info($"핀 생성: {region} (총 {_pins.Count}개)");
        _monitor.Refresh();
        PinsChanged?.Invoke();
        return pin;
    }

    /// <summary>
    /// 핀을 레지스트리에 들이고 수명·통과 이벤트를 배선한다 (83단계, A7-1). <see cref="CreatePin"/>이 쓰고, 헤드리스 증인은
    /// 띄우지 않은 핀으로 직접 부른다 — 표시·배치·<see cref="PinsChanged"/>는 여기서 하지 않는다.
    /// 통과 토글은 <b>반드시</b> 복귀 훅의 Refresh 계기여야 한다(AGENTS L35 부류, AC-17): 핀이 스스로 통과를 켜는 두 경로
    /// (크롬 '클릭 통과' 버튼, 창 안 Ctrl+가운데 버튼)는 <see cref="PinWindow.ClickThroughChanged"/>만 올리므로, 여기서 Refresh하지
    /// 않으면 WH_MOUSE_LL이 걸리지 않고 입력을 받지 못하는 통과 핀을 되찾을 길이 없다. Refresh를 토스트 계기보다 먼저 둔다 —
    /// <see cref="ClickThroughEngaged"/> 처리기가 던져도 훅은 이미 걸려 있어야 한다.
    /// </summary>
    internal void Adopt(PinWindow pin)
    {
        pin.PinClosed += OnPinClosed;
        pin.ClickThroughChanged += on =>
        {
            _monitor.Refresh();
            if (on)
            {
                ClickThroughEngaged?.Invoke();
            }
        };
        _pins.Add(pin);
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
