using SSPen.Settings;
using Xunit;

namespace SSPen.E2ETests;

public class SettingsAndPersistenceE2ETests
{
    [Fact]
    public void Settings_ApplyGeneralSettings_UpdatesStateAndSettingsBinder() => E2EAppFixture.Run(actor =>
    {
        var settingsBinder = actor.App.SettingsBinder;
        var original = settingsBinder.Settings;

        // 일반 설정 변경
        var updated = new AppSettings
        {
            PenColor = "#FFFF0000",
            PenThickness = 3,
            DefaultBoardIsBlack = true,
            FadingSeconds = 3.5,
            WheelAdjustsPenSize = true,
        };

        actor.App.ApplyGeneralSettings(updated);

        Assert.Equal(3.5, actor.App.FadingSeconds);
        Assert.Equal(SSPen.Annotation.BoardMode.Black, actor.State.DefaultBoard);
        Assert.True(actor.State.WheelAdjustsPenSize);
    });

    [Fact]
    public void ApplyGeneralSettings_RoutesRunAtLoginThroughSeam_LeavesRegistryUntouched()
    {
        // A8-2: 픽스처가 주입한 이음매가 Start와 ApplyGeneralSettings 두 호출을 모두 받아야 한다.
        // 기본값(RunAtLogin=false)이 실제 RunAtLogin.Apply로 새면 HKCU Run의 "SS Pen" 값이 지워진다 —
        // 설치본에서 로그인 시 시작을 켜 둔 개발자 PC라면 before=true가 false로 바뀌어 여기서 잡힌다.
        bool before = RunAtLogin.IsEnabled();
        IReadOnlyList<bool> calls = [];

        E2EAppFixture.Run(actor =>
        {
            actor.App.ApplyGeneralSettings(new AppSettings { CheckUpdateOnStart = false });
            actor.Pump();
            calls = [.. actor.RunAtLoginCalls];
        });

        Assert.Equal([false, false], calls);
        Assert.Equal(before, RunAtLogin.IsEnabled());
    }

    [Fact]
    public void OpenSettingsWindow_CreatesAndActivatesWindow() => E2EAppFixture.Run(actor =>
    {
        actor.OpenSettings();
        Assert.NotNull(actor.App.CurrentSettingsWindow);
        Assert.True(actor.App.CurrentSettingsWindow.IsVisible);
        Assert.True(actor.App.CurrentSettingsWindow.Topmost);

        // 창 닫기
        actor.App.CurrentSettingsWindow.Close();
        actor.Pump();
        Assert.Null(actor.App.CurrentSettingsWindow);
    });

    [Fact]
    public void Settings_ToggleDisabledMonitors_DynamicallyAddsAndRemovesSurfaces() => E2EAppFixture.Run(actor =>
    {
        // 3개 가상 모니터 환경에서 초기 서피스 3개 확인
        Assert.Equal(3, actor.App.Surfaces.Count);

        // 1. 우측 모니터(\\.\DISPLAY2) 판서 비활성화 설정 적용
        var updated = new AppSettings
        {
            DisabledMonitors = [@"\\.\DISPLAY2"],
        };
        actor.App.ApplyGeneralSettings(updated);
        actor.Pump();

        // 서피스가 2개로 축소되고 DISPLAY2 서피스가 닫혔는지 확인
        Assert.Equal(2, actor.App.Surfaces.Count);
        Assert.DoesNotContain(actor.App.Surfaces, s => s.Monitor.DeviceName == @"\\.\DISPLAY2");

        // 닫힌 서피스가 있는 상태에서 도구 변경 및 상태 변경이 예외(InvalidOperationException) 없이 동작하는지 확인
        actor.SelectTool(SSPen.Annotation.ToolKind.Pen);
        actor.SelectTool(SSPen.Annotation.ToolKind.Highlighter);
        actor.Pump();

        // 2. 다시 모든 모니터 활성화 설정 적용
        var reenabled = new AppSettings
        {
            DisabledMonitors = [],
        };
        actor.App.ApplyGeneralSettings(reenabled);
        actor.Pump();

        // 서피스가 3개로 다시 복원되었는지 확인
        Assert.Equal(3, actor.App.Surfaces.Count);
        Assert.Contains(actor.App.Surfaces, s => s.Monitor.DeviceName == @"\\.\DISPLAY2");
    });
}
