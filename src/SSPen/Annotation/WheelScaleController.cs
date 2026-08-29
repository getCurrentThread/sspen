using System.Windows;

namespace SSPen.Annotation;

/// <summary>
/// 휠 유휴 디바운스 이음매 (R7). 구현은 <c>ContentSurfaceWindow.DispatcherIdleScheduler</c> 하나뿐이고,
/// 헤드리스 테스트는 가짜를 넣어 450ms를 기다리지 않고 유휴 만료를 흉내낸다.
///
/// 콜백을 매 호출마다 받는 <c>Restart(TimeSpan, Action)</c> 꼴을 쓰지 않는다 — 구독이 누적되어
/// 한 번의 유휴 만료가 세션을 여러 번 확정한다. 구독은 <see cref="Tick"/> 이벤트로 분리하고
/// 소비자가 <c>-=</c> 뒤 <c>+=</c>로 멱등하게 다시 건다.
/// </summary>
public interface IIdleScheduler
{
    /// <summary>디바운스 재시작: 이미 대기 중이면 멈췄다가 처음부터 다시 센다.</summary>
    void Restart(TimeSpan interval);

    /// <summary>대기 취소. 이미 멈춰 있으면 무동작이다.</summary>
    void Cancel();

    /// <summary>유휴 시간이 만료됐다.</summary>
    event Action? Tick;
}

