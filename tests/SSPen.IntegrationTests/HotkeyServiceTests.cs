using System.Runtime.InteropServices;
using SSPen.Shell;
using Xunit;

namespace SSPen.IntegrationTests;

/// <summary>
/// WI-4 / 프리모템 3: 전역 핫키 등록 — 전체 맵 성공, 사전 점유 충돌 시 부분 실패 보고, 재등록.
/// 머신 바운드: 다른 앱(Epic Pen 등)이 Alt+Shift 조합을 점유 중이면 결과가 달라질 수 있다.
/// </summary>
public class HotkeyServiceTests
{
    // 실사용 맵과 겹치지 않는 시험용 조합 (Ctrl+Alt+Shift+F13~).
    private const uint TestMods = 0x0001 | 0x0002 | 0x0004; // ALT|CONTROL|SHIFT
    private const uint VkF13 = 0x7C;
    private const uint VkF14 = 0x7D;
    private const uint VkF15 = 0x7E;

    [Fact]
    public void RegisterAll_CleanCombos_AllSucceed() => StaRunner.Run(() =>
    {
        using var service = new HotkeyService();
        service.SetBindings(
        [
            new HotkeyBinding("시험1", TestMods, VkF13, () => { }),
            new HotkeyBinding("시험2", TestMods, VkF14, () => { }),
        ]);
        Assert.Empty(service.FailedBindings);
    });

    [Fact]
    public void RegisterAll_PreRegisteredCombo_ReportsPartialFailure() => StaRunner.Run(() =>
    {
        // 다른 소유자가 조합을 선점한 상황 시뮬레이션 (Epic Pen 동시 실행 시나리오).
        using var blocker = new HotkeyService();
        blocker.SetBindings([new HotkeyBinding("선점", TestMods, VkF15, () => { })]);
        Assert.Empty(blocker.FailedBindings);

        using var service = new HotkeyService();
        service.SetBindings(
        [
            new HotkeyBinding("성공해야 함", TestMods, VkF13, () => { }),
            new HotkeyBinding("충돌해야 함", TestMods, VkF15, () => { }),
        ]);

        Assert.Equal(new[] { "충돌해야 함" }, service.FailedBindings);
    });

    [Fact]
    public void Reregister_AfterBlockerReleased_Succeeds() => StaRunner.Run(() =>
    {
        var blocker = new HotkeyService();
        blocker.SetBindings([new HotkeyBinding("선점", TestMods, VkF15, () => { })]);

        using var service = new HotkeyService();
        service.SetBindings([new HotkeyBinding("재시도 대상", TestMods, VkF15, () => { })]);
        Assert.Single(service.FailedBindings);

        // 선점 해제 후 재등록 (AC-23 재지정/트레이 재시도 경로).
        blocker.Dispose();
        service.RegisterAll();
        Assert.Empty(service.FailedBindings);
    });

    [Fact]
    public void SuppressRestore_TogglesRegistration() => StaRunner.Run(() =>
    {
        using var service = new HotkeyService();
        service.SetBindings([new HotkeyBinding("억제 대상", TestMods, VkF13, () => { })]);
        Assert.Empty(service.FailedBindings);

        // 억제 중에는 같은 조합을 다른 소유자가 가져갈 수 있어야 한다 (ARCH-8).
        service.Suppress();
        using var taker = new HotkeyService();
        taker.SetBindings([new HotkeyBinding("인수자", TestMods, VkF13, () => { })]);
        Assert.Empty(taker.FailedBindings);

        // 복원 시도: 인수자가 아직 점유 중이므로 실패 목록에 잡힌다.
        service.Restore();
        Assert.Single(service.FailedBindings);

        taker.Dispose();
        service.RegisterAll();
        Assert.Empty(service.FailedBindings);
    });
}
