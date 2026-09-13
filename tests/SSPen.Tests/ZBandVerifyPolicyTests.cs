using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="ZBandVerifyPolicy"/> (54단계 L3). 잠그는 것: 이벤트 코얼레싱(큐에 검증 하나), 정렬됨은 카운터 리셋,
/// 어긋남은 복구 허용을 연속 <see cref="ZBandVerifyPolicy.MaxConsecutiveRepairs"/>회까지만, 한도에 닿으면 이벤트를 받지 않고,
/// 정규 재적용(<see cref="ZBandVerifyPolicy.Reset"/>)이 백오프를 푼다.
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
}
