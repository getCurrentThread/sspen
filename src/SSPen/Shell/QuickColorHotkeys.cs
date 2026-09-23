using SSPen.Interop;
using SSPen.Settings;

namespace SSPen.Shell;

/// <summary>
/// 바로가기 색상 여섯 칸의 고정 조합 <c>Ctrl+Shift+1~6</c>의 단일 소유자 (67단계, A9-3).
///
/// 예전에는 한 가지 사실이 다섯 곳에 손으로 적혀 있었다: 등록 조합(<see cref="ShellHotkeys.BuildHotkeyMap"/>),
/// 충돌 판정의 예약 조합(<see cref="HotkeyConflictRules"/> — 주석에 "같은 값"이라고 적어 둔 것 자체가 드리프트의 자백),
/// 표시 라벨 세 벌(바인딩 표시명, 툴바 툴팁, 설정 창 칸 툴팁), 그리고 생성 측과 파싱 측이 다른 파일에 있던
/// 툴팁 id 프로토콜 <c>"quickcolor:n"</c>. 조합을 하나 바꾸면 등록만 바뀌고 충돌 규칙·라벨은 옛 조합을 가르쳤다.
/// 라벨은 이제 다른 모든 핫키와 같이 <see cref="HotkeyFormatting.Format"/>을 거친다.
///
/// <c>slot</c>은 전부 0-기반이다. 툴팁 id의 숫자만 사람이 읽는 1-기반이다.
/// 사용자 문구 <see cref="Strings.SettingsQuickColorsHint"/>는 const로 남고 <c>QuickColorHotkeysTests</c>가 같은 조합임을 잠근다.
/// </summary>
public static class QuickColorHotkeys
{
    /// <summary>여섯 칸이 공유하는 수식키.</summary>
    public const uint Modifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT;

    private const string TooltipPrefix = "quickcolor:";

    /// <summary>칸의 등록 조합: 수식키 + 숫자 키 <c>1</c>부터.</summary>
    public static HotkeyDef For(int slot) => new(Modifiers, VirtualKeys.D1 + (uint)slot);

    /// <summary>칸의 조합 표기 ("Ctrl+Shift+1").</summary>
    public static string Label(int slot) => HotkeyFormatting.Format(For(slot));

    /// <summary>칸의 표시명 ("퀵컬러 1") — 바인딩 표시명과 충돌 알림이 같은 이름을 쓴다.</summary>
    public static string Name(int slot) => $"{Strings.QuickColorName} {slot + 1}";

    /// <summary>툴바 칸의 툴팁 핫키 id ("quickcolor:1"). <see cref="ShellHotkeys.HotkeyLabel"/>이 <see cref="TryParseTooltipId"/>로 되읽는다.</summary>
    public static string TooltipId(int slot) => $"{TooltipPrefix}{slot + 1}";

    /// <summary>
    /// <see cref="TooltipId"/>의 역. 접두가 맞고 뒤가 정수면 0-기반 칸을 돌려준다.
    /// 칸 수 범위는 검사하지 않는다 — 예전 파서(<c>int.TryParse</c> 뒤 그대로 표기)와 같은 관용이다.
    /// </summary>
    public static bool TryParseTooltipId(string id, out int slot)
    {
        if (id.StartsWith(TooltipPrefix, StringComparison.Ordinal)
            && int.TryParse(id[TooltipPrefix.Length..], out int oneBased))
        {
            slot = oneBased - 1;
            return true;
        }
        slot = -1;
        return false;
    }
}
