using System.IO;
using System.Windows.Threading;
using SSPen.Annotation;
using SSPen.Settings;
using SSPen.Shell;
using Xunit;
using static SSPen.Tests.StaThread;

namespace SSPen.Tests;

/// <summary>
/// <see cref="HotkeyRemapFlow"/>의 증인 (40단계, ARCH-8/AC-23). 억제 → 모달 → 복원의 순서와 '취소·예외에도 복원'을 잠근다.
/// 재지정 <b>쓰기</b>가 여기에 없다는 것도 계약이다: 예전에는 캡처 직후 곧바로 SaveNow까지 해서
/// 취소를 눌러도 단축키만 이미 디스크에 남는 비대칭이 있었다.
/// 79단계(A6-3)부터는 확인 시의 일괄 반영 <see cref="HotkeyRemapFlow.ApplyBatch"/>도 여기서 잠근다 — 전부 쓴 뒤 저장 1회·재등록 1회,
/// 그래서 맞바꾸기 도중의 중간 충돌(가짜 트레이 경고)이 생기지 않는다.
/// </summary>
public class HotkeyRemapFlowTests
{
    private const uint AltShift = 0x0001 | 0x0004;

    private static readonly HotkeyDef Captured = new(Modifiers: 0x0001, VirtualKey: 0x42);

    /// <summary>기본 표의 펜 = Alt+Shift+3, 지우개 = Alt+Shift+5 (ShellHotkeys 기본값).</summary>
    private static readonly HotkeyDef PenDefault = new(AltShift, 0x33);
    private static readonly HotkeyDef EraserDefault = new(AltShift, 0x35);

    /// <summary>맞바꾸기에 거쳐 가는 임시 조합 — 기본 표 어디에도 없는 Alt+Shift+K.</summary>
    private static readonly HotkeyDef Temp = new(AltShift, 0x4B);

    [Fact]
    public void Run_Captured_SuppressDialogRestore_InOrder()
    {
        var host = new FakeSettingsHost();

        var result = HotkeyRemapFlow.Run(host, () => { host.Calls.Add("Dialog"); return Captured; });

        Assert.Equal(Captured, result);
        Assert.Equal(["Suppress", "Dialog", "Restore"], host.Calls);
    }

    /// <summary>확정해도 설정을 쓰지 않는다 — 반영은 창이 확인 시점에 모아서 한다.</summary>
    [Fact]
    public void Run_Captured_DoesNotWriteSettings()
    {
        var host = new FakeSettingsHost();

        HotkeyRemapFlow.Run(host, () => Captured);

        Assert.DoesNotContain(host.Calls, call => call.StartsWith("Remap", StringComparison.Ordinal));
        Assert.Empty(host.Settings.Hotkeys);
    }

    [Fact]
    public void Run_Cancelled_ReturnsNull_ButRestores()
    {
        var host = new FakeSettingsHost();

        var result = HotkeyRemapFlow.Run(host, () => null);

        Assert.Null(result);
        Assert.Equal(["Suppress", "Restore"], host.Calls);
    }

    [Fact]
    public void Run_DialogThrows_StillRestores_AndPropagates()
    {
        var host = new FakeSettingsHost();

        Assert.Throws<InvalidOperationException>(() =>
            HotkeyRemapFlow.Run(host, () => throw new InvalidOperationException("boom")));

        Assert.Equal(["Suppress", "Restore"], host.Calls);
    }

