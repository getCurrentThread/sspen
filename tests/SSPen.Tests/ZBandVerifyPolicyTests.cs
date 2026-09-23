using SSPen.Interop;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="ZBandVerifyPolicy"/> (54단계 L3). 잠그는 것: 이벤트 코얼레싱(큐에 검증 하나), 정렬됨은 카운터 리셋,
/// 어긋남은 복구 허용을 연속 <see cref="ZBandVerifyPolicy.MaxConsecutiveRepairs"/>회까지만, 한도에 닿으면 이벤트를 받지 않고,
/// 정규 재적용(<see cref="ZBandVerifyPolicy.Reset"/>)이 백오프를 푼다. 그리고 두 WinEvent 훅 설치(70단계, A9-8):
/// 한쪽이 실패해도 다른 쪽은 시도되고, 로그 문구가 어느 쪽이 실패했는지 말한다.
/// </summary>
public class ZBandVerifyPolicyTests
{
    [Fact]
    public void OnEvent_FirstEvent_QueuesVerification()
    {
        var p = new ZBandVerifyPolicy();

        Assert.True(p.OnEvent());
        Assert.True(p.Pending);
    }

    [Fact]
    public void OnEvent_WhilePending_DoesNotQueueAgain()
    {
        var p = new ZBandVerifyPolicy();
        p.OnEvent();

        Assert.False(p.OnEvent());
        Assert.False(p.OnEvent());
    }

    [Fact]
    public void OnVerified_Ordered_ClearsPending_AndDeniesRepair()
    {
        var p = new ZBandVerifyPolicy();
        p.OnEvent();

        Assert.False(p.OnVerified(ordered: true));
        Assert.False(p.Pending);
        Assert.True(p.OnEvent()); // 다음 이벤트는 다시 큐에 들어간다
    }

    [Fact]
    public void OnVerified_Disordered_AllowsRepair_UpToLimit_ThenSuspends()
    {
        var p = new ZBandVerifyPolicy();

        for (int i = 0; i < ZBandVerifyPolicy.MaxConsecutiveRepairs; i++)
        {
            Assert.True(p.OnEvent());
            Assert.True(p.OnVerified(ordered: false));
        }

        Assert.True(p.Suspended);
        Assert.False(p.OnEvent()); // 백오프: 복구 → 재정렬 이벤트 → 검증 → 복구 루프를 끊는다
    }

    [Fact]
    public void OnVerified_OrderedBetweenRepairs_ResetsConsecutiveCount()
    {
        var p = new ZBandVerifyPolicy();
        p.OnEvent();
        p.OnVerified(ordered: false);
        p.OnEvent();
        p.OnVerified(ordered: false);

        p.OnEvent();
        p.OnVerified(ordered: true);

        for (int i = 0; i < ZBandVerifyPolicy.MaxConsecutiveRepairs; i++)
        {
            Assert.True(p.OnEvent());
            Assert.True(p.OnVerified(ordered: false));
        }
        Assert.True(p.Suspended);
    }

    [Fact]
    public void Reset_LiftsSuspension()
    {
        var p = new ZBandVerifyPolicy();
        for (int i = 0; i < ZBandVerifyPolicy.MaxConsecutiveRepairs; i++)
        {
            p.OnEvent();
            p.OnVerified(ordered: false);
        }
        Assert.True(p.Suspended);

        p.Reset();

        Assert.False(p.Suspended);
        Assert.True(p.OnEvent());
    }

    [Fact]
    public void OnVerified_WhileSuspended_DeniesRepair_AndClearsPending()
    {
        // 한도에 닿은 직후 이미 큐에 있던 검증이 돌 수 있다 — 복구는 거부하되 pending은 풀어야 Reset 뒤 이벤트가 다시 들어온다.
        var p = new ZBandVerifyPolicy();
        for (int i = 0; i < ZBandVerifyPolicy.MaxConsecutiveRepairs; i++)
        {
            p.OnEvent();
            p.OnVerified(ordered: false);
        }

        Assert.False(p.OnVerified(ordered: false));
        Assert.False(p.Pending);
        p.Reset();
        Assert.True(p.OnEvent());
    }

