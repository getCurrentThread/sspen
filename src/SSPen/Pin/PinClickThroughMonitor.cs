using System.Runtime.InteropServices;
using SSPen.Diagnostics;
using SSPen.Interop;

namespace SSPen.Pin;

/// <summary>
/// 클릭 통과 상태 핀의 복귀 경로 (AC-17 왕복 토글 보장).
/// WS_EX_TRANSPARENT 핀은 창 입력을 받을 수 없으므로, 통과 핀이 1개 이상일 때만
/// WH_MOUSE_LL 훅을 걸어 Ctrl+가운데 버튼이 통과 핀 사각형 위에서 눌리면 다시 토글한다.
/// 통과 핀이 없으면 훅을 즉시 해제한다 (상시 훅 금지). 훅 배관은 <see cref="LowLevelHook"/>(52단계) —
/// 자체 DllImport 넷은 NativeMethods로 합쳤고 Ctrl은 <see cref="KeyboardState.Control"/>로 읽는다.
/// </summary>
public sealed class PinClickThroughMonitor : IDisposable
{
    private readonly PinManager _manager;
    private readonly LowLevelHook _hook;

    /// <param name="hooks">OS 훅 이음매 — 프로덕션은 <see cref="LowLevelHook.Native"/>, 테스트는 가짜 (52단계).</param>
    public PinClickThroughMonitor(PinManager manager, IHookInstaller hooks)
    {
        _manager = manager;
        _hook = new LowLevelHook(NativeMethods.WH_MOUSE_LL, OnMouseEvent, hooks);
    }

    /// <summary>통과 핀 존재 여부에 따라 훅 설치/해제.</summary>
    public void Refresh()
    {
        bool needHook = _manager.Pins.Any(p => p.IsClickThrough);
        if (needHook && !_hook.IsInstalled)
        {
            bool installed = _hook.Install();
            Log.Info($"핀 복귀 마우스 훅 설치 {(installed ? "완료" : "실패")}");
        }
        else if (!needHook && _hook.IsInstalled)
        {
            _hook.Uninstall();
            Log.Info("핀 복귀 마우스 훅 해제");
        }
    }

    /// <summary>래치하지 않는다 — Dispose 뒤 Refresh는 통과 핀이 남아 있으면 다시 건다 (52단계 이전과 같은 의미; PinManager.Dispose는 CloseAll이 먼저다).</summary>
    public void Dispose() => _hook.Dispose();

    /// <summary>훅 콜백 (nCode &lt; 0 통과와 "소비 = 1 반환"은 <see cref="LowLevelHook"/> 소유). true = 소비.</summary>
    private bool OnMouseEvent(nint wParam, nint lParam)
    {
        // 아키텍트 B1: WPF Keyboard.Modifiers는 스레드 로컬 입력 상태라 타 앱이 포그라운드일 때
        // 항상 None을 반환한다 — 전역 훅에서는 비동기 키 상태(KeyboardState)를 읽어야 한다.
        // 메시지를 먼저 거른다: WM_MOUSEMOVE 홍수에서 OS 키 상태를 읽지 않기 위해서다.
        if ((int)wParam != NativeMethods.WM_MBUTTONDOWN || !KeyboardState.Control)
        {
            return false;
        }

        var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
        foreach (var pin in _manager.Pins)
        {
            if (pin.IsClickThrough && pin.PhysicalBounds().Contains(data.pt.X, data.pt.Y))
            {
                pin.Dispatcher.BeginInvoke(() =>
                {
                    pin.SetClickThrough(false);
                    _manager.NotifyClickThroughChanged();
                });
                return true; // 소비: 하위 창으로 보내지 않는다.
            }
        }
        return false;
    }
}
