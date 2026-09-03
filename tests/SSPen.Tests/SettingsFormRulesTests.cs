using System.Windows.Media;
using SSPen.Annotation;
using SSPen.Settings;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="SettingsFormRules"/>의 증인 (41단계, WI-16/AC-26). 제자리 변형(폼에 없는 필드 보존), 기본 폴더 → 빈 문자열,
/// 모니터 전부 해제 시 첫 항목 복원, 퀵컬러 hex 직렬화를 잠근다.
/// 정직한 표기: 창 안의 컨트롤 → 값 스냅샷(체크박스 9종 읽기)은 여전히 증인이 없다 — 리뷰 게이트.
/// </summary>
public class SettingsFormRulesTests
{
    private const string DefaultFolder = @"C:\\Default\\Folder";

    private static SettingsFormValues Values(
        IReadOnlyList<(string, bool)>? monitors = null, string saveFolder = @"C:\\Custom", IReadOnlyList<Color>? quick = null) =>
        new(
            RunAtLogin: false, CheckUpdateOnStart: true, WheelAdjustsPenSize: false, SyncToolStyles: true,
            BoardAllMonitors: false, DefaultBoardIsBlack: true, QuickColors: quick ?? ColorPalette.DefaultQuickColors,
            HighlightCursor: true, SaveFolder: saveFolder,
            Monitors: monitors ?? [(@"\\.\DISPLAY1", true), (@"\\.\DISPLAY2", false)]);

    [Fact]
    public void ApplyTo_CopiesEveryFormField()
    {
        var target = new AppSettings();

        SettingsFormRules.ApplyTo(target, Values(), DefaultFolder);

        Assert.False(target.RunAtLogin);
        Assert.True(target.CheckUpdateOnStart);
        Assert.False(target.WheelAdjustsPenSize);
        Assert.True(target.SyncToolStyles);
        Assert.False(target.BoardAllMonitors);
        Assert.True(target.DefaultBoardIsBlack);
        Assert.True(target.HighlightCursor);
        Assert.Equal(@"C:\\Custom", target.SaveFolder);
        Assert.Equal([@"\\.\DISPLAY2"], target.DisabledMonitors);
    }

    [Fact]
    public void ApplyTo_PreservesFieldsNotOnForm()
    {
        var target = new AppSettings
        {
            ToolbarLeft = 12.5,
            ToolbarTop = -3,
            FadingSeconds = 2.5,
            PenColor = "#123456",
            PenThickness = 4,
            Hotkeys = { ["undo"] = new HotkeyDef(1, 2) },
        };

        SettingsFormRules.ApplyTo(target, Values(), DefaultFolder);

        Assert.Equal(12.5, target.ToolbarLeft);
        Assert.Equal(-3, target.ToolbarTop);
        Assert.Equal(2.5, target.FadingSeconds);
        Assert.Equal("#123456", target.PenColor);
        Assert.Equal(4, target.PenThickness);
        Assert.Equal(new HotkeyDef(1, 2), target.Hotkeys["undo"]);
    }

    [Fact]
    public void ApplyTo_SaveFolderEqualsDefault_StoresEmpty()
    {
        var target = new AppSettings { SaveFolder = @"C:\\Old" };

        SettingsFormRules.ApplyTo(target, Values(saveFolder: DefaultFolder), DefaultFolder);

        Assert.Equal(string.Empty, target.SaveFolder);
    }

    /// <summary>
    /// 첫 화면을 되살리되 <b>되살렸다고 말한다</b>. 조용히 되돌리던 시절에는 사용자가 자기 설정이
    /// 무시됐다고 읽었고, 창을 다시 열기 전까지 이유를 알 방법이 없었다.
    /// </summary>
    [Fact]
    public void ApplyTo_AllMonitorsUnchecked_RestoresFirstAndReportsIt()
    {
        var target = new AppSettings();

        var result = SettingsFormRules.ApplyTo(target, Values(monitors: [(@"\\.\DISPLAY1", false), (@"\\.\DISPLAY2", false), (@"\\.\DISPLAY3", false)]), DefaultFolder);

        Assert.Equal([@"\\.\DISPLAY2", @"\\.\DISPLAY3"], target.DisabledMonitors);
        Assert.True(result.MonitorSelectionCoerced);
        Assert.Equal(@"\\.\DISPLAY1", result.RestoredDeviceName);
    }

    /// <summary>교정하지 않았으면 알릴 것도 없다.</summary>
    [Fact]
    public void ApplyTo_AtLeastOneMonitorChecked_ReportsNoCoercion()
    {
        var target = new AppSettings();

        var result = SettingsFormRules.ApplyTo(target, Values(monitors: [(@"\\.\DISPLAY1", true), (@"\\.\DISPLAY2", false)]), DefaultFolder);

        Assert.False(result.MonitorSelectionCoerced);
        Assert.Null(result.RestoredDeviceName);
    }

    [Fact]
    public void ApplyTo_NoMonitors_LeavesDisabledEmpty()
    {
        var target = new AppSettings { DisabledMonitors = [@"\\.\DISPLAY9"] };

        var result = SettingsFormRules.ApplyTo(target, Values(monitors: []), DefaultFolder);

        Assert.Empty(target.DisabledMonitors);
        Assert.False(result.MonitorSelectionCoerced);
    }

    [Fact]
    public void ApplyTo_QuickColors_SerializedAsHex_FreshArray()
    {
        var target = new AppSettings();
        var purple = (Color)ColorConverter.ConvertFromString("#7F00FF");

        SettingsFormRules.ApplyTo(target, Values(quick: [purple, purple, purple, purple, purple, purple]), DefaultFolder);

        Assert.All(target.QuickColors, hex => Assert.Equal(ColorPalette.ToHex(purple), hex));
        Assert.Equal(purple, ColorPalette.RestoreQuickColors(target.QuickColors)[0]); // 왕복
    }
}
