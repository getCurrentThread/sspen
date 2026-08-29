namespace SSPen.Annotation;

/// <summary>
/// 드래그·휠 제스처가 끝날 때 원장에 실을 <see cref="TransformDelta"/> 목록을 만드는 순수 규칙.
///
/// <b>순회를 소유하지 않는다</b>: 이미 정렬된 (요소, 시작 상태) 쌍만 받는다. 호출부마다 요소를
/// 되찾는 의미론이 다르기 때문이다 — 드래그는 선택집합·핸들 대상에서 되찾고, 휠은 세션이 잡아 둔
/// held 리스트를 그대로 순회한다. 여기에 <c>Func&lt;long, AnnotationElement?&gt;</c> 같은 해석
/// 콜백을 두면 휠 확정이 선택집합을 경유하게 되어, 유휴 타이머가 도는 사이 선택이 비었을 때
/// <b>화면에는 커진 채 원장에는 없는</b> 변형이 남는 결함이 정확히 돌아온다 (R7).
/// </summary>
public static class TransformCommitPlan
{
    /// <summary>
    /// 실제로 바뀐 요소만 원장에 싣는다 — 제자리 클릭이 빈 undo 항목을 만들면 안 된다 (f3).
    /// 소유 문서는 요소마다 <paramref name="ownerLookup"/>으로 찾고, 못 찾으면 제스처가 벌어진
    /// 문서로 떨어진다 (다중 모니터 선택: 델타 하나하나가 자기 소유자를 든다).
    /// <see cref="TransformDelta.BeforeOwner"/>와 <see cref="TransformDelta.AfterOwner"/>를
    /// <b>같게</b> 둔다 — 이관 판정은 컴포지션 루트가 하고, 실제 소유권 이동은
    /// <see cref="SelectionTransfer.Execute"/>가 <c>After</c>/<c>AfterOwner</c>를 다시 써서 반영한다 (f7/SEL-14).
    /// </summary>
    public static List<TransformDelta> Build(
        IEnumerable<(AnnotationElement Element, ElementTransformState Before)> pairs,
        Func<AnnotationElement, AnnotationDocument?> ownerLookup,
        AnnotationDocument fallback)
    {
        var deltas = new List<TransformDelta>();
        foreach (var (element, before) in pairs)
        {
            if (before == element.TransformState)
            {
                continue;
            }
            var owner = ownerLookup(element) ?? fallback;
            deltas.Add(new TransformDelta(element, before, element.TransformState, owner, owner));
        }
        return deltas;
    }

    /// <summary>
    /// 놓은 지점을 컴포지션 루트에 넘기는 것은 <b>이동일 때뿐</b>이다: 크기/회전은 커서가 요소를
    /// 끌고 다닌 것이 아니라 핸들을 돌린 것이므로, 회전 핸들이 옆 모니터에 닿았다고 요소를
    /// 이관하면 안 된다 (f7/SEL-14).
    /// </summary>
    public static bool CarriesDropPoint(SelectionDragKind kind) => kind == SelectionDragKind.Move;
}

