using SSPen.Diagnostics;
using SSPen.Interop;

namespace SSPen.Shell;

/// <summary>
/// z-밴드 사후 검증의 조립 (54단계 L3를 72단계 A8-1·A1-1에서 합성 루트 밖으로 꺼냈다). 정책(코얼레싱·백오프·<see cref="ZBandVerifyPolicy.Wakes"/>)은
/// <see cref="ZBandVerifyPolicy"/>가, 순서 판정은 <see cref="ZOrderInvariant"/>가 가진다. 이 클래스가 소유하는 것은 둘을 잇는 규칙이다:
/// (a) 검증은 토스트를 <b>빼고</b> 복구는 토스트를 <b>넣는다</b> — 토스트는 클릭 통과 창이라 설정창이 그 위로 올라도 입력에 영향이 없다.
/// (b) 닫힌 창의 낡은 HWND는 검사 전에 거른다. (c) 검증의 복구(<see cref="Repair"/>)는 백오프를 풀지 않는다 — 정규 재적용
/// <see cref="Apply"/>만 푼다. (d) <see cref="Stop"/> 뒤에는 요청을 받지 않고, 이미 큐에 든 검증은 pending만 풀고 끝난다 —
/// 폴러 경로(<see cref="IsOrdered"/>·<see cref="Repair"/>)도 밴드를 적용하지 않는다 (92단계).
/// (e) WinEvent 두 훅(REORDER, FOREGROUND)의 소유 — 생성은 OS를 건드리지 않고, <see cref="Install"/>이 둘 다 시도한다 (70단계).
///
/// <b>언제</b> 적용하고 검증을 깨우는가(AppState.Changed·핀·캡처·설정·툴바 전이·ZBandRequested·툴바 ZOrderChanged)는 합성 루트가
/// 소유한다 — 이 클래스는 호출 지점을 늘리거나 줄이지 않는다. 렌더 틱에서 부르는 것은 AGENTS L14 위반이다.
/// OS 경계는 전부 주입이다(밴드 목록·적용·IsWindow·아래 이웃·데스크톱·Background 게시·<see cref="IWinEventInstaller"/>) —
/// 헤드리스 증인은 <c>ZBandVerifierTests</c>(FakeZOrder·FakeWinEventInstaller).
/// </summary>
public sealed class ZBandVerifier : IDisposable
{
    private readonly Func<bool, IReadOnlyList<nint>> _bandOrder;
    private readonly Action<IReadOnlyList<nint>> _applyBand;
    private readonly Func<nint, bool> _isWindow;
    private readonly Func<nint, nint> _below;
    private readonly Func<nint> _desktop;
    private readonly Action<Action> _postBackground;
    private readonly ZBandVerifyPolicy _policy = new();
    private readonly WinEventWatch _reorderWatch;
    private readonly WinEventWatch _foregroundWatch;
    private bool _stopped;

    /// <param name="bandOrder">위→아래 밴드 HWND 목록. 인자는 includeToast — 호출 시점에 평가되는 지연 조회여야 한다.</param>
    /// <param name="applyBand">밴드 적용(<c>WindowStyling.ApplyZBand</c>).</param>
    /// <param name="isWindow">살아 있는 창인가(<c>WindowStyling.IsWindow</c>) — 낡은 HWND 필터.</param>
    /// <param name="below">z-순서상 바로 아래 창(<c>WindowStyling.Below</c>) — <see cref="ZOrderInvariant.IsOrdered"/>에 주입한다.</param>
    /// <param name="desktop">데스크톱 창 HWND(<c>GetDesktopWindow</c>) — REORDER 필터의 기준. 이벤트마다 읽는다.</param>
    /// <param name="postBackground">검증을 디스패처에 미룬다. 프로덕션은 <b>반드시</b> <c>DispatcherPriority.Background</c>다 (AGENTS L14).</param>
    /// <param name="winEvents">WinEvent 설치기 — 프로덕션 <c>WinEventWatch.Native</c>, 테스트 <c>FakeWinEventInstaller</c>.</param>
    public ZBandVerifier(
        Func<bool, IReadOnlyList<nint>> bandOrder,
        Action<IReadOnlyList<nint>> applyBand,
        Func<nint, bool> isWindow,
        Func<nint, nint> below,
        Func<nint> desktop,
        Action<Action> postBackground,
        IWinEventInstaller winEvents)
    {
        _bandOrder = bandOrder;
        _applyBand = applyBand;
        _isWindow = isWindow;
        _below = below;
        _desktop = desktop;
        _postBackground = postBackground;
        _reorderWatch = new WinEventWatch(
            NativeMethods.EVENT_OBJECT_REORDER, NativeMethods.EVENT_OBJECT_REORDER, OnWinEvent, winEvents);
        _foregroundWatch = new WinEventWatch(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND, OnWinEvent, winEvents);
    }

    /// <summary>연속 복구 한도를 넘겨 정규 재적용(<see cref="Apply"/>)까지 검증을 쉬는 중인가.</summary>
    public bool Suspended => _policy.Suspended;

    /// <summary>
    /// 검증을 깨우는 WinEvent 두 훅을 REORDER → FOREGROUND 순서로 <b>둘 다</b> 설치 시도한다
    /// (<see cref="ZBandVerifyPolicy.InstallWatches"/>, 70단계 A9-8). 실패하면 어느 훅이 실패했는지 경고 로그를 남긴다 —
    /// 요청·결과 단계 훅은 그대로 살아 있으므로 진단만 한다. 둘 다 설치되면 true.
    /// </summary>
    public bool Install()
    {
        if (ZBandVerifyPolicy.InstallWatches(_reorderWatch.Install, _foregroundWatch.Install) is { } failure)
        {
            Log.Warn(failure);
            return false;
        }
        return true;
    }