    /// <summary>
    /// 회귀(79단계, A6-3): 펜과 지우개를 임시 조합을 거쳐 맞바꾸고(창의 충돌 검사가 받아들이는 경로) 확인하면, 재등록 어느 순간에도
    /// 등록 실패가 없다. 예전의 건별 적용은 첫 건(지우개 → 펜의 옛 조합) 직후 두 항목이 같은 조합이 되어 RegisterHotKey 하나가
    /// 실패했고, 루트는 그 실패 목록을 트레이 풍선에 잇는다 — 다음 건에서 바로 풀리는 가짜 경고였다.
    /// 실제 조합 표(<see cref="ShellHotkeys.BuildHotkeyMap"/>)와 등록 정책(<see cref="HotkeyService"/> + <see cref="FakeHotkeyRegistrar"/>)을 그대로 쓴다.
    /// </summary>
    [Fact]
    public void ApplyBatch_SwapViaTemp_NeverReportsRegistrationFailure() => RunSta(() =>
    {
        var settings = new AppSettings();
        var shell = CreateShellHotkeys(settings);
        var registrar = new FakeHotkeyRegistrar();
        using var service = new HotkeyService(registrar);
        service.SetBindings(shell.BuildHotkeyMap());
        var reported = new List<string[]>();
        service.RegistrationFailuresChanged += failed => reported.Add([.. failed]);
        var draft = new HotkeyDraft();
        foreach (var (id, def) in new[] { ("eraser", Temp), ("pen", EraserDefault), ("eraser", PenDefault) })
        {
            Assert.Null(draft.Conflict(shell.RemappableHotkeys, id, def, AppState.QuickColorCount));
            draft.Stage(id, def);
        }

        HotkeyRemapFlow.ApplyBatch(
            draft.Drain(), settings.Hotkeys, save: () => { }, rebind: () => service.SetBindings(shell.BuildHotkeyMap()));

        Assert.Empty(reported.SelectMany(failed => failed)); // 가짜 경고의 재료 — 어느 재등록에서도 실패 이름이 없다.
        Assert.Single(reported); // 재등록은 한 번이다.
        Assert.All(registrar.Registers, register => Assert.True(register.Ok));
        Assert.Equal(EraserDefault, settings.Hotkeys["pen"]);
        Assert.Equal(PenDefault, settings.Hotkeys["eraser"]);
    });

    /// <summary>
    /// 회귀(79단계, A6-3): 보류 N건은 전부 설정 사전에 쓴 뒤 저장 1회 → 재등록 1회다. 예전에는 건마다 디스크 저장과 전체 재등록이
    /// 따라왔다(N회씩). 저장·재등록이 볼 때 이미 모든 건이 써져 있다는 것도 함께 잠근다.
    /// </summary>
    [Fact]
    public void ApplyBatch_SeveralEntries_WritesAllThenSavesOnceAndRebindsOnce()
    {
        var hotkeys = new Dictionary<string, HotkeyDef>();
        var calls = new List<string>();

        HotkeyRemapFlow.ApplyBatch(
            [("pen", EraserDefault), ("eraser", PenDefault), ("text", Temp)],
            hotkeys,
            save: () => calls.Add($"save:{hotkeys.Count}"),
            rebind: () => calls.Add($"rebind:{hotkeys.Count}"));

        Assert.Equal(["save:3", "rebind:3"], calls);
    }

    /// <summary>보류분이 없으면 저장도 재등록도 없다 — 예전 창의 0회 루프와 같다.</summary>
    [Fact]
    public void ApplyBatch_Empty_DoesNotSaveOrRebind()
    {
        var hotkeys = new Dictionary<string, HotkeyDef>();
        var calls = new List<string>();

        HotkeyRemapFlow.ApplyBatch([], hotkeys, save: () => calls.Add("save"), rebind: () => calls.Add("rebind"));

        Assert.Empty(calls);
        Assert.Empty(hotkeys);
    }

    /// <summary>
    /// <see cref="HotkeyDraft.Drain"/>이 먼저 비워도 잃는 것이 없다는 증인 (79단계, A6-3 — 67단계 리뷰의 미결 사항).
    /// 저장이 던져도(디스크 IOException) 그 전에 모든 건이 설정 사전에 써져 있다 — 예전 건별 경로에서는 첫 건만 남고 나머지는
    /// 비워진 드래프트와 함께 사라졌다. 예외는 그대로 전파된다.
    /// </summary>
    [Fact]
    public void ApplyBatch_SaveThrows_EveryEntryAlreadyWritten_AndPropagates()
    {
        var hotkeys = new Dictionary<string, HotkeyDef>();

        Assert.Throws<IOException>(() => HotkeyRemapFlow.ApplyBatch(
            [("pen", EraserDefault), ("eraser", PenDefault)],
            hotkeys,
            save: () => throw new IOException("저장 실패 시험"),
            rebind: () => { }));

        Assert.Equal(
            [("eraser", PenDefault), ("pen", EraserDefault)],
            hotkeys.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (pair.Key, pair.Value)));
    }

    private static ShellHotkeys CreateShellHotkeys(AppSettings settings) =>
        new(
            Dispatcher.CurrentDispatcher,
            new AppState(),
            () => settings,
            undo: () => { },
            clearAll: () => { },
            startCapture: () => { },
            toggleToolbar: () => { },
            deleteSelection: () => { });
}
