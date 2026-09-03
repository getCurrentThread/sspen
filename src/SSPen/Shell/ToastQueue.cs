namespace SSPen.Shell;

/// <summary>토스트 심각도. 값이 클수록 우선한다 — 선점 판정이 이 순서에 기댄다.</summary>
public enum ToastKind
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>
/// 토스트 한 건의 요청. <c>ActionLabel</c>이 있으면 클릭 가능한 토스트가 된다 (예: "폴더 열기").
/// <c>OnAction</c>은 그 라벨을 눌렀을 때의 동작으로, <b>병합 판정에는 들어가지 않는다</b> —
/// 같은 문구는 같은 알림이며 어느 쪽 델리게이트가 남든 동작이 같기 때문이다.
///
/// <c>Transient</c>는 <b>현재 상태를 읽어 주는</b> 알림이다(상태 리드아웃). 사건 보고와 달리 시간이 지나면
/// 값이 아니라 <b>거짓</b>이 되므로 절대 줄을 서지 않는다 — 휠을 열 번 굴리면 열 번째 상태 하나만 옳다.
/// </summary>
public readonly record struct ToastRequest(
    ToastKind Kind,
    string Text,
    string? ActionLabel = null,
    Action? OnAction = null,
    bool Transient = false);

/// <summary>
/// 한 틱의 표시 상태. <c>StopTimer</c>는 큐가 비어 타이머를 스스로 내려도 된다는 신호다
/// (<see cref="FlyoutWatchRules"/>·<c>RenderTickController</c>와 같은 규율 — 어댑터가 폴링을 계속 돌리지 않는다).
/// </summary>
public readonly record struct ToastStep(
    bool Visible,
    string Text,
    ToastKind Kind,
    string? ActionLabel,
    bool Interactive,
    bool StopTimer,
    Action? OnAction = null);

/// <summary>
/// 토스트 대기열 판정 (WI-11/AC-19 알림 계층의 순수 코어). 창·타이머·디스패처를 모르고 시계만 주입받는다 —
/// 어댑터(<c>ToastHost</c>)는 <see cref="Push"/>와 <see cref="Tick"/>을 호출하고 결과를 창에 바르는 일만 한다.
///
/// 정책 세 가지:
/// <list type="bullet">
///   <item><b>지속 시간</b>은 종류가 정한다 — 정보 2.6초, 경고 4.5초, 오류 6초. 읽는 데 드는 시간이 다르기 때문이다.</item>
///   <item><b>선점</b>: 더 심각한 알림은 기다리지 않는다. 표시 중이던 낮은 등급은 버린다 —
///     오류가 뜬 뒤에 "복사했습니다"가 뒤늦게 나오면 사용자가 실패를 성공으로 읽는다.</item>
///   <item><b>병합</b>: 같은 <c>(Kind, Text)</c>가 다시 오면 줄을 세우지 않고 <b>시간만 연장</b>한다.
///     연타(예: 저장 실패 반복)로 같은 문구가 큐에 쌓여 몇 초씩 밀리는 것을 막는다.</item>
/// </list>
/// </summary>
public sealed class ToastQueue(Func<DateTime> now)
{
    /// <summary>종류별 표시 시간. 바꾸려면 <c>ToastQueueTests</c>의 고정 값도 함께 바꾼다.</summary>
    public static TimeSpan DwellFor(ToastKind kind) => kind switch
    {
        ToastKind.Error => TimeSpan.FromSeconds(6.0),
        ToastKind.Warning => TimeSpan.FromSeconds(4.5),
        _ => TimeSpan.FromSeconds(2.6),
    };

    private readonly List<ToastRequest> _pending = [];
    private ToastRequest? _current;
    private DateTime _deadline;

    /// <summary>표시 중인 토스트가 있거나 대기열이 남아 있으면 참 — 어댑터가 타이머를 켤 계기다.</summary>
    public bool HasWork => _current is not null || _pending.Count > 0;

    public void Push(ToastRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return; // 빈 문구는 표시할 것이 없다 — 빈 창이 깜빡이느니 아무 일도 하지 않는다.
        }

        var at = now();
        if (request.Transient)
        {
            // 상태 리드아웃: 대기열에 넣지 않는다. 더 심각한 알림이 떠 있으면 아예 버린다 —
            // 오류를 밀어내고 "펜 · 굵기 3/5"가 뜨면 사용자는 실패를 못 본다. 같은 등급이면 즉시 갈아 끼운다.
            // 액션이 달린 토스트("폴더 열기")도 밀어내지 않는다 — 누를 기회를 뺏는 것이기 때문이다.
            if (_current is { } shown && (shown.Kind > request.Kind || shown.ActionLabel is not null))
            {
                return;
            }
            Show(request, at);
            return;
        }
        if (_current is { } current)
        {
            if (SameMessage(current, request))
            {
                _deadline = at + DwellFor(request.Kind); // 병합: 줄을 세우지 않고 시간만 연장한다.
                return;
            }
            if (request.Kind > current.Kind)
            {
                Show(request, at); // 선점: 낮은 등급은 버린다 (위 문서 참조).
                return;
            }
        }
        else
        {
            Show(request, at);
            return;
        }

        if (!_pending.Any(p => SameMessage(p, request)))
        {
            _pending.Add(request);
        }
    }

    public ToastStep Tick(DateTime at)
    {
        if (_current is { } current && at < _deadline)
        {
            return StepFor(current, stopTimer: false);
        }

        _current = null;
        if (_pending.Count > 0)
        {
            var next = _pending[0];
            _pending.RemoveAt(0);
            Show(next, at);
            return StepFor(next, stopTimer: false);
        }

        return new ToastStep(
            Visible: false, Text: string.Empty, Kind: ToastKind.Info,
            ActionLabel: null, Interactive: false, StopTimer: true);
    }

    /// <summary>표시 중인 것과 대기열을 모두 버린다 (캡처 세션 진입처럼 화면을 비워야 하는 자리).</summary>
    public void Clear()
    {
        _current = null;
        _pending.Clear();
    }

    private void Show(ToastRequest request, DateTime at)
    {
        _current = request;
        _deadline = at + DwellFor(request.Kind);
    }

    private static ToastStep StepFor(ToastRequest request, bool stopTimer) => new(
        Visible: true,
        Text: request.Text,
        Kind: request.Kind,
        ActionLabel: request.ActionLabel,
        Interactive: !string.IsNullOrEmpty(request.ActionLabel),
        StopTimer: stopTimer,
        OnAction: request.OnAction);

    private static bool SameMessage(ToastRequest a, ToastRequest b) =>
        a.Kind == b.Kind && string.Equals(a.Text, b.Text, StringComparison.Ordinal);
}