    /// <summary>
    /// 정규 재적용(상태 변경·핀·캡처·설정·툴바 전이·ZBandRequested): 백오프를 풀고 토스트를 포함한 밴드 전체를 적용한다.
    /// 검증의 복구 경로는 이것이 아니라 <see cref="Repair"/>다.
    /// </summary>
    public void Apply()
    {
        _policy.Reset();
        _applyBand(_bandOrder(true));
    }

    /// <summary>검증을 큐에 넣는다 — 멈췄거나, 이미 걸려 있거나, 백오프 중이면 무동작 (코얼레싱은 <see cref="ZBandVerifyPolicy"/>).</summary>
    public void RequestVerify()
    {
        if (_stopped || !_policy.OnEvent())
        {
            return;
        }
        _postBackground(Verify);
    }

    /// <summary>
    /// 실제 z-순서가 밴드 순서와 같은가 — 토스트를 뺀 목록에서 낡은 HWND를 거른 뒤 <see cref="ZOrderInvariant.IsOrdered"/>로 판정한다.
    /// 정책 상태(pending·연속 복구 카운터)는 건드리지 않는다 — 73단계 <see cref="ZBandPoller"/>가 주기 검사에 이것을 쓴다.
    /// <see cref="Stop"/> 뒤에는 z-순서를 읽지 않고 참(정렬됨)을 돌려준다 — 정지 뒤에 한 번 더 도는 폴러 틱이 정정으로 가지 않게 (92단계).
    /// </summary>
    public bool IsOrdered()
    {
        if (_stopped)
        {
            return true;
        }
        var order = _bandOrder(false).Where(_isWindow).ToList();
        return ZOrderInvariant.IsOrdered(order, _below);
    }

    /// <summary>
    /// 백오프를 건드리지 않는 적용 — 토스트를 포함한 밴드 전체를 다시 적용하되 <see cref="ZBandVerifyPolicy.Reset"/>은 부르지 않는다.
    /// 검증의 복구 경로가 이것이다(<see cref="Apply"/>를 부르면 연속 복구 카운터가 매번 지워져 백오프가 영영 걸리지 않는다).
    /// 73단계 <see cref="ZBandPoller"/>의 정정도 이것이다 — 백오프를 우회하되 풀지 않는다(사용자 결정).
    /// <see cref="Stop"/> 뒤에는 무동작이다 — 창 닫기 뒤에 폴러 틱이 한 번 더 돌아도 파괴 중인 창에 SetWindowPos가 걸리지 않게
    /// (54단계 L5와 같은 위험, 92단계). 증인: <c>ZBandVerifierTests.IsOrderedAndRepair_AfterStop_ReadNothingAndApplyNothing</c>.
    /// </summary>
    public void Repair()
    {
        if (_stopped)
        {
            return;
        }
        _applyBand(_bandOrder(true));
    }

    /// <summary>
    /// 종료: WinEvent 두 훅을 해제하고 이후의 검증 요청을 무시한다(예전 <c>AppController._shuttingDown</c>의 의미).
    /// 이미 디스패처 큐에 든 검증은 돌 때 pending만 풀고 아무것도 적용하지 않는다 — 파괴 중인 창에 SetWindowPos가 걸리지 않게 (54단계 L5).
    /// 폴러가 쓰는 <see cref="IsOrdered"/>·<see cref="Repair"/>도 이 뒤로는 z-순서를 읽거나 밴드를 적용하지 않는다 (92단계).
    /// </summary>
    public void Stop()
    {
        _stopped = true;
        _reorderWatch.Dispose();
        _foregroundWatch.Dispose();
    }

    public void Dispose() => Stop();

    /// <summary>
    /// WinEvent 콜백 (54단계 L3). 어떤 이벤트가 검증을 깨우는지는 <see cref="ZBandVerifyPolicy.Wakes"/>가 소유한다.
    /// 콜백은 설치 스레드(UI)의 펌프에서 오지만 짧아야 하므로 검증은 디스패처로 미룬다.
    /// </summary>
    private void OnWinEvent(uint eventType, nint hwnd, int idObject)
    {
        if (ZBandVerifyPolicy.Wakes(eventType, hwnd, _desktop()))
        {
            RequestVerify();
        }
    }

    /// <summary>
    /// 실제 z-순서를 읽어 밴드 순서와 다를 때만 재적용한다 (54단계 L3). 토스트는 클릭 통과 창이라 검사에서 뺀다 —
    /// 설정창처럼 활성화되는 창이 토스트 위로 오르는 것은 입력에 아무 영향이 없다. 닫힌 창의 낡은 HWND도 뺀다.
    /// 렌더 틱이 아니라 z-순서 <b>사건</b>에만 반응하므로 AGENTS의 "틱에서 밴드 재적용 금지"와 충돌하지 않는다.
    /// </summary>
    private void Verify()
    {
        if (_stopped)
        {
            _policy.OnVerified(ordered: true);
            return;
        }
        if (!_policy.OnVerified(IsOrdered()))
        {
            return;
        }
        Log.Info("z-밴드 순서가 어긋나 있어 다시 적용했다 (사후 검증).");
        // Apply()가 아니라 Repair()다 — Apply는 정규 재적용이라 백오프 카운터를 지운다.
        // 증인: ZBandVerifierTests.Verify_RepairPath_DoesNotResetBackoff_ThirdFutileRepairSuspends.
        Repair();
        if (_policy.Suspended)
        {
            Log.Warn($"z-밴드 복구가 연속 {ZBandVerifyPolicy.MaxConsecutiveRepairs}회 소용없어 다음 정규 재적용까지 검증을 쉰다.");
        }
    }
}
