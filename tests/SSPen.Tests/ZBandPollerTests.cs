using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="ZBandPoller"/> (73단계, 실험적 z-순서 주기 정정)의 헤드리스 증인. 타이머는 <see cref="FakeIdleScheduler"/>라
/// 2초를 기다리지 않고 <see cref="FakeIdleScheduler.Fire"/>로 틱을 일으킨다. MTA, WPF 불필요.
/// 잠그는 것: 켜면 <see cref="ZBandPoller.Interval"/>로 무장, 멱등 구독, 끄면 취소·구독 해제·틱 무동작, 정렬 시 무정정,
/// 어긋남마다 틱당 정정 1회, blocked면 검사 없이 재무장만, 재무장이 검사보다 먼저, 로그는 전이에서만, Dispose 뒤 켜기 무시,
/// 그리고 실제 <see cref="ZBandVerifier"/>와 묶어 "백오프로 쉬는 중에도 정정하되 백오프는 풀지 않는다"(사용자 결정).
/// </summary>
public class ZBandPollerTests
{
    private sealed class Rig
    {
        public FakeIdleScheduler Timer { get; } = new();

        public bool Blocked { get; set; }

        public bool Ordered { get; set; } = true;

        public int BlockedCalls { get; private set; }

        public int IsOrderedCalls { get; private set; }

        public int Repairs { get; private set; }

        public List<string> Logs { get; } = [];

        /// <summary>isOrdered가 불린 순간의 재무장 횟수 — "먼저 재무장" 순서 증인.</summary>
        public int RestartsSeenByCheck { get; private set; } = -1;

        public ZBandPoller Poller { get; }

        public Rig()
        {
            Poller = new ZBandPoller(
                Timer,
                blocked: () =>
                {
                    BlockedCalls++;
                    return Blocked;
                },
                isOrdered: () =>
                {
                    IsOrderedCalls++;
                    RestartsSeenByCheck = Timer.RestartCount;
                    return Ordered;
                },
                repair: () => Repairs++,
                log: Logs.Add);
        }
    }

    [Fact]
    public void Interval_IsFixedTwoSeconds() =>
        Assert.Equal(TimeSpan.FromSeconds(2), ZBandPoller.Interval);

    [Fact]
    public void Constructor_StartsDisabled_TouchesNothing()
    {
        var rig = new Rig();

        Assert.False(rig.Poller.Enabled);
        Assert.Equal(0, rig.Timer.RestartCount);
        Assert.Equal(0, rig.Timer.SubscriberCount);
        Assert.Empty(rig.Logs);
    }

    [Fact]
    public void SetEnabled_True_ArmsWithInterval_SubscribesOnce_LogsOn()
    {
        var rig = new Rig();

        rig.Poller.SetEnabled(true);

        Assert.True(rig.Poller.Enabled);
        Assert.Equal(1, rig.Timer.RestartCount);
        Assert.Equal(ZBandPoller.Interval, rig.Timer.LastInterval);
        Assert.Equal(1, rig.Timer.SubscriberCount);
        Assert.Equal(["z-순서 주기 정정 켜짐 (2초)"], rig.Logs);
    }

    [Fact]
    public void SetEnabled_TrueTwice_IsIdempotent_OneSubscriptionOneArmOneLog()
    {
        var rig = new Rig();

        rig.Poller.SetEnabled(true);
        rig.Poller.SetEnabled(true);

        Assert.Equal(1, rig.Timer.SubscriberCount);
        Assert.Equal(1, rig.Timer.RestartCount);
        Assert.Single(rig.Logs);
    }

    [Fact]
    public void SetEnabled_OnOffOn_KeepsOneSubscription()
    {
        var rig = new Rig();

        rig.Poller.SetEnabled(true);
        rig.Poller.SetEnabled(false);
        rig.Poller.SetEnabled(true);

        Assert.Equal(1, rig.Timer.SubscriberCount);
        rig.Timer.Fire();
        Assert.Equal(1, rig.IsOrderedCalls);
    }

    [Fact]
    public void SetEnabled_FalseWhileDisabled_IsNoOp()
    {
        var rig = new Rig();

        rig.Poller.SetEnabled(false);

        Assert.Equal(0, rig.Timer.CancelCount);
        Assert.Empty(rig.Logs);
    }

