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

    /// <summary>
    /// 모니터 간 이동 변위의 DPI 환산 (D1). 요소 기하는 자기 소유 서피스의 논리 단위로 굳어 있는데
    /// 변위는 게스처가 일어난 서피스의 논리 단위이므로, 두 모니터의 배율이 다르면 같은 손동작이
    /// 서로 다른 물리 거리로 번역된다. 물리 거리를 보존하는 환산은
    /// <c>d_target = d_source · (srcDpi / tgtDpi)</c>이며, 이관(<see cref="RebaseState"/>)이 쓰는
    /// 비율 <c>r = sourceDpi / targetDpi</c>와 <b>같은 값</b>이다 (ARCH-20). 두 식이 갈라지지 않도록
    /// 일부러 같은 파일에 이웃으로 둔다 — 파일이 갈리면 가드 차이가 영원히 안 보인다.
    ///
    /// 대상 배율이 <b>높을수록 논리 변위는 작아진다</b>: 150% 모니터에서는 1 논리 단위가 1.5 물리
    /// 픽셀이므로, 같은 물리 거리를 유지하려면 논리 변위를 <c>1/1.5</c>로 줄여야 한다.
    ///
    /// <see cref="RebaseState"/>와 가드가 다른 이유: 이쪽은 <c>targetDpi</c>가 주입 델리게이트
    /// (<c>AppController.DpiOf</c>)에서 오고, 그 델리게이트가 0을 내면 변위가 ±∞가 되어 요소가
    /// 화면 밖으로 사라진다. 오늘 그 델리게이트는 못 찾으면 1을 돌려주므로 0은 오지 않지만,
    /// 주입 지점이라 가드를 보존한다. 배율이 같을 때는 <c>×1.0</c> 왕복조차 넣지 않고
    /// <b>원본 벡터를 그대로</b> 돌려준다.
    ///
    /// 이 리그는 3대 모두 100%(r=1)라 통합 테스트가 이 식을 절대 잡지 못한다 — 헤드리스 증인이
    /// 유일한 방어선이며 <b>반드시 r ≠ 1</b>로 검증해야 한다 (R18, AGENTS.md:109).
    /// </summary>
    public static Vector ScaleDisplacementForDpi(Vector delta, double sourceDpi, double targetDpi) =>
        targetDpi > 0 && Math.Abs(targetDpi - sourceDpi) > 1e-9
            ? new Vector(delta.X * sourceDpi / targetDpi, delta.Y * sourceDpi / targetDpi)
            : delta;

    /// <summary>
    /// 선택 전체 이동 계획 (SEL-AC-9). 순회 대상은 <b>선택집합 전체</b>이지 이 서피스가 소유한
    /// 부분집합이 아니다 — 모니터에 걸친 선택에서 <b>이동만</b>은 허용되며(SEL-LIM-5), 다른 문서
    /// 소속 요소도 함께 움직여야 선택이 통째로 따라오기 때문이다.
    ///
    /// 다른 모니터 소속 요소의 변위는 <see cref="ScaleDisplacementForDpi"/>가 환산한다 (D1).
    /// 반환 순서는 <paramref name="selected"/>의 순서 그대로다 —
    /// <paramref name="baseStates"/>를 순회하지 않는다: 사전 순서는 선택 순서가 아니고,
    /// 스냅샷에는 선택집합에 없는 핸들 대상이 섞일 수 있다.
    ///
    /// 매 프레임 <paramref name="baseStates"/>(드래그 시작 상태)에서 <b>재계산</b>하고 요소의 현재
    /// 상태를 절대 읽지 않는다 — 직전 프레임 결과에 누적하면 부동소수 오차가 프레임마다 쌓여 요소가
    /// 서서히 어긋나고, 취소 복원 기준도 사라진다.
    ///
    /// 이 함수는 아무것도 쓰지 않는다 (ARCH-15, D4): 대입과 소유 문서 알림은 호출부의 단일
    /// 집행 지점이 맡는다 (R15).
    /// </summary>
    public static IReadOnlyList<(AnnotationElement Element, ElementTransformState Next)> PlanMove(
        IReadOnlyList<AnnotationElement> selected,
        IReadOnlyDictionary<long, ElementTransformState> baseStates,
        Vector delta,
        double sourceDpi,
        Func<AnnotationElement, double> targetDpiOf)
    {
        var plan = new List<(AnnotationElement, ElementTransformState)>(selected.Count);
        foreach (var element in selected)
        {
            if (!baseStates.TryGetValue(element.Id, out var start))
            {
                continue; // 제스처 시작 뒤에 선택에 더해진 요소 — 시작 상태가 없으므로 조용히 건너뛴다.
            }
            var scaled = ScaleDisplacementForDpi(delta, sourceDpi, targetDpiOf(element));
            plan.Add((element, TransformMath.Translate(start, scaled)));
        }
        return plan;
    }
}
