namespace SSPen.Shell;

/// <summary>설정 창의 섹션 (표시 순서는 <see cref="SettingsSectionPlan.Order"/>).</summary>
public enum SettingsSection
{
    General,
    Monitors,
    QuickColors,
    Hotkeys,
}

/// <summary>
/// 설정 창 골격 판정 (순수 코어).
///
/// 고치는 문제: 창은 <c>ResizeMode.NoResize</c>에 높이가 손으로 맞춘 상수(560)였고, 그 안에 단축키
/// 21행이 끝없이 이어져 있었다. 화면이 작거나 DPI가 높으면 아래 버튼 줄까지 닿지 못하고,
/// 21행 중 하나를 찾으려면 매번 스크롤로 훑어야 했다. 단축키 섹션을 기본 접힘 + 검색으로 바꾸고
/// 창을 사용자가 늘릴 수 있게 한다.
///
/// 여기 있는 것은 전부 값 판정이라 헤드리스로 잠긴다 — 창은 이 답을 바르기만 한다.
/// </summary>
public static class SettingsSectionPlan
{
    /// <summary>섹션 표시 순서 (일반 → 판서 화면 → 바로가기 색상 → 단축키).</summary>
    public static readonly IReadOnlyList<SettingsSection> Order =
        [SettingsSection.General, SettingsSection.Monitors, SettingsSection.QuickColors, SettingsSection.Hotkeys];

    /// <summary>
    /// 처음 열었을 때 펼쳐져 있는가. 단축키만 접혀 있다 — 21행은 나머지 전 섹션을 합친 것보다 길어서
    /// 펼쳐 두면 사용자가 자주 바꾸는 일반 항목이 화면 밖으로 밀린다.
    /// </summary>
    public static bool StartsExpanded(SettingsSection section) => section != SettingsSection.Hotkeys;

    /// <summary>창 최소 크기 — 이보다 작으면 조합 버튼(160)과 라벨(220)이 겹친다.</summary>
    public const double MinWidth = 460;

    /// <summary>단축키를 접은 상태에서 버튼 줄까지 보이는 높이.</summary>
    public const double MinHeight = 360;

    /// <summary>기본 높이. 최소 높이보다 커야 첫 실행에서 스크롤이 필요 없다.</summary>
    public const double DefaultHeight = 560;

    /// <summary>
    /// 검색어 정규화: 앞뒤 공백을 떼고, 남는 것이 없으면 null(= 필터 없음)이다.
    /// </summary>
    public static string? NormalizeQuery(string? query)
    {
        string trimmed = (query ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>
    /// 한 줄이 검색어에 걸리는가. 이름과 <b>조합 표기</b> 둘 다 본다 — "Alt+Shift+S가 뭐였지"로 찾는 것이
    /// "캡처가 무슨 키였지"만큼 흔하다. 대소문자는 무시한다(사용자는 "alt+s"라고 친다).
    /// </summary>
    public static bool MatchesHotkeyFilter(string name, string combo, string? query)
    {
        if (NormalizeQuery(query) is not { } needle)
        {
            return true;
        }
        return name.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || combo.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}
