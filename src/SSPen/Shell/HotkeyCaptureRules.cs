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

    /// <summary>
    /// 조합 모양이지만 전역으로 등록하면 안 된다 — 입력을 삼키고 지금 조합은 그대로 두며,
    /// <see cref="HotkeyCaptureVerdict.Reason"/>의 이유를 보여 준다 (80단계, A6-7: Shift 단독 + 글자 입력 키).
    /// </summary>
    Rejected,
}

/// <summary>
/// 판정 결과. <see cref="Captured"/>는 <see cref="HotkeyCaptureAction.Capture"/>일 때만,
/// <see cref="Reason"/>(사용자에게 보일 거부 이유, <see cref="Strings"/>의 문구)은 <see cref="HotkeyCaptureAction.Rejected"/>일 때만 null이 아니다.
/// </summary>
public readonly record struct HotkeyCaptureVerdict(HotkeyCaptureAction Action, HotkeyDef? Captured, string? Reason = null);

/// <summary>
/// 키 입력 → 조합 판정의 순수 코어 (67단계, A6-5). 예전에는 <see cref="HotkeyCaptureDialog"/>의
/// <c>OnPreviewKeyDown</c> 안에만 있어 어떤 입력을 조합으로 받고, 무시하고, 대화상자 조작으로 흘리는지에
/// 헤드리스 증인이 없었다. 창은 이 답에 따라 <c>e.Handled</c>와 표시 라벨만 바꾼다.
///
/// 판정 순서가 계약이다: 수식키 단독 → 대화상자 조작 키 → 수식키 매핑 → 수식키 없음 → Shift 단독 + 글자 입력 키(거부).
/// 조작 키를 수식키 매핑보다 먼저 보므로 Ctrl+Esc·Shift+Esc 같은 조합도 기본 처리로 흘러 취소(IsCancel)가 동작한다.
/// Windows 수식키는 전역 핫키 조합에 넣지 않고 조용히 버린다 (Win+Alt+X → Alt+X) — 특성화 테스트가 이 동작을 고정한다.
///
/// Shift 단독 거부의 이유 (80단계, A6-7): "최소 1개의 수식키"의 뜻은 "다른 앱의 입력을 빼앗지 않는다"인데, Shift는 글자 입력의 일부다.
/// Shift+P를 <c>RegisterHotKey</c>로 전역 등록하면 그때부터 모든 앱에서 대문자 P(Shift+1이면 '!')를 칠 수 없다 — <c>WM_HOTKEY</c>가 삼킨다.
/// 그래서 등록될 수식키가 Shift 하나뿐이고 키가 <see cref="IsTextInputKey"/>이면 거부한다. 판정은 등록될 mods를 보므로 버려지는
/// Win은 셈하지 않는다 (Win+Shift+P도 거부). F키·PrtScn·방향키·Insert/Delete 같은 비입력 키는 Shift 하나로도 지금처럼 잡는다.
/// 이미 settings.json에 저장된 Shift 단독 조합은 마이그레이션하지 않는다 — 계속 등록되며, 사용자가 재지정해 벗어난다.
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

        var vk = (uint)KeyInterop.VirtualKeyFromKey(k);
        if (mods == NativeMethods.MOD_SHIFT && IsTextInputKey(vk))
        {
            // Shift는 글자 입력의 일부다 — 전역 등록하면 모든 앱의 대문자·기호 입력을 삼킨다 (80단계, A6-7).
            return new(HotkeyCaptureAction.Rejected, null, Strings.HotkeyShiftOnlyRejected);
        }

        return new(HotkeyCaptureAction.Capture, new HotkeyDef(mods, vk));
    }

    /// <summary>
    /// 이 가상 키가 Shift와 함께 눌리면 글자(대문자·기호)를 입력하는가 (80단계, A6-7). 집합은 명시한다:
    /// Space(0x20), 숫자 0–9(0x30–0x39), A–Z(0x41–0x5A), 숫자패드(0x60–0x6F), OEM 기호(0xBA–0xC0, 0xDB–0xDF, 0xE2).
    /// F1–F24·PrtScn·방향키·Insert/Delete/Home/End/PageUp/PageDown 같은 비입력 키는 밖이다.
    /// </summary>
    public static bool IsTextInputKey(uint vk) => vk switch
    {
        0x20 => true,
        >= 0x30 and <= 0x39 => true,
        >= 0x41 and <= 0x5A => true,
        >= 0x60 and <= 0x6F => true,
        >= 0xBA and <= 0xC0 => true,
        >= 0xDB and <= 0xDF => true,
        0xE2 => true,
        _ => false,
    };
}
