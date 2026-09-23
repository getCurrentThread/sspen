using SSPen.Interop;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="ZBandVerifier"/> (72단계, A8-1·A1-1) — 54단계 L3 사후 검증 배선의 헤드리스 증인. MTA, WPF 불필요.
/// z-순서는 <see cref="FakeZOrder"/>, WinEvent는 범위별로 라우팅하는 <see cref="FakeWinEventInstaller"/>, Background 게시는 큐,
/// 밴드 적용은 기록형 가짜다. 밴드 목록은 실제 <see cref="ZBandOrder.Build"/>(71단계: 툴바 &gt; 핀 &gt; 서피스)로 만든다.
/// 잠그는 것: 정렬 시 무정정, 어긋남마다 토스트 포함 1회 정정, 코얼레싱, 검증의 복구는 Reset을 부르지 않아 3회 헛정정 뒤 쉰다
/// (구 AppController 781행 회귀), Apply가 백오프를 푼다, Wakes 필터, 토스트 제외, 낡은 HWND 필터, Stop 뒤 요청 무시·훅 해제,
/// Install이 한쪽 실패에도 다른 쪽을 시도, IsOrdered/Repair가 정책 상태를 바꾸지 않는다, Stop 뒤 IsOrdered/Repair는 읽지도 적용하지도 않는다(92단계).
/// </summary>
public class ZBandVerifierTests
{
    private const nint Toast = 0x10;
    private const nint Settings = 0x20;
    private const nint Toolbar = 0x40;
    private const nint Pin = 0x50;
    private const nint SurfaceA = 0x60;
    private const nint SurfaceB = 0x61;
    private const nint Desktop = 0x9000;
    private const nint Foreign = 0x7777;

    private sealed class Rig
    {
        public FakeZOrder ZOrder { get; } = new(Toast, Settings, Toolbar, Pin, SurfaceA, SurfaceB);

        public List<nint> Pins { get; } = [Pin];

        /// <summary>닫힌 창 — isWindow가 거짓을 돌려준다.</summary>
        public HashSet<nint> Closed { get; } = [];

        public List<IReadOnlyList<nint>> Applied { get; } = [];

        /// <summary>밴드 목록 조회 횟수 — z-순서를 읽거나 적용하려 했는지의 흔적이다.</summary>
        public int BandReads { get; private set; }

        public Queue<Action> Posted { get; } = new();

        public FakeWinEventInstaller WinEvents { get; } = new();

        /// <summary>적용이 실제 z-순서를 고치는가 — 거짓이면 헛정정(외부 앱이 계속 뒤집는 상황)이다.</summary>
        public bool ApplyFixesOrder { get; set; } = true;

        public ZBandVerifier Verifier { get; }

        public Rig()
        {
            WinEvents.NextHandles.Enqueue(0x2001); // REORDER
            WinEvents.NextHandles.Enqueue(0x2002); // FOREGROUND
            Verifier = new ZBandVerifier(
                bandOrder: includeToast =>
                {
                    BandReads++;
                    return ZBandOrder.Build(includeToast ? Toast : 0, Settings, 0, Toolbar, Pins, [SurfaceA, SurfaceB]);
                },
                applyBand: order =>
                {
                    Applied.Add([.. order]);
                    if (ApplyFixesOrder)
                    {
                        ZOrder.SetOrder(Toast, Settings, Toolbar, Pin, SurfaceA, SurfaceB);
                    }
                },
                isWindow: hwnd => hwnd != 0 && !Closed.Contains(hwnd),
                below: ZOrder.Below,
                desktop: () => Desktop,
                postBackground: Posted.Enqueue,
                winEvents: WinEvents);
        }

        /// <summary>서피스가 툴바 위로 올라선 상태 — 사용자 증상 "툴바가 보이는데 안 눌림".</summary>
        public void Disorder() => ZOrder.SetOrder(Toast, Settings, SurfaceA, Toolbar, Pin, SurfaceB);

