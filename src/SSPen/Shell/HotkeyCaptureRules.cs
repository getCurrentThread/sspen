using System.Windows.Input;
using SSPen.Interop;
using SSPen.Settings;

namespace SSPen.Shell;

/// <summary>핫키 캡처 대화상자가 키 입력 하나를 다루는 방식 (67단계, A6-5).</summary>
public enum HotkeyCaptureAction
{
    /// <summary>조합이 아니다 — 입력을 삼키고 아무것도 바꾸지 않는다 (수식키 단독, 수식키 없는 키).</summary>
    Ignore,

    /// <summary>대화상자 조작 키(Esc/Enter/Tab) — 기본 처리로 흘려 취소·확인·포커스 이동이 그대로 동작하게 둔다.</summary>
    PassThrough,

    /// <summary>조합 확정 — <see cref="HotkeyCaptureVerdict.Captured"/>에 담긴다.</summary>
    Capture,
}

/// <summary>판정 결과. <see cref="Captured"/>는 <see cref="HotkeyCaptureAction.Capture"/>일 때만 null이 아니다.</summary>
public readonly record struct HotkeyCaptureVerdict(HotkeyCaptureAction Action, HotkeyDef? Captured);

/// <summary>
/// 키 입력 → 조합 판정의 순수 코어 (67단계, A6-5). 예전에는 <see cref="HotkeyCaptureDialog"/>의
/// <c>OnPreviewKeyDown</c> 안에만 있어 어떤 입력을 조합으로 받고, 무시하고, 대화상자 조작으로 흘리는지에
/// 헤드리스 증인이 없었다. 창은 이 답에 따라 <c>e.Handled</c>와 표시 라벨만 바꾼다.
///
/// 판정 순서가 계약이다: 수식키 단독 → 대화상자 조작 키 → 수식키 매핑 → 수식키 없음. 조작 키를 수식키 매핑보다 먼저 보므로
/// Ctrl+Esc 같은 조합도 기본 처리로 흘러 취소(IsCancel)가 동작한다.
/// Windows 수식키는 전역 핫키 조합에 넣지 않고 조용히 버린다 (Win+Alt+X → Alt+X) — 특성화 테스트가 이 동작을 고정한다.
/// </summary>
public static class HotkeyCaptureRules
{
    /// <param name="key">WPF가 보고한 키. Alt가 눌리면 <see cref="Key.System"/>으로 온다.</param>
    /// <param name="systemKey"><paramref name="key"/>가 <see cref="Key.System"/>일 때의 실제 키.</param>
    /// <param name="modifiers">눌린 수식키 (<c>Keyboard.Modifiers</c>).</param>
    public static HotkeyCaptureVerdict Decide(Key key, Key systemKey, ModifierKeys modifiers)
    {
        var k = key == Key.System ? systemKey : key;
        if (k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None)
        {
            return new(HotkeyCaptureAction.Ignore, null); // 수식키 단독은 조합이 아니다.
        }
        if (k is Key.Escape or Key.Enter or Key.Tab)
        {
            return new(HotkeyCaptureAction.PassThrough, null); // 대화상자 조작 키는 그대로 둔다.
        }

        uint mods = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            mods |= NativeMethods.MOD_ALT;
        }
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            mods |= NativeMethods.MOD_CONTROL;
        }
        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            mods |= NativeMethods.MOD_SHIFT;
        }
        if (mods == 0)
        {
            return new(HotkeyCaptureAction.Ignore, null); // 전역 핫키는 최소 1개의 수식키가 필요하다.
        }

        return new(HotkeyCaptureAction.Capture, new HotkeyDef(mods, (uint)KeyInterop.VirtualKeyFromKey(k)));
    }
}