/// <summary>
/// 선택 드래그(이동·크기·회전·그룹)의 시작 상태 스냅샷이자 <c>TransformState</c> 대입의
/// <b>유일한</b> 집행 지점 (R15).
///
/// <b>id가 아니라 요소 참조를 함께 붙잡는 이유</b>: 롤백·커밋 시점에 선택집합을 다시 조회하면,
/// 그 사이 선택이 비어 버린 경로에서 요소를 되찾지 못해 <b>화면에는 변형된 채 원장에는 없는</b>
/// 변형이 남아 실행취소로 지울 수 없게 된다. 실제 경로: 드래그 도중 ESC →
/// <c>AppController.EngageClickThrough</c>가 선택을 비우고 <c>ClickThrough=true</c>로
/// <c>IsInteractive</c>를 떨어뜨리면 <c>ContentSurfaceWindow.ApplyState</c>가
/// <c>CancelActiveInput</c>을 부르는데, 그때 선택집합은 이미 비어 있다 (R5/SEL-B-4).
/// 이동·그룹 스케일·그룹 회전은 핸들 대상도 없으므로 id로는 아무것도 되찾지 못해
/// 롤백이 <b>통째로 무동작</b>이 된다. 휠 세션의 <c>_wheelElements</c>가 같은 실패를
/// 같은 방법으로 이미 막고 있었다 (R7) — 드래그 경로만 그 교훈을 받지 못했다.
///
/// 그래서 <see cref="RollbackAll"/>과 <see cref="Pairs"/>는 <b>인자를 받지 않는다</b>.
/// <c>SelectionModel</c>이나 핸들 대상을 다시 받는 오버로드를 추가하지 말 것 —
/// 위 결함이 그대로 돌아온다.
/// </summary>
public sealed class DragBaseStates(
    Func<AnnotationElement, AnnotationDocument?> ownerLookup,
    AnnotationDocument fallback)
{
    private List<AnnotationElement>? _elements;
    private Dictionary<long, ElementTransformState>? _baseStates;

    /// <summary>드래그 스냅샷이 살아 있는가 (제스처 진행 중).</summary>
    public bool Active => _baseStates is not null;

    /// <summary>
    /// 드래그 시작 상태 (id → 시작 변형). <b>"현재 상태" 접근자를 두지 않는다</b>:
    /// "매 프레임 드래그 시작 상태에서 재계산"(SEL-7) 규약을 <b>없는 API</b>로 표현한다 —
    /// 프레임 증분을 누적하려는 코드가 읽을 것이 애초에 없어야 한다.
    /// </summary>
    public IReadOnlyDictionary<long, ElementTransformState>? BaseStates => _baseStates;

    /// <summary>스냅샷이 붙잡은 요소 참조 (스냅샷 순서 보존).</summary>
    public IReadOnlyList<AnnotationElement> Elements => _elements ?? [];

    /// <summary>
    /// (요소, 시작 상태) 쌍을 스냅샷 순서대로 지연 열거한다 — 커밋(<see cref="TransformCommitPlan.Build"/>)과
    /// 롤백이 같은 순서를 본다. 선택집합을 재조회하지 않는다 (타입 요약 참조).
    /// </summary>
    public IEnumerable<(AnnotationElement Element, ElementTransformState Before)> Pairs
    {
        get
        {
            if (_elements is null || _baseStates is null)
            {
                yield break;
            }
            foreach (var element in _elements)
            {
                if (_baseStates.TryGetValue(element.Id, out var before))
                {
                    yield return (element, before);
                }
            }
        }
    }

    /// <summary>
    /// 드래그 시작 상태 스냅샷. 핸들 대상은 선택집합에 없을 수 없지만 방어적으로 함께 넣는다.
    /// 요소 참조도 <b>같이</b> 붙잡아 둔다 (이 타입 요약의 held 리스트 규약).
    /// 핸들 대상이 이미 선택집합에 있으면 쌍을 중복으로 만들지 않는다 — 중복 쌍은
    /// 같은 요소의 <see cref="TransformDelta"/>를 원장에 두 번 싣는다 (f3/SEL-12).
    /// </summary>
    public void Snapshot(SelectionModel selection, AnnotationElement? handleTarget)
    {
        _elements = [];
        _baseStates = [];
        foreach (var element in selection.Elements)
        {
            _elements.Add(element);
            _baseStates[element.Id] = element.TransformState;
        }
        if (handleTarget is { } target)
        {
            // 사전은 오늘과 같은 덮어쓰기, 리스트만 중복을 막는다.
            if (!_baseStates.ContainsKey(target.Id))
            {
                _elements.Add(target);
            }
            _baseStates[target.Id] = target.TransformState;
        }
    }

    /// <summary>
    /// 상태 대입 뒤에는 **반드시** 소유 문서의 알림이 따라와야 한다 (R15) — 그래야 시각물과 장식이 함께 움직인다.
    /// 다른 모니터 소속 요소도 그 요소의 소유 문서를 통해 알린다 (다중 선택 이동).
    /// </summary>
    public void Apply(AnnotationElement element, ElementTransformState next)
    {
        element.TransformState = next;
        (ownerLookup(element) ?? fallback).RaiseElementTransformChanged(element);
    }

    /// <summary>
    /// 진행 중이던 변형을 시작 상태로 롤백한다 — 원장에 없는 중간 변형이 화면에 남으면 실행취소로 지울 수 없다.
    /// 스냅샷이 붙잡은 요소 참조를 그대로 순회한다 (선택집합 재조회 금지 — 타입 요약 참조).
    /// </summary>
    public void RollbackAll()
    {
        foreach (var (element, before) in Pairs)
        {
            Apply(element, before);
        }
    }

    /// <summary>스냅샷 해제. 이후 <see cref="Active"/>는 false, <see cref="Pairs"/>는 빈 열거다.</summary>
    public void Reset()
    {
        _elements = null;
        _baseStates = null;
    }
}
