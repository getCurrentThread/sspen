using System.Windows.Media;
using SSPen.Annotation;
using SSPen.Settings;

namespace SSPen.Shell;

/// <summary>설정 창 폼의 값 스냅샷 (41단계). 컨트롤에서 값을 읽는 것은 창이, 값 → AppSettings 매핑은 <see cref="SettingsFormRules"/>가 맡는다.</summary>
public readonly record struct SettingsFormValues(
    bool RunAtLogin,
    bool CheckUpdateOnStart,
    bool WheelAdjustsPenSize,
    bool SyncToolStyles,
    bool BoardAllMonitors,
    bool DefaultBoardIsBlack,
    IReadOnlyList<Color> QuickColors,
    bool HighlightCursor,
    string SaveFolder,
    IReadOnlyList<(string DeviceName, bool Enabled)> Monitors);

/// <summary>적용 결과. 규칙이 사용자 입력을 <b>교정</b>했다면 무엇을 되살렸는지 알린다.</summary>
public readonly record struct SettingsApplyResult(bool MonitorSelectionCoerced, string? RestoredDeviceName);

/// <summary>
/// 폼 값 → AppSettings 매핑의 순수 규칙 (41단계, WI-16/AC-26). ToolbarStateMap 선례대로 컨트롤→값은 창이, 값→설정은 여기가.
///
/// 가장 큰 함정은 새 <see cref="AppSettings"/>를 만드는 것이다 — 폼에 없는 필드(Hotkeys, ToolbarLeft/Top, FadingSeconds,
/// 도구별 색·굵기)가 소실된다. 그래서 <see cref="ApplyTo"/>는 <b>제자리 변형</b>이며, 호출자는 <c>ISettingsHost.Settings</c>를
/// 그대로 넘기고 <c>ApplyGeneralSettings</c>를 정확히 1회 부른다 (오늘의 SettingsWindow.Apply와 같다).
/// </summary>
public static class SettingsFormRules
{
    /// <param name="defaultSaveFolder">저장 폴더가 이 값과 같으면 설정에는 빈 문자열(= 기본 폴더 사용)로 적는다.</param>
    public static SettingsApplyResult ApplyTo(AppSettings target, SettingsFormValues values, string defaultSaveFolder)
    {
        target.RunAtLogin = values.RunAtLogin;
        target.CheckUpdateOnStart = values.CheckUpdateOnStart;
        target.WheelAdjustsPenSize = values.WheelAdjustsPenSize;
        target.SyncToolStyles = values.SyncToolStyles;
        target.BoardAllMonitors = values.BoardAllMonitors;
        target.DefaultBoardIsBlack = values.DefaultBoardIsBlack;
        target.QuickColors = [.. values.QuickColors.Select(ColorPalette.ToHex)];
        target.HighlightCursor = values.HighlightCursor;
        target.SaveFolder = values.SaveFolder == defaultSaveFolder ? string.Empty : values.SaveFolder;

        var disabled = values.Monitors
            .Where(m => !m.Enabled)
            .Select(m => m.DeviceName)
            .ToList();
        // 모든 모니터가 비활성화되는 것을 방지: 최소 1개는 켠다 (첫 항목 복원).
        // 되살렸다는 사실을 **반환**한다 — 조용히 되돌리면 사용자는 자기 설정이 무시됐다고 읽고,
        // 창을 다시 열어 보기 전까지는 왜 그런지 알 방법이 없다.
        string? restored = null;
        if (values.Monitors.Count > 0 && disabled.Count == values.Monitors.Count)
        {
            restored = disabled[0];
            disabled.RemoveAt(0);
        }
        target.DisabledMonitors = disabled;
        return new SettingsApplyResult(restored is not null, restored);
    }
}