        /// <summary>큐에 든 Background 검증을 전부 돌린다. 돌린 개수를 돌려준다.</summary>
        public int Drain()
        {
            int ran = 0;
            while (Posted.TryDequeue(out var action))
            {
                action();
                ran++;
            }
            return ran;
        }

        /// <summary>요청 → 드레인 한 바퀴.</summary>
        public void RequestAndDrain()
        {
            Verifier.RequestVerify();
            Drain();
        }
    }

    private static readonly nint[] FullBand = [Toast, Settings, Toolbar, Pin, SurfaceA, SurfaceB];

    [Fact]
    public void Constructor_DoesNotTouchOs_NoHooksInstalled()
    {
        var rig = new Rig();

        Assert.Empty(rig.WinEvents.Installs);
        Assert.Empty(rig.Posted);
        Assert.Empty(rig.Applied);
    }

    [Fact]
    public void Verify_Ordered_AppliesNothing()
    {
        var rig = new Rig();

        rig.RequestAndDrain();

        Assert.Empty(rig.Applied);
        Assert.False(rig.Verifier.Suspended);
    }

    [Fact]
    public void Verify_Disordered_RepairsOnce_WithToastIncluded()
    {
        var rig = new Rig();
        rig.Disorder();

        rig.RequestAndDrain();

        var applied = Assert.Single(rig.Applied);
        Assert.Equal(FullBand, applied); // 검사에서 뺀 토스트도 복구에는 들어간다
        Assert.True(rig.Verifier.IsOrdered());
    }

    [Fact]
    public void RequestVerify_Burst_PostsOnce()
    {
        var rig = new Rig();

        rig.Verifier.RequestVerify();
        rig.Verifier.RequestVerify();
        rig.Verifier.RequestVerify();

        Assert.Single(rig.Posted);
        Assert.Equal(1, rig.Drain());

        rig.Verifier.RequestVerify(); // 검증이 돈 뒤의 다음 이벤트는 다시 큐에 들어간다
        Assert.Single(rig.Posted);
    }

    /// <summary>
    /// 구 AppController 781행 회귀의 증인: 검증의 복구가 <see cref="ZBandVerifier.Apply"/>(Reset 포함)로 '정리'되면 연속 복구
    /// 카운터가 매번 지워져 백오프가 영영 걸리지 않는다 — 복구 → REORDER → 검증 → 복구 무한 루프가 Background로 CPU를 먹는다.
    /// </summary>
    [Fact]
    public void Verify_RepairPath_DoesNotResetBackoff_ThirdFutileRepairSuspends()
    {
        var rig = new Rig { ApplyFixesOrder = false };
        rig.Disorder();

        for (int i = 0; i < ZBandVerifyPolicy.MaxConsecutiveRepairs; i++)
        {
            Assert.False(rig.Verifier.Suspended);
            rig.RequestAndDrain();
        }

        Assert.Equal(ZBandVerifyPolicy.MaxConsecutiveRepairs, rig.Applied.Count);
        Assert.True(rig.Verifier.Suspended);
    }

    [Fact]
    public void Suspended_LaterRequestsAndWinEvents_PostNothing()
    {
        var rig = new Rig { ApplyFixesOrder = false };
        rig.Verifier.Install();
        rig.Disorder();
        for (int i = 0; i < ZBandVerifyPolicy.MaxConsecutiveRepairs; i++)
        {
            rig.RequestAndDrain();
        }

        rig.Verifier.RequestVerify();
        rig.WinEvents.Fire(NativeMethods.EVENT_OBJECT_REORDER, Desktop);
        rig.WinEvents.Fire(NativeMethods.EVENT_SYSTEM_FOREGROUND, Foreign);

        Assert.Empty(rig.Posted);
        Assert.Equal(ZBandVerifyPolicy.MaxConsecutiveRepairs, rig.Applied.Count);
    }

