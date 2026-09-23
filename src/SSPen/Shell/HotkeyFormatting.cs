using SSPen.Interop;
using SSPen.Settings;

namespace SSPen.Shell;

// 67단계(A6-5): HotkeyCaptureDialog.cs 꼬리에서 자기 파일로 옮겼다 — 설정 창·툴팁·로그·검색 필터
// (SettingsSectionPlan.MatchesHotkeyFilter)가 모두 쓰는 표기 규칙이라 대화상자에 묻혀 있을 이유가 없다. 내용은 그대로다.
/// <summary>핫키 조합 표기 (키캡 이름 — Epic Pen 한국어 UI와 동일하게 키 이름은 그대로 표기).</summary>
public static class HotkeyFormatting
{
    public static string Format(HotkeyDef def)
    {
        var parts = new List<string>(4);
        if ((def.Modifiers & NativeMethods.MOD_CONTROL) != 0)
        {
            parts.Add("Ctrl");
        }
        if ((def.Modifiers & NativeMethods.MOD_ALT) != 0)
        {
            parts.Add("Alt");
        }
        if ((def.Modifiers & NativeMethods.MOD_SHIFT) != 0)
        {
            parts.Add("Shift");
        }
        parts.Add(KeyName(def.VirtualKey));
        return string.Join("+", parts);
    }

    private static string KeyName(uint vk) => vk switch
    {
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        VirtualKeys.OemOpenBracket => "[",
        VirtualKeys.OemCloseBracket => "]",
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        0x20 => "Space",
        0x2C => "PrtScn",
        _ => $"0x{vk:X2}",
    };
}
