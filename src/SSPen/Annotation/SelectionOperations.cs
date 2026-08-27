using System.Windows;
using SSPen.Interop;

namespace SSPen.Annotation;

/// <summary>삭제 계획 1건: 어느 문서의 몇 번째 자리에 있던 요소인가 (복원 좌표).</summary>
public readonly record struct DeletePlanEntry(
    AnnotationDocument Document,
    AnnotationElement Element,
    int Index);

/// <summary>
/// 선택 조작의 순수 계획 함수 (ARCH-15, D4). 문서를 **읽기만** 하고 아무것도 변경하지 않는다 —
/// 계획과 실행을 분리해야 "제거하면서 인덱스를 수집"하는 순서 결함이 구조적으로 불가능해진다.
/// </summary>
public static class SelectionOperations
{
    /// <summary>
    /// 삭제 계획 (SEL-13). **인덱스 수집을 제거 전에 완결**하는 것이 이 함수의 존재 이유다:
    /// 제거하면서 수집하면 앞 요소가 빠질 때마다 뒤 인덱스가 밀려 복원 자리가 어긋난다.
    /// 반환 순서는 문서별 인덱스 오름차순이며, 복원도 **같은 오름차순**으로 삽입해야 한다:
    /// 앞에서부터 자리를 채워야 매 삽입 시점에 목표 인덱스가 유효해진다 (연속 인덱스 사례 참고).
    /// </summary>
    public static IReadOnlyList<DeletePlanEntry> PlanDelete(
        IReadOnlyList<AnnotationElement> selection,
        Func<AnnotationElement, AnnotationDocument?> ownerLookup)
    {
        var plan = new List<DeletePlanEntry>();
        foreach (var element in selection)
        {
            if (ownerLookup(element) is not { } document)
            {
                continue; // 이미 어느 문서에도 없는 요소 (페이드 소멸 등) — 조용히 건너뛴다.
            }
            int index = document.IndexOf(element);
            if (index >= 0)
            {
                plan.Add(new DeletePlanEntry(document, element, index));
            }
        }
        // 문서별 오름차순으로 고정: 복원도 **같은 오름차순**으로 삽입해야 원래 자리로 돌아간다.
        return [.. plan.OrderBy(e => e.Document.SurfaceId, StringComparer.Ordinal).ThenBy(e => e.Index)];
    }

    /// <summary>
    /// 이관 순서 (SEL-AC-18): **원본 인덱스 오름차순**. 이 순서로 대상 문서에 Add하면
    /// 여러 요소를 한 번에 옮겨도 서로의 상대 z순서가 원본 그대로 대상 최상단에 재현된다.
    /// 인덱스는 각 요소의 **자기 소유 문서** 기준이다 — 다중 문서 선택이 섞일 수 있기 때문이다.
    /// 소유 문서에서 이미 사라진 요소는 맨 뒤로 밀린다 (순서를 잎지 않고 보존).
    /// </summary>
    public static IReadOnlyList<AnnotationElement> PlanTransferOrder(
        IReadOnlyList<AnnotationElement> selection,
        IReadOnlyList<TransformDelta> deltas)
    {
        var owners = new Dictionary<AnnotationElement, AnnotationDocument>();
        foreach (var delta in deltas)
        {
            owners[delta.Element] = delta.AfterOwner;
        }

        var ordered = new List<(AnnotationElement Element, int Index, int Tie)>();
        for (int i = 0; i < selection.Count; i++)
        {
            var element = selection[i];
            int index = owners.TryGetValue(element, out var owner) ? owner.IndexOf(element) : -1;
            ordered.Add((element, index < 0 ? int.MaxValue : index, i));
        }
        return [.. ordered.OrderBy(e => e.Index).ThenBy(e => e.Tie).Select(e => e.Element)];
    }

    /// <summary>
    /// 모니터 이관 시 변형 상태 4-튜플 보정 (ARCH-20). <c>r = srcDpi / tgtDpi</c>일 때:
    /// <code>
    /// ScaleX'       = ScaleX · r
    /// ScaleY'       = ScaleY · r
    /// AngleDegrees' = AngleDegrees            (회전은 DPI 불변)
    /// Translation'  = Rebase(c + Translation, …) − c       (c = 로컬 경계 중심)
    /// </code>
    ///
    /// 왜 스케일 보정이 필요한가: 요소 기하와 <c>Thickness</c>가 원본 서피스의 논리 단위로 굳어 있고
    /// get-only인데, 대상 서피스는 자기 논리 단위로 렌더한다. DPI가 다르면(100%→150%) 놓는 순간
    /// 요소가 1.5배로 뛴다. <c>Thickness</c>가 get-only이므로 <c>TransformState</c>의 스케일 성분이
    /// **유일한 보정 수단**이다.
    ///
    /// 왜 <c>Translation</c>만 다르게 다루는가: 이것은 위치가 아니라 **변위**다. 점 사상을 그대로
    /// 먹이면 대상 모니터의 원점 오프셋이 중복 가산되어 요소가 엉뚱한 곳으로 간다.
    /// 기준점 <c>c</c>를 더해 위치로 만든 뒤 사상하고 다시 빼는 형태가 유일하게 옳다.
    /// </summary>
    public static ElementTransformState RebaseState(
        ElementTransformState state,
        Rect localBounds,
        PhysicalRect sourceMonitor,
        double sourceDpi,
        PhysicalRect targetMonitor,
        double targetDpi)
    {
        double ratio = sourceDpi / targetDpi;
        var center = new Point(
            localBounds.X + localBounds.Width / 2,
            localBounds.Y + localBounds.Height / 2);

        // 변위 → 위치 → 사상 → 다시 변위.
        var asPosition = new Point(center.X + state.Translation.X, center.Y + state.Translation.Y);
        var mapped = CoordinateSpace.Rebase(asPosition, sourceMonitor, sourceDpi, targetMonitor, targetDpi);

        return state with
        {
            ScaleX = state.ScaleX * ratio,
            ScaleY = state.ScaleY * ratio,
            Translation = new Vector(mapped.X - center.X, mapped.Y - center.Y),
        };
    }
}