    // ---- 두 WinEvent 훅 설치 (70단계, A9-8): `||` 단락 평가로 REORDER 실패 시 FOREGROUND가 시도조차 안 되던 결함 ----

    private static (WinEventWatch Watch, FakeWinEventInstaller Fake) Watch(uint eventType, bool installSucceeds)
    {
        var fake = new FakeWinEventInstaller { NextHandle = installSucceeds ? 0x2000 : 0 };
        return (new WinEventWatch(eventType, eventType, (_, _, _) => { }, fake), fake);
    }

    [Fact]
    public void InstallWatches_ReorderFails_StillInstallsForeground_AndNamesReorder()
    {
        var reorder = Watch(NativeMethods.EVENT_OBJECT_REORDER, installSucceeds: false);
        var foreground = Watch(NativeMethods.EVENT_SYSTEM_FOREGROUND, installSucceeds: true);

        string? failure = ZBandVerifyPolicy.InstallWatches(reorder.Watch.Install, foreground.Watch.Install);

        Assert.Single(reorder.Fake.Installs);
        Assert.Single(foreground.Fake.Installs); // 첫 설치 실패가 두 번째 시도를 건너뛰게 하면 안 된다
        Assert.True(foreground.Watch.IsInstalled);
        Assert.False(reorder.Watch.IsInstalled);
        Assert.NotNull(failure);
        Assert.Contains("REORDER=실패", failure);
        Assert.Contains("FOREGROUND=설치됨", failure);
    }

    [Fact]
    public void InstallWatches_ForegroundFails_KeepsReorderInstalled_AndNamesForeground()
    {
        var reorder = Watch(NativeMethods.EVENT_OBJECT_REORDER, installSucceeds: true);
        var foreground = Watch(NativeMethods.EVENT_SYSTEM_FOREGROUND, installSucceeds: false);

        string? failure = ZBandVerifyPolicy.InstallWatches(reorder.Watch.Install, foreground.Watch.Install);

        Assert.True(reorder.Watch.IsInstalled); // 살아남은 계기는 되돌리지 않는다 — 검증을 하나라도 깨울 수 있어야 한다
        Assert.Empty(reorder.Fake.Uninstalls);
        Assert.False(foreground.Watch.IsInstalled);
        Assert.NotNull(failure);
        Assert.Contains("REORDER=설치됨", failure);
        Assert.Contains("FOREGROUND=실패", failure);
    }

    [Fact]
    public void InstallWatches_BothFail_AttemptsBoth_AndNamesBoth()
    {
        var reorder = Watch(NativeMethods.EVENT_OBJECT_REORDER, installSucceeds: false);
        var foreground = Watch(NativeMethods.EVENT_SYSTEM_FOREGROUND, installSucceeds: false);

        string? failure = ZBandVerifyPolicy.InstallWatches(reorder.Watch.Install, foreground.Watch.Install);

        Assert.Single(reorder.Fake.Installs);
        Assert.Single(foreground.Fake.Installs);
        Assert.NotNull(failure);
        Assert.Contains("REORDER=실패", failure);
        Assert.Contains("FOREGROUND=실패", failure);
    }

    [Fact]
    public void InstallWatches_BothSucceed_ReturnsNull_InReorderThenForegroundOrder()
    {
        // 정상 경로는 호출 순서까지 54단계 배선과 같다 — REORDER 다음 FOREGROUND.
        var calls = new List<string>();

        string? failure = ZBandVerifyPolicy.InstallWatches(
            () => { calls.Add("REORDER"); return true; },
            () => { calls.Add("FOREGROUND"); return true; });

        Assert.Null(failure);
        Assert.Equal(["REORDER", "FOREGROUND"], calls);
    }
}
