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
