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
        IReadOnlyList<(string, bool)>? monitors = null, string saveFolder = @"C:\\Custom", IReadOnlyList<Color>? quick = null,
        bool zBandPolling = false) =>
        new(
            RunAtLogin: true, CheckUpdateOnStart: true, WheelAdjustsPenSize: false, SyncToolStyles: true,
            BoardAllMonitors: false, DefaultBoardIsBlack: true, QuickColors: quick ?? ColorPalette.DefaultQuickColors,
            HighlightCursor: true, SaveFolder: saveFolder,
            Monitors: monitors ?? [(@"\\.\DISPLAY1", true), (@"\\.\DISPLAY2", false)],
            ZBandPolling: zBandPolling);

    [Fact]
    public void ApplyTo_CopiesEveryFormField()
    {
        var target = new AppSettings();

        SettingsFormRules.ApplyTo(target, Values(), DefaultFolder);

        Assert.True(target.RunAtLogin);
        Assert.True(target.CheckUpdateOnStart);
        Assert.False(target.WheelAdjustsPenSize);
        Assert.True(target.SyncToolStyles);
        Assert.False(target.BoardAllMonitors);
        Assert.True(target.DefaultBoardIsBlack);
        Assert.True(target.HighlightCursor);
        Assert.Equal(@"C:\\Custom", target.SaveFolder);
        Assert.Equal([@"\\.\DISPLAY2"], target.DisabledMonitors);
        // 기본값(true)과 다른 값을 넣어 복사를 증명한다 (73단계 실험적 기능).
        Assert.False(target.ZBandPolling);
    }

    /// <summary>실험적 기능 체크박스(73단계): 켜고 끄는 두 방향 모두 설정에 그대로 적힌다.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ApplyTo_ZBandPolling_CopiesCheckboxValue(bool before, bool checkbox)
    {
        var target = new AppSettings { ZBandPolling = before };

        SettingsFormRules.ApplyTo(target, Values(zBandPolling: checkbox), DefaultFolder);

        Assert.Equal(checkbox, target.ZBandPolling);
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

    // ── 확인 버튼이 창을 열어 두는가 (78단계, A6-1) ──
    // 예전에는 창이 '교정 알림 라벨이 보이는가'로 닫기를 판정했는데, 그 라벨을 접는 코드가 없어서 한 번 교정된 뒤로는
    // 확인 버튼이 영영 창을 닫지 못했다. 판정은 이제 순수 결과값 KeepsWindowOpen 하나이고, 아래가 그 증인이다.

    private static readonly IReadOnlyList<(string, bool)> AllUnchecked =
        [(@"\\.\DISPLAY1", false), (@"\\.\DISPLAY2", false), (@"\\.\DISPLAY3", false)];

    /// <summary>판서 화면을 전부 해제하면 첫 화면을 되살렸다고 알려야 하므로 창을 열어 둔다.</summary>
    [Fact]
    public void KeepsWindowOpen_AllMonitorsUnchecked_True()
    {
        var result = SettingsFormRules.ApplyTo(new AppSettings(), Values(monitors: AllUnchecked), DefaultFolder);

        Assert.True(result.KeepsWindowOpen);
        Assert.Equal(@"\\.\DISPLAY1", result.RestoredDeviceName);
    }

    [Fact]
    public void KeepsWindowOpen_OneChecked_False()
    {
        var result = SettingsFormRules.ApplyTo(
            new AppSettings(), Values(monitors: [(@"\\.\DISPLAY1", false), (@"\\.\DISPLAY2", true), (@"\\.\DISPLAY3", false)]), DefaultFolder);

        Assert.False(result.KeepsWindowOpen);
    }

    [Fact]
    public void KeepsWindowOpen_NoMonitors_False()
    {
        var result = SettingsFormRules.ApplyTo(new AppSettings(), Values(monitors: []), DefaultFolder);

        Assert.False(result.KeepsWindowOpen);
    }

    /// <summary>
    /// 창이 실제로 겪는 순서의 순수 증인(회귀): 1회차 확인은 모두 해제 → 교정 → 창 유지, 창은 되살린 체크박스를 다시 켜 두므로
    /// 2회차 확인은 DISPLAY1이 켜진 채 들어와 교정이 없다 → 창이 닫혀야 한다. 결함 시절에는 1회차에 띄운 알림 라벨이
    /// 남아 있어 2회차에도 창이 열려 있었다.
    /// </summary>
    [Fact]
    public void ApplyTo_SecondApplyWithRestoredChecked_DoesNotKeepOpen()
    {
        var target = new AppSettings();

        var first = SettingsFormRules.ApplyTo(target, Values(monitors: AllUnchecked), DefaultFolder);
        var second = SettingsFormRules.ApplyTo(
            target, Values(monitors: [(@"\\.\DISPLAY1", true), (@"\\.\DISPLAY2", false), (@"\\.\DISPLAY3", false)]), DefaultFolder);

        Assert.True(first.KeepsWindowOpen);
        Assert.False(second.KeepsWindowOpen);
        Assert.Equal([@"\\.\DISPLAY2", @"\\.\DISPLAY3"], target.DisabledMonitors);
    }

    /// <summary>
    /// 창이 알림을 띄우는 조건(교정됨 + 되살린 장치 이름 있음)과 한 글자도 다르지 않다 — 이름 없는 교정은 띄울 알림이 없으니
    /// 창을 열어 둘 이유도 없다. 규칙은 이 조합을 만들지 않지만, 두 조건이 어긋나면 '알림 없이 안 닫히는 창'이 된다.
    /// </summary>
    [Theory]
    [InlineData(true, @"\\.\DISPLAY1", true)]
    [InlineData(true, null, false)]
    [InlineData(false, @"\\.\DISPLAY1", false)]
    [InlineData(false, null, false)]
    public void KeepsWindowOpen_RequiresCoercionAndDeviceName(bool coerced, string? device, bool expected)
    {
        var result = new SettingsApplyResult(coerced, device);

        Assert.Equal(expected, result.KeepsWindowOpen);
    }
}
