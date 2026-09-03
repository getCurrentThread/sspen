namespace SSPen.Shell;

/// <summary>
/// 여러 핫키를 한 툴팁 줄로 합치는 표기 (AC-20).
///
/// 그룹 버튼(도형·굵기 미리보기)은 하나의 조합이 아니라 <b>여러 조합</b>에 대응한다. 그대로 이어 붙이면
/// "Alt+Shift+L / Alt+Shift+A / Alt+Shift+U / Alt+Shift+E"처럼 수식키만 네 번 반복돼 30px 버튼 옆
/// 툴팁이 두 줄로 접힌다. 수식키가 모두 같을 때만 접두를 한 번 쓰고 키만 나열한다.
///
/// 순수 함수인 이유: 표기 규칙이 <see cref="ShellHotkeys"/>(Dispatcher가 필요한 클래스) 안에 있으면
/// 헤드리스로 잠글 수 없다.
/// </summary>
public static class HotkeyLabelFormat
{
    /// <summary>
    /// null(미할당)을 걸러 내고 합친다. 남는 것이 없으면 null — 툴팁의 핫키 줄이 통째로 숨는다.
    /// 수식키 접두(마지막 '+'까지)가 전부 같으면 한 번만 쓴다: "Alt+Shift+L / A / U / E".
    /// </summary>
    public static string? Compose(IEnumerable<string?> labels)
    {
        var kept = labels.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l!).ToList();
        if (kept.Count == 0)
        {
            return null;
        }
        if (kept.Count == 1)
        {
            return kept[0];
        }

        string first = kept[0];
        int cut = first.LastIndexOf('+');
        if (cut > 0)
        {
            string prefix = first[..(cut + 1)];
            // 접두보다 긴지도 본다 — "Alt+Shift+"만으로 끝나는 라벨은 키가 없다는 뜻이라 접을 수 없다.
            if (kept.All(l => l.Length > prefix.Length && l.StartsWith(prefix, StringComparison.Ordinal)))
            {
                return prefix + string.Join(" / ", kept.Select(l => l[prefix.Length..]));
            }
        }
        return string.Join(" / ", kept);
    }
}