    [Fact]
    public void Apply_AfterSuspension_LiftsBackoff_AndNextRequestPosts()
    {
        var rig = new Rig { ApplyFixesOrder = false };
        rig.Disorder();
        for (int i = 0; i < ZBandVerifyPolicy.MaxConsecutiveRepairs; i++)
        {
            rig.RequestAndDrain();
        }
        Assert.True(rig.Verifier.Suspended);

        rig.Verifier.Apply();

        Assert.False(rig.Verifier.Suspended);
        Assert.Equal(FullBand, rig.Applied[^1]); // 정규 재적용도 토스트 포함 전체 밴드다
        rig.Verifier.RequestVerify();
        Assert.Single(rig.Posted);
    }

    [Theory]
    [InlineData(NativeMethods.EVENT_OBJECT_REORDER, 0x9000L, 1)]    // 데스크톱: 최상위 z-순서 변화 — 깨운다
    [InlineData(NativeMethods.EVENT_OBJECT_REORDER, 0x7777L, 0)]    // 남의 앱 자식 창 재정렬 — 거른다
    [InlineData(NativeMethods.EVENT_SYSTEM_FOREGROUND, 0x7777L, 1)] // 포그라운드 전환 — 언제나 깨운다
    public void WinEvent_WakesFilter_DecidesWhetherVerificationPosts(uint eventType, long hwnd, int expectedPosts)
    {
        // 특성 인수는 nint를 허용하지 않아 long으로 받는다 (0x9000 = Desktop, 0x7777 = Foreign).
        var rig = new Rig();
        rig.Verifier.Install();

        rig.WinEvents.Fire(eventType, (nint)hwnd);

        Assert.Equal(expectedPosts, rig.Posted.Count);
    }

    [Fact]
    public void Verify_OnlyToastOutOfOrder_IsNotDisorder_AppliesNothing()
    {
        // 설정창처럼 활성화되는 창이 클릭 통과 토스트 위로 오르는 것은 입력에 영향이 없다 — 검사에서 토스트를 뺀다.
        var rig = new Rig();
        rig.ZOrder.SetOrder(Settings, Toast, Toolbar, Pin, SurfaceA, SurfaceB);

        rig.RequestAndDrain();

        Assert.Empty(rig.Applied);
        Assert.True(rig.Verifier.IsOrdered());
    }

    [Fact]
    public void Verify_StaleHandleInBand_IsFiltered_AppliesNothing()
    {
        // 닫힌 핀의 낡은 HWND는 실제 z-순서에 없다 — 거르지 않으면 IsOrdered가 영영 거짓이라 헛정정만 쌓인다.
        const nint StalePin = 0x51;
        var rig = new Rig();
        rig.Pins.Add(StalePin);
        rig.Closed.Add(StalePin);

        rig.RequestAndDrain();

        Assert.Empty(rig.Applied);
        Assert.True(rig.Verifier.IsOrdered());
    }

    [Fact]
    public void Stop_ThenRequestAndWinEvents_PostNothing_AndBothHooksUninstalled()
    {
        var rig = new Rig();
        Assert.True(rig.Verifier.Install());

        rig.Verifier.Stop();
        rig.Disorder();
        rig.Verifier.RequestVerify();
        rig.WinEvents.Fire(NativeMethods.EVENT_OBJECT_REORDER, Desktop);
        rig.WinEvents.Fire(NativeMethods.EVENT_SYSTEM_FOREGROUND, Foreign);

        Assert.Empty(rig.Posted);
        Assert.Equal([(nint)0x2001, (nint)0x2002], rig.WinEvents.Uninstalls);
        Assert.Empty(rig.WinEvents.LiveRanges);
    }

    [Fact]
    public void Stop_WithQueuedVerify_DrainAppliesNothing()
    {
        // 종료 직전에 이미 큐에 든 검증: 파괴 중인 창에 SetWindowPos를 걸면 안 된다 (54단계 L5) — pending만 풀고 끝난다.
        var rig = new Rig();
        rig.Disorder();
        rig.Verifier.RequestVerify();

        rig.Verifier.Stop();
        Assert.Equal(1, rig.Drain());

        Assert.Empty(rig.Applied);
        Assert.False(rig.Verifier.Suspended);
    }