    [Fact]
    public void SetEnabled_False_CancelsUnsubscribes_AndTicksDoNothing()
    {
        var rig = new Rig { Ordered = false };
        rig.Poller.SetEnabled(true);

        rig.Poller.SetEnabled(false);
        rig.Timer.Fire();

        Assert.False(rig.Poller.Enabled);
        Assert.Equal(1, rig.Timer.CancelCount);
        Assert.Equal(0, rig.Timer.SubscriberCount);
        Assert.Equal(0, rig.IsOrderedCalls);
        Assert.Equal(0, rig.Repairs);
        Assert.Equal("z-순서 주기 정정 꺼짐", rig.Logs[^1]);
    }

    [Fact]
    public void Tick_DisabledDuringSameDispatch_IsNoOp_NoRearm()
    {
        // 같은 틱 발화 안에서 앞선 구독자가 끈 경우 — 이벤트는 발화 시점의 구독 목록을 돌므로 OnTick이 그래도 불린다.
        // 그때는 재무장도 검사도 하지 않아야 한다(끈 뒤 타이머가 되살아나면 안 된다).
        var rig = new Rig { Ordered = false };
        rig.Timer.Tick += () => rig.Poller.SetEnabled(false);
        rig.Poller.SetEnabled(true);
        int restartsBefore = rig.Timer.RestartCount;

        rig.Timer.Fire();

        Assert.Equal(restartsBefore, rig.Timer.RestartCount);
        Assert.Equal(0, rig.IsOrderedCalls);
        Assert.Equal(0, rig.Repairs);
    }

    [Fact]
    public void Tick_Ordered_RepairsNothing_AndRearms()
    {
        var rig = new Rig { Ordered = true };
        rig.Poller.SetEnabled(true);

        rig.Timer.Fire();
        rig.Timer.Fire();

        Assert.Equal(2, rig.IsOrderedCalls);
        Assert.Equal(0, rig.Repairs);
        Assert.Equal(3, rig.Timer.RestartCount);
        Assert.Equal(ZBandPoller.Interval, rig.Timer.LastInterval);
    }

    [Fact]
    public void Tick_Disordered_RepairsOncePerTick()
    {
        var rig = new Rig { Ordered = false };
        rig.Poller.SetEnabled(true);

        rig.Timer.Fire();
        rig.Timer.Fire();
        rig.Timer.Fire();

        Assert.Equal(3, rig.Repairs);
    }

    [Fact]
    public void Tick_Blocked_SkipsCheckAndRepair_ButRearms()
    {
        var rig = new Rig { Blocked = true, Ordered = false };
        rig.Poller.SetEnabled(true);

        rig.Timer.Fire();

        Assert.Equal(1, rig.BlockedCalls);
        Assert.Equal(0, rig.IsOrderedCalls);
        Assert.Equal(0, rig.Repairs);
        Assert.Equal(2, rig.Timer.RestartCount);
    }

    [Fact]
    public void Tick_RearmsBeforeChecking()
    {
        // 재무장이 먼저다 — 검사·정정이 던져도 다음 틱이 온다.
        var rig = new Rig();
        rig.Poller.SetEnabled(true);

        rig.Timer.Fire();

        Assert.Equal(2, rig.RestartsSeenByCheck);
    }

    [Fact]
    public void Tick_Logs_OnlyOnTransitions()
    {
        var rig = new Rig { Ordered = false };
        rig.Poller.SetEnabled(true);

        rig.Timer.Fire();
        rig.Timer.Fire();
        rig.Timer.Fire();
        rig.Ordered = true;
        rig.Timer.Fire();
        rig.Timer.Fire();

        Assert.Equal(
            [
                "z-순서 주기 정정 켜짐 (2초)",
                "z-순서가 어긋나 정정했다 (주기 검사).",
                "z-순서 주기 정정: 3회 정정 뒤 회복했다.",
            ],
            rig.Logs);
    }

