using System.Runtime.InteropServices;
using System.Windows.Threading;
using SSPen.Annotation;
using SSPen.Diagnostics;
using SSPen.Interop;

namespace SSPen.Shell;

/// <summary>
/// 선택 도구 전용 키 채널 (R3/R4): 수식키 없는 ESC / Delete / Backspace.
///
/// 왜 창 이벤트가 아닌가: 서피스 창은 <c>WS_EX_NOACTIVATE</c> + <c>ShowActivated=false</c>라
/// 키보드 포커스를 <b>구조적으로</b> 가질 수 없다. <c>OnKeyDown</c>은 텍스트 도구의 ARCH-2
/// NOACTIVATE 핸드셰이크 구간에서만 살아 있으므로, 선택 도구에서는 절대 발화하지 않는다.
///
/// 왜 <c>RegisterHotKey</c>가 아닌가: 맨 ESC/Delete/Backspace를 전역 등록하면 SS Pen이 떠 있는
/// 내내 모든 앱에서 그 키를 빼앗는다. 대화상자 취소도, 문서 편집 중 글자 지우기도 죽는다.
///
/// 그래서 <c>PinClickThroughMonitor</c>와 같은 <b>조건부 훅</b> 관용구(<see cref="Interop.LowLevelHook"/>, 52단계)를
/// 쓴다: 필요할 때만 걸고 필요 없어지면 즉시 해제한다. 게이트가 참인 구간(선택 도구 + 선택집합 있음 + 인터랙티브)에서는
/// 서피스가 화면(작업 영역)의 마우스 클릭을 흡수하고 있으므로, 그 키를 가져가는 것도 일관된 동작이다.
/// 상시 훅은 금지다.
///
/// 그 정당화에는 두 개의 <b>구멍</b>이 있어 각각 막아 두었다.
/// (1) 서피스는 <c>WorkArea</c> 크기라 <b>작업표시줄을 덮지 않는다</b> — 트레이 메뉴가 떠 있으면
///     그 ESC는 메뉴의 것이다. 그래서 <c>blocked</c>에 트레이 메뉴 표시 상태가 들어간다.
/// (2) 조합키는 애초에 서피스가 흡수한 적이 없다. 그래서 Ctrl·Alt·Win이 눌려 있으면 통과시킨다
///     (<see cref="Interop.KeyboardState.NonShiftModifier"/>). <b>Shift는 예외로 계속 삼킨다</b> —
///     Shift+클릭·Shift+마퀴로 여러 개 골라 놓고 Shift를 놓기 전에 Delete를 누르는 것이 정상 경로인데,
///     통과시키면 그 Delete가 뒤쪽 앱의 파괴적 Shift+Delete가 된다.
/// </summary>
public sealed class SelectionKeyMonitor : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly AppState _state;
    private readonly SelectionModel _selection;
    private readonly Func<bool> _blocked;
    private readonly Action _clearSelection;
    private readonly Action _deleteSelection;
    private readonly LowLevelHook _hook;
    private bool _disposed;

    /// <param name="blocked">
    /// 훅을 걸면 안 되는 구간 (D7): 캡처 세션·설정 창·트레이 컨텍스트 메뉴. 셋 다 자기 ESC 의미론
    /// (<c>IsCancel</c> 버튼·폴더 선택 대화상자·영역 선택 취소·메뉴 닫기)을 갖고 있어 삼키면 안 된다.
    /// </param>
    /// <param name="hooks">OS 훅 이음매 — 프로덕션은 <see cref="LowLevelHook.Native"/>, 테스트는 가짜 (52단계).</param>
    public SelectionKeyMonitor(
        Dispatcher dispatcher,
        AppState state,
        SelectionModel selection,
        Func<bool> blocked,
        Action clearSelection,
        Action deleteSelection,
        IHookInstaller hooks)
    {
        _dispatcher = dispatcher;
        _state = state;
        _selection = selection;
        _blocked = blocked;
        _clearSelection = clearSelection;
        _deleteSelection = deleteSelection;
        _hook = new LowLevelHook(NativeMethods.WH_KEYBOARD_LL, OnKeyboardEvent, hooks);
    }

    /// <summary>훅이 필요한 상태인가 — 이 술어가 이 클래스의 안전성 전부다.</summary>
    private bool Needed =>
        _state.ActiveTool == ToolKind.Select
        && _state.IsInteractive
        && _selection.Count > 0
        && !_blocked();

    /// <summary>상태 변화마다 호출. 필요하면 설치하고 아니면 해제한다 (상시 훅 금지).</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }
        bool needed = Needed;
        if (needed && !_hook.IsInstalled)
        {
            bool installed = _hook.Install();
            Log.Info($"선택 키 훅 설치 {(installed ? "완료" : "실패")}");
        }
        else if (!needed && _hook.IsInstalled)
        {
            _hook.Uninstall();
            Log.Info("선택 키 훅 해제");
        }
    }

    /// <summary>래치한다: Dispose 뒤 Refresh는 무동작 (래퍼는 래치하지 않으므로 여기서 정한다 — 52단계 이전과 같은 의미).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _hook.Dispose();
    }

    /// <summary>훅 콜백 (nCode &lt; 0 통과와 "소비 = 1 반환"은 <see cref="LowLevelHook"/> 소유). true = 소비.</summary>
    private bool OnKeyboardEvent(nint wParam, nint lParam)
    {
        // WM_SYSKEYDOWN은 **받지 않는다** — 그것은 정의상 Alt가 눌린 키다. Alt+Esc(창 순환)처럼
        // 셸이 소유한 조합을 우리가 삼키면 안 된다. 맨 키는 언제나 WM_KEYDOWN으로 온다.
        if ((int)wParam != NativeMethods.WM_KEYDOWN)
        {
            return false;
        }

        var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
        // 게이트를 훅 안에서 **다시** 판정한다: Refresh는 상태 변화 시점에만 돌므로,
        // 그 사이에 선택이 비거나 캡처가 시작되면 낡은 훅이 남의 앱 키를 삼킬 수 있다.
        if (!Needed)
        {
            return false;
        }

        // 계약은 'Ctrl/Alt/Win 없는' ESC/Delete/Backspace다 (클래스 문서). 게이트가 없으면
        // Ctrl+Backspace 같은 남의 앱 조합키까지 전역으로 삼킨다. Shift가 빠진 이유는
        // KeyboardState.NonShiftModifier 참고 — Shift는 선택 도구 자신의 다중 선택 수식키다.
        if (KeyboardState.NonShiftModifier)
        {
            return false;
        }

        Action? action = (int)data.vkCode switch
        {
            NativeMethods.VK_ESCAPE => _clearSelection,
            NativeMethods.VK_DELETE or NativeMethods.VK_BACK => _deleteSelection,
            _ => null,
        };
        if (action is null)
        {
            return false;
        }

        _dispatcher.BeginInvoke(action);
        return true; // 소비: 하위 창으로 보내지 않는다.
    }
}
