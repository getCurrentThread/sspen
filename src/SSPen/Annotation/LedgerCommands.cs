using SSPen.Diagnostics;

namespace SSPen.Annotation;

/// <summary>
/// 원장 명령의 <b>단일 소유자</b> (47단계 — R5, R7(c), SEL-12/SEL-13, f3, CRIT-06): 실행취소·전체 지우기·선택 삭제·클릭 통과 전환·
/// ESC 해제·변형 확정(이관 포함). 합성 루트(<c>AppController</c>)에 흩어져 있던 여섯 진입점을 옮긴 것으로, 창·핀·서피스 목록을
/// 모른다 — 서피스가 있어야 답할 수 있는 질문은 전부 델리게이트다:
/// <list type="bullet">
///   <item><c>documents</c> — 전체 지우기 대상 (서피스마다 하나).</item>
///   <item><c>ownerOf</c> — 요소의 <b>현재</b> 소유 문서 (이관 뒤에도 유효; <see cref="UndoLedger"/>와 같은 술어).</item>
///   <item><c>flushPendingTransforms</c> — R7(c): 원장에 싣거나 소비하는 진입점 <b>선두</b>의 휠 세션 확정 팬아웃 (전 서피스).</item>
///   <item><c>transferSurfaces</c> — 이관 후보 투사 (<see cref="SurfaceProjection"/>); 놓은 지점이 있을 때만 평가한다.</item>
///   <item><c>closePins</c> — 전체 지우기의 핀 닫기. <b>실행취소 대상이 아니다</b> (원장은 판서 문서만 다룬다).</item>
/// </list>
/// 이름이 <c>EditingCommands</c>가 아닌 이유: WPF <c>System.Windows.Documents.EditingCommands</c>와 충돌한다.
/// <see cref="SelectionModel.SelectionChanged"/>는 구독하지 않는다 (R5) — 클릭 통과 전환은 제스처(제자리 클릭·ESC·삭제 완료)에만
/// 매달리며, 그 사건 없이 선택이 비는 여섯 경로(도구 전환 등)에서는 일어나지 않는다.
/// </summary>
public sealed class LedgerCommands(
    AppState state,
    SelectionModel selection,
    UndoLedger ledger,
    Func<IReadOnlyList<AnnotationDocument>> documents,
    Func<AnnotationElement, AnnotationDocument?> ownerOf,
    Action flushPendingTransforms,
    Func<IReadOnlyList<TransferSurface>> transferSurfaces,
    Action closePins)
{
    /// <summary>
    /// 가장 최근 조작 취소 (전역 시간순 원장). 플러시가 <b>먼저</b>다 — 없으면 확대 직후 실행취소가 확대가 아니라
    /// 그 이전 조작을 되돌리고, 뒤늦게 깨어난 유휴 타이머가 그 위에 변형 항목을 얹는다 (R7(c)).
    /// </summary>
    public void Undo()
    {
        flushPendingTransforms();
        ledger.Undo();
    }

    /// <summary>
    /// 모든 서피스 전체 지우기 — 판서는 하나의 원장 항목.
    /// 고정해 둔 핀 캡처도 함께 닫는다 (사용자 요청 15차): "전체 지우기"가 화면을
    /// 깨끗이 비우는 동작이라고 기대하는데 핀만 남으면 다시 하나씩 닫아야 했다.
    /// <b>핀 닫기는 실행취소 대상이 아니다</b> — 원장은 판서 문서만 다룬다.
    /// </summary>
    public void ClearAll()
    {
        flushPendingTransforms();
        var cleared = documents()
            .Select(document => (Document: document, Snapshot: document.Clear()))
            .ToList();
        ledger.RecordClearAll(cleared);
        // R10: 장식은 선택집합을 따라가므로 해제하지 않으면 빈 화면에 핸들만 남는다.
        selection.Clear();
        closePins();
    }

    /// <summary>
    /// 선택 요소 전부 삭제 (SEL-13). 문서가 여럿이어도 원장 <b>1항목</b>이라
    /// 실행취소 1번으로 전부 원래 자리에 돌아온다 (f3).
    /// </summary>
    public void DeleteSelection()
    {
        // 진행 중인 휠 확대를 **먼저** 확정한다 (R7). 안 그러면 450ms 유휴 타이머가 뒤늦게 깨어나
        // 이미 삭제된 요소의 변형을 삭제 항목 **뒤에** 실어, 실행취소 1회가 아무 일도 하지 않는다.
        flushPendingTransforms();
        // 계획을 먼저 완결한다 — 제거하면서 수집하면 앞 요소가 빠질 때마다 뒤 인덱스가 밀려 복원 자리가 어긋난다.
        var plan = SelectionOperations.PlanDelete(selection.Elements, ownerOf);
        if (plan.Count == 0)
        {
            return;
        }
        foreach (var entry in plan)
        {
            entry.Document.Remove(entry.Element);
        }
        ledger.RecordDeleteSelection(
            [.. plan.Select(e => (e.Document, e.Element, e.Index))]);
        Log.Info($"선택 삭제: 요소 {plan.Count}개");
        // R5: 삭제로 선택이 비는 것도 사용자의 **명시적** 해제 제스처다 (Clear는 여기 안에서 일어난다).
        EngageClickThrough();
    }

    /// <summary>
    /// 명시적 해제 제스처(제자리 클릭·ESC·삭제 완료) 뒤 클릭 통과로 전환 (R5).
    ///
    /// <b>선택집합 변화가 아니라 제스처에 매달아야 한다</b>: 선택이 비는 경로는 6개이고 그중
    /// 도구 전환에 걸리면 펜 버튼을 눌러도 곧바로 도구가 해제되어 아무 도구도 고를 수 없게 된다.
    /// <c>ClickThrough=true</c>는 <c>SetActiveTool(None)</c>을 강제하고, 그 <c>ActiveToolChanged</c>가
    /// 선택집합까지 비우므로(SEL-B-4) 여기서 별도로 <c>Clear</c>를 부를 필요는 없다.
    /// </summary>
    public void EngageClickThrough()
    {
        selection.Clear();
        state.ClickThrough = true;
        Log.Info("선택 해제 → 클릭 통과");
    }

    /// <summary>ESC: 선택만 해제하고 클릭 통과로 넘어간다 (R3 + R5). 선택이 비어 있으면 아무 일도 하지 않는다.</summary>
    public void ClearSelectionByEscape()
    {
        if (selection.Count == 0)
        {
            return;
        }
        Log.Info("ESC: 선택 해제");
        EngageClickThrough();
    }

    /// <summary>
    /// 변형 드래그 1회 확정 (SEL-12). 다중 선택·다중 문서가 섞여도 원장 <b>1항목</b>이다 (f3).
    /// </summary>
    /// <param name="dropPhysical">
    /// 놓은 물리 지점. <b>null이면 이관 판정을 건너뛴다</b> — 크기·회전·휠 확대는 요소를 어디에도
    /// 놓지 않았으므로, 커서가 옆 모니터 위에 있다는 이유로 선택 전체가 이관되면 의도와 정반대다.
    /// </param>
    public void CommitTransform(IReadOnlyList<TransformDelta> deltas, (int X, int Y)? dropPhysical)
    {
        // 이관 절차는 SelectionTransfer가 소유한다 — 테스트와 프로덕션이 **같은 코드**를 타야
        // 순서(R19)나 억제 스코프(LD-5)를 뒤집었을 때 증인이 빨간불이 된다.
        var committed = deltas;
        if (dropPhysical is { } drop)
        {
            var surfaces = transferSurfaces();
            // CRIT-06: 놓은 지점이 어느 모니터에도 걸치지 않으면 (모니터 사이 공백 등) 이관하지 않고 원본을 유지한다.
            if (SelectionTransfer.ResolveTarget(surfaces, drop.X, drop.Y) is { } to)
            {
                committed = SelectionTransfer.Execute(deltas, surfaces, to, selection);
            }
        }

        ledger.RecordTransform(committed);
        Log.Info($"변형 확정: 요소 {committed.Count}개, 놓은 지점 {(dropPhysical is { } p ? $"({p.X},{p.Y})" : "없음(이관 판정 생략)")}");
    }
}
