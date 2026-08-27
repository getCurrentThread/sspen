using SSPen.Diagnostics;
using SSPen.Interop;

namespace SSPen.Annotation;

/// <summary>
/// 이관 대상 후보 1개: 문서 + 그 문서를 렌더하는 서피스의 물리 경계와 DPI.
/// <see cref="ContentSurfaceWindow"/>를 직접 들지 않으므로 이관 절차 전체가 창 없이 검증 가능하다.
/// </summary>
public readonly record struct TransferSurface(
    AnnotationDocument Document,
    PhysicalRect Bounds,
    double DpiScale);

/// <summary>
/// 모니터 간 이관 절차 (SEL-14). <b>프로덕션과 테스트가 같은 코드를 탄다</b> —
/// 이 클래스가 없으면 테스트가 이관 절차를 재구현하게 되고, 그러면 프로덕션의 순서(R19)나
/// 억제 스코프(LD-5)를 뒤집어도 증인이 전부 초록불로 남는다. 그것이 R15/R19/R22가 막으려던 결함 유형 자체다.
/// </summary>
public static class SelectionTransfer
{
    /// <summary>
    /// 놓은 물리 지점을 품는 서피스 (CRIT-06: 어느 모니터에도 안 걸리면 null → 이관 없이 원본 유지).
    /// 모니터 사이 공백이나 가상 스크린 밖으로 놓는 경우가 여기로 온다.
    /// </summary>
    public static TransferSurface? ResolveTarget(
        IReadOnlyList<TransferSurface> surfaces, int dropPhysicalX, int dropPhysicalY)
    {
        foreach (var surface in surfaces)
        {
            if (surface.Bounds.Contains(dropPhysicalX, dropPhysicalY))
            {
                return surface;
            }
        }
        return null;
    }

    /// <summary>
    /// 이관 실행. 반환값은 소유권 변경이 반영된 델타 목록이며, <b>이것이 원장에 실려야</b>
    /// undo가 상태와 소유권을 함께 되돌린다 (SEL-AC-10).
    /// </summary>
    /// <param name="selection">억제 스코프 제공자 (LD-5).</param>
    public static IReadOnlyList<TransformDelta> Execute(
        IReadOnlyList<TransformDelta> deltas,
        IReadOnlyList<TransferSurface> surfaces,
        TransferSurface target,
        SelectionModel selection)
    {
        var result = new List<TransformDelta>(deltas.Count);

        // SEL-AC-18: 원본 인덱스 오름차순으로 진행해야 여러 요소를 한 번에 옮겨도 상대 z순서가
        // 대상 최상단에 원본 그대로 재현된다. 순서 산출은 순수 계획 함수가 소유한다.
        var order = SelectionOperations.PlanTransferOrder([.. deltas.Select(d => d.Element)], deltas);

        // 중복 요소에 관대하게(마지막 것이 이김) — ToDictionary는 중복에서 던진다.
        // 호출부가 요소당 델타 1개를 보장하지만, 공개 진입점이므로 명시되지 않은 전제로
        // 던지게 두지 않는다. PlanTransferOrder도 같은 규칙을 따른다.
        var byElement = new Dictionary<AnnotationElement, TransformDelta>();
        foreach (var delta in deltas)
        {
            byElement[delta.Element] = delta;
        }

        var handled = new HashSet<AnnotationElement>();
        foreach (var element in order)
        {
            if (!handled.Add(element))
            {
                continue; // 중복 항목은 한 번만 이관한다.
            }
            var delta = byElement[element];
            var source = FindSurface(surfaces, delta.AfterOwner);
            if (source is not { } from || ReferenceEquals(from.Document, target.Document))
            {
                result.Add(delta); // 같은 모니터 안 변형 — 이관 없음.
                continue;
            }

            // R19: **반드시** Remove → 보정 → Add 순서다. Add가 ElementAdded → BuildVisual을 동기 호출하고
            // BuildVisual이 행렬을 심으므로, Add 이후에 보정하면 새 시각물이 낡은 행렬로 굳고
            // 그걸 갱신할 후속 이벤트가 없다.
            // LD-5: Remove가 조건 없이 ElementRemoved를 발화하고 R17 구독자가 선택집합에서 떨구므로,
            // 억제하지 않으면 모니터를 넘겨 놓는 순간 선택이 통째로 비워진다 (SEL-AC-5 위반).
            using (selection.SuppressInvalidation())
            {
                from.Document.Remove(element);
                element.TransformState = SelectionOperations.RebaseState(
                    delta.After,
                    element.LocalBounds,
                    from.Bounds,
                    from.DpiScale,
                    target.Bounds,
                    target.DpiScale);
                target.Document.Add(element);
            }

            Log.Info($"이관: 요소 {element.Id} {from.Document.SurfaceId} → {target.Document.SurfaceId}");

            result.Add(delta with
            {
                After = element.TransformState,
                AfterOwner = target.Document,
            });
        }
        return result;
    }

    private static TransferSurface? FindSurface(
        IReadOnlyList<TransferSurface> surfaces, AnnotationDocument document)
    {
        foreach (var surface in surfaces)
        {
            if (ReferenceEquals(surface.Document, document))
            {
                return surface;
            }
        }
        return null;
    }
}