/// <summary>
/// 휠 확대/축소 (R7): 연속 노치를 하나의 원장 항목으로 묶는다.
///
/// 세션(<see cref="WheelScaleSession"/>) · 시작 상태 스냅샷 · <b>잡은 요소 리스트</b>를 한 덩어리로
/// 소유한다 — 셋 중 하나라도 밖에 두면 유휴 확정 시점에 서로 어긋난다.
/// 시계와 유휴 스케줄러를 주입받아 헤드리스로 검증된다 (<c>FadeSchedulerCore</c>와 같은 분리 관례).
/// </summary>
/// <param name="ownerLookup">요소의 소유 문서 조회 (다중 모니터 선택: 델타마다 자기 소유자를 든다).</param>
/// <param name="fallbackDocument">소유자를 못 찾은 요소가 떨어질 문서.</param>
/// <param name="applyTransformState">
/// 상태 대입 + 소유 문서 알림. <b>반드시</b> 컨트롤러가 가진 <b>단 하나의</b>
/// <see cref="DragBaseStates"/> 인스턴스의 <c>Apply</c> 메서드 그룹을 넘긴다 —
/// 여기서 요소의 <c>TransformState</c>에 직접 대입하는 코드를 쓰면 R15 집행 지점이 둘이 된다.
/// </param>
/// <param name="commitTransform">원장 커밋 (f3/SEL-12: 세션 전체가 항목 1개).</param>
/// <param name="now">주입 시계 — 450ms 유휴 판정이 전부 이 값에서 나온다 (R7).</param>
/// <param name="idle">유휴 디바운스 이음매 (R7).</param>
public sealed class WheelScaleController(
    Func<AnnotationElement, AnnotationDocument?> ownerLookup,
    AnnotationDocument fallbackDocument,
    Action<AnnotationElement, ElementTransformState> applyTransformState,
    Action<IReadOnlyList<TransformDelta>, Point?> commitTransform,
    Func<DateTime> now,
    IIdleScheduler idle)
{
    private readonly WheelScaleSession _session = new();
    private Dictionary<long, ElementTransformState>? _baseStates;

    /// <summary>
    /// 휠 세션이 잡은 요소들. <b>선택집합을 다시 조회하지 않는 이유</b>: 휠 확정은 유휴 타이머로
    /// 비동기 발생하므로, 그 사이 ESC나 클릭 통과 전환으로 선택이 비면 요소를 되찾지 못해
    /// <b>화면에는 커진 채 원장에는 없는</b> 변형이 남아 실행취소로 지울 수 없게 된다.
    /// </summary>
    private List<AnnotationElement>? _elements;

    /// <summary>세션 진행 중인가.</summary>
    public bool Active => _session.Active;

    /// <summary>
    /// 휠 노치 1회. 첫 노치에서 시작 상태와 고정점을 <b>동결</b>하고, 이후 노치는 그 시작 상태에
    /// 누적 배율을 곱한다 (드래그와 같은 "직전 프레임 누적 금지" 규약 — 누적하면 부동소수 오차가 쌓인다).
    ///
    /// <paramref name="dragActive"/>가 참이면 아무 일도 하지 않는다 (R7). 호출부에도 조기 반환이 있지만
    /// 여기서 한 번 더 막는 이유: 두 세션이 같은 요소를 동시에 잡으면 시작 상태 스냅샷이 둘로 갈라져,
    /// 마우스 업이 항목 1을 싣고 450ms 뒤 유휴 타이머가 항목 2를 더 실어 한 번의 드래그가
    /// 실행취소 2번이 된다 (그중 하나는 아무 일도 하지 않는 유령 스텝).
    /// </summary>
    public void Step(IReadOnlyList<AnnotationElement> owned, Point cursor, int notches, bool dragActive)
    {
        if (dragActive)
        {
            return;
        }
        if (!_session.Active)
        {
            // 각도 없는 축 정렬 프레임이다 (SEL-LIM-6). 각도가 실린 제스처 프레임으로 바꾸면
            // 각도가 고정점 계산과 배율 경로로 새어 나가고, 그리는 각도와 잉크의 각도가
            // 다시 갈라질 수 있게 된다.
            if (SelectionGroup.Frame(owned) is not { } frame)
            {
                return;
            }
            _baseStates = [];
            _elements = [.. owned];
            foreach (var element in owned)
            {
                _baseStates[element.Id] = element.TransformState;
            }
            _session.Begin(SelectionGestureRules.WheelPivot(frame, cursor), now());
        }
        if (_baseStates is not { } baseStates || _elements is not { } elements)
        {
            return;
        }

        double raw = _session.Step(notches, now());
        double factor = TransformMath.ClampGroupFactor(raw, baseStates.Values);
        _session.SetFactor(factor); // 한계 밖 누적을 지워 천장에서 첫 역방향 노치부터 반응하게 한다 (R7).
        foreach (var element in elements)
        {
            if (baseStates.TryGetValue(element.Id, out var start))
            {
                applyTransformState(
                    element, TransformMath.ScaleAbout(start, element.LocalBounds, _session.Pivot, factor));
            }
        }

        // `-=` 뒤 `+=`는 중복이 아니라 멱등 재구독이다 — 노치마다 구독이 쌓이면 유휴 만료 한 번이
        // 세션을 여러 번 확정한다.
        idle.Tick -= OnIdle;
        idle.Tick += OnIdle;
        idle.Restart(WheelScaleSession.IdleTimeout);
    }

    /// <summary>
    /// 휠 세션 마감. <paramref name="commit"/>이면 <b>원장 1항목</b>으로 싣는다 (f3/SEL-12).
    /// 이관 판정은 건너뛴다 — 휠은 요소를 어디에도 "놓지" 않았고, 커서가 옆 모니터 위에 있다는
    /// 이유로 선택 전체가 이관되면 사용자 의도와 정반대다 (f7/SEL-14: 그래서 놓은 지점이 null이고
    /// 이 메서드에는 <c>Point</c> 인자가 아예 없다).
    /// </summary>
    public void Flush(bool commit)
    {
        idle.Cancel();
        if (!_session.Active)
        {
            _baseStates = null;
            _elements = null;
            return;
        }
        if (commit && _baseStates is { } baseStates && _elements is { } elements)
        {
            var pairs = new List<(AnnotationElement, ElementTransformState)>();
            foreach (var element in elements)
            {
                if (baseStates.TryGetValue(element.Id, out var before))
                {
                    pairs.Add((element, before));
                }
            }
            var deltas = TransformCommitPlan.Build(pairs, ownerLookup, fallbackDocument);
            if (deltas.Count > 0)
            {
                commitTransform(deltas, null);
            }
        }
        _session.End();
        _baseStates = null;
        _elements = null;
    }

    /// <summary>
    /// 유휴 만료. <see cref="WheelScaleSession.DueToCommit"/> 재확인이 남아 있어야 한 박자 늦게
    /// 도착한 틱이 무해하다 — 그 사이 새 노치가 들어왔으면 확정하지 않고 다음 만료를 기다린다 (R7).
    /// </summary>
    private void OnIdle()
    {
        if (_session.DueToCommit(now()))
        {
            Flush(commit: true);
        }
    }
}
