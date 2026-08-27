using System.Windows;

namespace SSPen.Annotation;

/// <summary>
/// 모니터별 콘텐츠 서피스 1개가 소유하는 판서 문서 (플랜 원칙 4).
/// 렌더링·페이드·undo는 이 모델 위에서 동작한다. UI 요소를 직접 담지 않는다.
/// </summary>
public sealed class AnnotationDocument
{
    private readonly List<AnnotationElement> _elements = [];

    public AnnotationDocument(string surfaceId)
    {
        SurfaceId = surfaceId;
    }

    public string SurfaceId { get; }

    public IReadOnlyList<AnnotationElement> Elements => _elements;

    public event Action<AnnotationElement>? ElementAdded;

    public event Action<AnnotationElement>? ElementRemoved;

    /// <summary>
    /// 제자리 변형 알림 (ARCH-01). 추가·제거에만 채널이 있는데 제자리 변형이라는 새 상태 변경 종류를
    /// 도입하면서 채널을 만들지 않으면 드래그 밖 모든 경로(undo, 롤백, 이관 보정)에서 뷰가 정지한다.
    /// 이때 헤드리스 증인은 상태만 검사하므로 **테스트가 통과시켜 버리는 무증상 결함**이 된다 (R15).
    /// </summary>
    public event Action<AnnotationElement>? ElementTransformChanged;

    /// <summary>
    /// 변형 알림 발화. <c>TransformState =</c> 대입 뒤에는 **반드시** 이것이 따라와야 한다 (R15 리뷰 규칙).
    /// 생산 호출자(입력 컨트롤러·원장 연산·셸)가 모두 동일 어셈블리라 internal로 둔다.
    /// 테스트는 공개 <see cref="ElementTransformChanged"/> 이벤트로만 관측한다.
    /// </summary>
    internal void RaiseElementTransformChanged(AnnotationElement element) =>
        ElementTransformChanged?.Invoke(element);

    public void Add(AnnotationElement element)
    {
        _elements.Add(element);
        ElementAdded?.Invoke(element);
    }

    /// <summary>undo 복원용: 원래 z-순서 위치에 재삽입.</summary>
    public void Insert(int index, AnnotationElement element)
    {
        _elements.Insert(Math.Clamp(index, 0, _elements.Count), element);
        ElementAdded?.Invoke(element);
    }

    public int IndexOf(AnnotationElement element) => _elements.IndexOf(element);

    public bool Remove(AnnotationElement element)
    {
        if (_elements.Remove(element))
        {
            ElementRemoved?.Invoke(element);
            return true;
        }
        return false;
    }

    /// <summary>전체 지우기: 스냅샷을 반환하고 비운다 (undo 원장이 스냅샷을 보관).</summary>
    public IReadOnlyList<AnnotationElement> Clear()
    {
        var snapshot = _elements.ToArray();
        _elements.Clear();
        foreach (var element in snapshot)
        {
            ElementRemoved?.Invoke(element);
        }
        return snapshot;
    }

    /// <summary>
    /// 지우개 히트테스트: 허용 오차 안의 요소 중 가장 가까운 것 하나 (동률이면 위쪽 요소).
    /// 근접한 두 획 사이 클릭은 더 가까운 획만 지운다 (플랜 유닛테스트 계약).
    /// 명중 판정과 순위 비교 모두 <see cref="AnnotationElement.ScreenDistanceTo"/> 하나만 쓴다 (ARCH-19):
    /// 서로 다른 요소의 거리를 직접 비교하므로 모델 공간 값을 섞으면 화면상 더 먼 요소가 선택된다.
    /// </summary>
    public AnnotationElement? HitTestNearest(Point p, double tolerance)
    {
        AnnotationElement? best = null;
        double bestDistance = double.MaxValue;
        // 뒤에서부터: 동률일 때 나중에 그린(위쪽) 요소 우선.
        for (int i = _elements.Count - 1; i >= 0; i--)
        {
            var element = _elements[i];
            if (!element.HitTest(p, tolerance))
            {
                continue;
            }
            double distance = element.ScreenDistanceTo(p);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = element;
            }
        }
        return best;
    }
}
