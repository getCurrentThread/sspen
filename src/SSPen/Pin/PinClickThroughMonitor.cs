using System.Runtime.InteropServices;
using System.Windows.Threading;
using SSPen.Diagnostics;
using SSPen.Interop;

namespace SSPen.Pin;

/// <summary>
/// 복귀 훅이 핀에 대해 아는 최소 계약 (53단계, RenderTickController의 IFrameSource 선례). 유일한 프로덕션 구현은
/// <see cref="PinWindow"/>(멤버 추가 없음 — <see cref="Dispatcher"/>는 DispatcherObject에서 온다).
/// </summary>
public interface IClickThroughPin
{
    bool IsClickThrough { get; }

    /// <summary>물리 픽셀 기준 창 사각형 (전역 훅 히트테스트용).</summary>
    PhysicalRect PhysicalBounds();

    Dispatcher Dispatcher { get; }

    void SetClickThrough(bool on);
}

/// <summary>
/// 클릭 통과 상태 핀의 복귀 경로 (AC-17 왕복 토글 보장).
/// WS_EX_TRANSPARENT 핀은 창 입력을 받을 수 없으므로, 통과 핀이 1개 이상일 때만
/// WH_MOUSE_LL 훅을 걸어 Ctrl+가운데 버튼이 통과 핀 사각형 위에서 눌리면 다시 토글한다.
/// 통과 핀이 없으면 훅을 즉시 해제한다 (상시 훅 금지). 훅 배관은 <see cref="LowLevelHook"/>(52단계) —
/// 자체 DllImport 넷은 NativeMethods로 합쳤다. 핀 목록·Ctrl 읽기·변경 통지는 주입받는다(53단계): 프로덕션은
/// <see cref="PinManager"/>가 자기 목록과 <see cref="KeyboardState.Control"/>을 배선하고, 헤드리스 증인은 OS를 읽지 않는다.
/// 히트 판정은 순수 함수 <see cref="HitClickThroughPin"/>다.
/// </summary>
public sealed class PinClickThroughMonitor : IDisposable
{
    private readonly Func<IReadOnlyList<IClickThroughPin>> _pins;
    private readonly Func<bool> _controlDown;
    private readonly Action _clickThroughChanged;
    private readonly LowLevelHook _hook;

    /// <param name="pins">현재 핀 목록 (생성 순서 = 겹칠 때 우선순위). 매번 평가한다.</param>
    /// <param name="controlDown">Ctrl이 물리적으로 눌려 있는가 — 프로덕션은 <see cref="KeyboardState.Control"/>.</param>
    /// <param name="clickThroughChanged">복귀 뒤 통지 — 프로덕션은 <c>PinManager.NotifyClickThroughChanged</c>(= Refresh).</param>
    /// <param name="hooks">OS 훅 이음매 — 프로덕션은 <see cref="LowLevelHook.Native"/>, 테스트는 가짜 (52단계).</param>
    public PinClickThroughMonitor(
        Func<IReadOnlyList<IClickThroughPin>> pins,
        Func<bool> controlDown,
        Action clickThroughChanged,
        IHookInstaller hooks)
    {
        _pins = pins;
        _controlDown = controlDown;
        _clickThroughChanged = clickThroughChanged;
        _hook = new LowLevelHook(NativeMethods.WH_MOUSE_LL, OnMouseEvent, hooks);
    }

    /// <summary>통과 핀 존재 여부에 따라 훅 설치/해제.</summary>
    public void Refresh()
    {
        bool needHook = _pins().Any(p => p.IsClickThrough);
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

    /// <summary>
    /// (x, y)를 덮는 첫 통과 핀 (AC-17 복귀 대상). 목록 순서(= 생성 순서, 겹치면 먼저 만든 핀)로 보고, 통과가 아닌 핀의
    /// 사각형은 읽지 않으며 첫 일치에서 멈춘다. 없으면 null. <see cref="PhysicalRect.Contains"/>는 오른쪽·아래 변을 뺀 반개구간이다.
    /// </summary>
    public static IClickThroughPin? HitClickThroughPin(IReadOnlyList<IClickThroughPin> pins, int x, int y)
    {
        foreach (var pin in pins)
        {
            if (pin.IsClickThrough && pin.PhysicalBounds().Contains(x, y))
            {
                return pin;
            }
        }
        return null;
    }

    /// <summary>훅 콜백 (nCode &lt; 0 통과와 "소비 = 1 반환"은 <see cref="LowLevelHook"/> 소유). true = 소비.</summary>
    private bool OnMouseEvent(nint wParam, nint lParam)
    {
        // 아키텍트 B1: WPF Keyboard.Modifiers는 스레드 로컬 입력 상태라 타 앱이 포그라운드일 때
        // 항상 None을 반환한다 — 전역 훅에서는 비동기 키 상태(KeyboardState)를 읽어야 한다.
        // 순서가 계약이다: 메시지 → Ctrl → 디코드 → 핀 목록. WM_MOUSEMOVE 홍수에서 OS 키 상태도 핀 목록도 읽지 않는다.
        if ((int)wParam != NativeMethods.WM_MBUTTONDOWN || !_controlDown())
        {
            return false;
        }

        var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
        var pin = HitClickThroughPin(_pins(), data.pt.X, data.pt.Y);
        if (pin is null)
        {
            return false;
        }

        pin.Dispatcher.BeginInvoke(() =>
        {
            pin.SetClickThrough(false);
            _clickThroughChanged();
        });
        return true; // 소비: 하위 창으로 보내지 않는다.
    }
}