    [Fact]
    public void IsOrderedAndRepair_AfterStop_ReadNothingAndApplyNothing()
    {
        // 92단계: 창 닫기 뒤에 폴러 틱이 한 번 더 돌아도(54단계 L5와 같은 위험) 정지한 검증기는 z-순서를 읽지 않고
        // 정렬됨으로 답하며, Repair는 파괴 중인 창에 SetWindowPos를 걸지 않는다.
        var rig = new Rig();
        rig.Disorder();
        rig.Verifier.Stop();
        int readsAtStop = rig.BandReads;

        Assert.True(rig.Verifier.IsOrdered());
        rig.Verifier.Repair();

        Assert.Empty(rig.Applied);
        Assert.Equal(readsAtStop, rig.BandReads);
        Assert.False(rig.Verifier.Suspended);
    }

    [Fact]
    public void Install_ReorderFails_StillInstallsForeground_WhichStillWakes()
    {
        var rig = new Rig();
        rig.WinEvents.NextHandles.Clear();
        rig.WinEvents.NextHandles.Enqueue(0);      // REORDER 실패
        rig.WinEvents.NextHandles.Enqueue(0x2002); // FOREGROUND 성공

        Assert.False(rig.Verifier.Install());

        Assert.Equal(
            [(NativeMethods.EVENT_OBJECT_REORDER, NativeMethods.EVENT_OBJECT_REORDER),
             (NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND)],
            rig.WinEvents.Installs.Select(i => (i.Min, i.Max)));
        Assert.Equal([(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND)], rig.WinEvents.LiveRanges);

        rig.WinEvents.Fire(NativeMethods.EVENT_OBJECT_REORDER, Desktop); // 설치 안 된 훅 — 버려진다
        Assert.Empty(rig.Posted);
        rig.WinEvents.Fire(NativeMethods.EVENT_SYSTEM_FOREGROUND, Foreign); // 살아남은 계기는 여전히 깨운다
        Assert.Single(rig.Posted);
    }

    [Fact]
    public void Install_BothSucceed_ReturnsTrue_ReorderThenForeground()
    {
        var rig = new Rig();

        Assert.True(rig.Verifier.Install());

        Assert.Equal(
            [(NativeMethods.EVENT_OBJECT_REORDER, NativeMethods.EVENT_OBJECT_REORDER),
             (NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND)],
            rig.WinEvents.LiveRanges);
    }

    [Fact]
    public void IsOrderedAndRepair_WhileSuspended_LeaveBackoffUntouched()
    {
        // 73단계 폴러의 계약: 백오프를 우회하되 풀지도 않는다 — Repair는 Reset을 부르지 않는다.
        var rig = new Rig { ApplyFixesOrder = false };
        rig.Disorder();
        for (int i = 0; i < ZBandVerifyPolicy.MaxConsecutiveRepairs; i++)
        {
            rig.RequestAndDrain();
        }

        Assert.False(rig.Verifier.IsOrdered());
        rig.Verifier.Repair();

        Assert.True(rig.Verifier.Suspended);
        Assert.Equal(FullBand, rig.Applied[^1]);
        rig.Verifier.RequestVerify();
        Assert.Empty(rig.Posted);
    }

    [Fact]
    public void Repair_BetweenFutileVerifications_DoesNotRestartConsecutiveCount()
    {
        // 헛정정 2회 뒤 폴러식 Repair·IsOrdered가 끼어도 카운터는 이어진다 — 다음 헛정정 1회로 쉰다.
        var rig = new Rig { ApplyFixesOrder = false };
        rig.Disorder();
        rig.RequestAndDrain();
        rig.RequestAndDrain();

        rig.Verifier.IsOrdered();
        rig.Verifier.Repair();
        Assert.False(rig.Verifier.Suspended);
        rig.RequestAndDrain();

        Assert.True(rig.Verifier.Suspended);
    }
}
