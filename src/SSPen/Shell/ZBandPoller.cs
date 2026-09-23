using SSPen.Annotation;

namespace SSPen.Shell;

/// <summary>
/// 실험적 z-순서 주기 정정 (73단계, 사용자 요청 2026-09-23). 설정 "실험적 기능"의 체크박스(<c>AppSettings.ZBandPolling</c>, 기본 켜짐)로 켜고 끈다.
/// 고정 <see cref="Interval"/>(2초)마다 <see cref="ZBandVerifier.IsOrdered"/>와 같은 검사를 돌려, 어긋나 있으면
/// <see cref="ZBandVerifier.Repair"/>로 밴드를 다시 적용한다 — 이벤트 계층(요청·결과·사후 검증)이 놓친 뒤집힘의 안전망이다.
///
/// 사용자 결정: 주기는 고정 2초, 폴링은 <see cref="ZBandVerifyPolicy"/>의 백오프와 <b>무관하게</b> 정정하되 백오프를 <b>풀지도 않는다</b> —
/// 그래서 이 클래스는 정책을 참조하지 않고, 정정 경로는 <c>Reset</c>을 부르는 <c>Apply</c>가 아니라 <c>Repair</c>다.
/// AGENTS L14의 "이벤트 구동만" 규칙에 대한 유일한 시간 기반 예외다. 타이머는 Background 우선순위 <see cref="IIdleScheduler"/>
/// (프로덕션 <see cref="DispatcherIdleScheduler"/>)이고, <c>CompositionTarget</c> 렌더 틱에서 밴드를 만지는 것은 여전히 금지다.
///
/// 계약: (a) <see cref="SetEnabled"/>는 멱등 — 같은 값이면 무동작, 켤 때 구독은 <c>-=</c> 뒤 <c>+=</c>로 한 번만.
/// (b) 틱은 <b>먼저 재무장</b>한다 — 검사·정정이 던져도 폴링이 멈추지 않는다. (c) <c>blocked</c>(캡처 세션)이면 검사를 건너뛴다.
/// (d) 로그는 <b>전이에서만</b> 남긴다 — 첫 정정 1줄, 회복 시 "N회 정정 뒤 회복" 1줄. 2초마다 로그가 쌓이지 않게.
/// (e) <see cref="Dispose"/> 뒤의 <c>SetEnabled(true)</c>는 무시한다(종료 중 설정 적용이 타이머를 되살리지 않게).
/// 헤드리스 증인은 <c>ZBandPollerTests</c>(FakeIdleScheduler, 실제 <see cref="ZBandVerifier"/>와 묶은 백오프 증인 포함).
/// </summary>
public sealed class ZBandPoller : IDisposable
{
    /// <summary>검사 주기 — 사용자 결정으로 고정이다(설정은 켜고 끄는 체크박스뿐).</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly IIdleScheduler _timer;
    private readonly Func<bool> _blocked;
    private readonly Func<bool> _isOrdered;
    private readonly Action _repair;
    private readonly Action<string> _log;
    private int _consecutiveRepairs;
    private bool _disposed;

    /// <param name="timer">재무장형 주기 타이머 — 프로덕션 <see cref="DispatcherIdleScheduler"/>(Background 우선순위), 테스트 가짜.</param>
    /// <param name="blocked">이번 틱의 검사를 건너뛸까(캡처 세션 활성). 건너뛰어도 재무장은 한다.</param>
    /// <param name="isOrdered">실제 z-순서가 밴드 순서와 같은가 (<see cref="ZBandVerifier.IsOrdered"/>).</param>
    /// <param name="repair">백오프를 건드리지 않는 재적용 (<see cref="ZBandVerifier.Repair"/>).</param>
    /// <param name="log">전이 로그 (<c>Log.Info</c>).</param>
    public ZBandPoller(IIdleScheduler timer, Func<bool> blocked, Func<bool> isOrdered, Action repair, Action<string> log)
    {
        _timer = timer;
        _blocked = blocked;
        _isOrdered = isOrdered;
        _repair = repair;
        _log = log;
    }

    /// <summary>주기 정정이 켜져 있는가.</summary>
    public bool Enabled { get; private set; }

    /// <summary>
    /// 켜고 끈다 — 멱등. 켤 때: 틱 구독(<c>-=</c> 뒤 <c>+=</c>, 한 번만) → <see cref="Interval"/>로 무장 → 로그.
    /// 끌 때: 대기 취소 → 구독 해제 → 연속 정정 카운터 초기화 → 로그. <see cref="Dispose"/> 뒤의 켜기는 무시한다.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        if (enabled == Enabled || (enabled && _disposed))
        {
            return;
        }
        Enabled = enabled;
        if (enabled)
        {
            _timer.Tick -= OnTick;
            _timer.Tick += OnTick;
            _timer.Restart(Interval);
            _log($"z-순서 주기 정정 켜짐 ({Interval.TotalSeconds:0.#}초)");
            return;
        }
        _timer.Cancel();
        _timer.Tick -= OnTick;
        _consecutiveRepairs = 0;
        _log("z-순서 주기 정정 꺼짐");
    }

    /// <summary>끄고, 이후의 켜기를 무시한다 (종료 경로 — <c>AppController.Shutdown</c>이 창을 닫기 전에 부른다).</summary>
    public void Dispose()
    {
        SetEnabled(false);
        _disposed = true;
    }

    /// <summary>
    /// 한 번의 주기 검사. 순서가 계약이다: 꺼져 있으면 무동작(같은 틱 발화 중에 꺼진 경우) → 먼저 재무장 → blocked면 검사 생략 →
    /// 정렬돼 있으면 회복 전이만 기록 → 어긋나 있으면 정정하고 첫 정정만 기록.
    /// </summary>
    private void OnTick()
    {
        if (!Enabled)
        {
            return;
        }
        _timer.Restart(Interval);
        if (_blocked())
        {
            return;
        }
        if (_isOrdered())
        {
            if (_consecutiveRepairs > 0)
            {
                _log($"z-순서 주기 정정: {_consecutiveRepairs}회 정정 뒤 회복했다.");
                _consecutiveRepairs = 0;
            }
            return;
        }
        _repair();
        _consecutiveRepairs++;
        if (_consecutiveRepairs == 1)
        {
            _log("z-순서가 어긋나 정정했다 (주기 검사).");
        }
    }
}