    [Fact]
    public void Tick_BlockedBetweenRepairs_DoesNotBreakTheRun()
    {
        // 캡처 중 건너뛴 틱은 전이가 아니다 — 연속 정정은 이어지고 회복 로그는 한 번이다.
        var rig = new Rig { Ordered = false };
        rig.Poller.SetEnabled(true);

        rig.Timer.Fire();
        rig.Blocked = true;
        rig.Timer.Fire();
        rig.Blocked = false;
        rig.Timer.Fire();
        rig.Ordered = true;
        rig.Timer.Fire();

        Assert.Equal(2, rig.Repairs);
        Assert.Equal("z-순서 주기 정정: 2회 정정 뒤 회복했다.", rig.Logs[^1]);
        Assert.Single(rig.Logs, l => l.StartsWith("z-순서가 어긋나", StringComparison.Ordinal));
    }

    [Fact]
    public void SetEnabled_False_ResetsRepairRun_NoStaleRecoveryLogAfterReenable()
    {
        var rig = new Rig { Ordered = false };
        rig.Poller.SetEnabled(true);
        rig.Timer.Fire();

        rig.Poller.SetEnabled(false);
        rig.Ordered = true;
        rig.Poller.SetEnabled(true);
        rig.Timer.Fire();

        Assert.DoesNotContain(rig.Logs, l => l.Contains("회복", StringComparison.Ordinal));
    }

    [Fact]
    public void Dispose_Disables_ThenSetEnabledTrue_IsIgnored()
    {
        var rig = new Rig();
        rig.Poller.SetEnabled(true);

        rig.Poller.Dispose();
        rig.Poller.SetEnabled(true);
        rig.Timer.Fire();

        Assert.False(rig.Poller.Enabled);
        Assert.Equal(0, rig.Timer.SubscriberCount);
        Assert.Equal(1, rig.Timer.RestartCount);
        Assert.Equal(1, rig.Timer.CancelCount);
        Assert.Equal(0, rig.IsOrderedCalls);
    }

    [Fact]
    public void Dispose_Twice_IsSafe()
    {
        var rig = new Rig();
        rig.Poller.SetEnabled(true);

        rig.Poller.Dispose();
        rig.Poller.Dispose();

        Assert.Equal(1, rig.Timer.CancelCount);
        Assert.Single(rig.Logs, l => l == "z-순서 주기 정정 꺼짐");
    }

    // ---- 실제 ZBandVerifier와 묶은 증인 (사용자 결정: 백오프를 우회하되 풀지도 않는다) ----

    private const nint Toast = 0x10;
    private const nint Toolbar = 0x40;
    private const nint Pin = 0x50;
    private const nint Surface = 0x60;

    [Fact]
    public void Tick_VerifierSuspended_StillRepairs_AndBackoffStaysSuspended()
    {
        var zOrder = new FakeZOrder(Toast, Surface, Toolbar, Pin); // 서피스가 툴바 위 — "보이는데 안 눌리는 툴바"
        var applied = new List<IReadOnlyList<nint>>();
        var posted = new Queue<Action>();
        var winEvents = new FakeWinEventInstaller();
        using var verifier = new ZBandVerifier(
            bandOrder: includeToast => ZBandOrder.Build(includeToast ? Toast : 0, 0, 0, Toolbar, [Pin], [Surface]),
            applyBand: order => applied.Add([.. order]), // 헛정정 — 외부 앱이 계속 뒤집는 상황
            isWindow: hwnd => hwnd != 0,
            below: zOrder.Below,
            desktop: () => 0x9000,
            postBackground: posted.Enqueue,
            winEvents: winEvents);
        for (int i = 0; i < ZBandVerifyPolicy.MaxConsecutiveRepairs; i++)
        {
            verifier.RequestVerify();
            while (posted.TryDequeue(out var verify))
            {
                verify();
            }
        }
        Assert.True(verifier.Suspended);
        int appliedBefore = applied.Count;

        var timer = new FakeIdleScheduler();
        using var poller = new ZBandPoller(timer, blocked: () => false, verifier.IsOrdered, verifier.Repair, log: _ => { });
        poller.SetEnabled(true);
        timer.Fire();
        timer.Fire();

        Assert.Equal(appliedBefore + 2, applied.Count);
        Assert.Equal([Toast, Toolbar, Pin, Surface], applied[^1]);
        Assert.True(verifier.Suspended);
        verifier.RequestVerify();
        Assert.Empty(posted);
    }
}
