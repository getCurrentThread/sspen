using SSPen.Diagnostics;

namespace SSPen.Annotation;

/// <summary>
/// 전역 시간순 undo 원장 (플랜 CRIT-1/ARCH-7 확정 사양).
/// 3개 서피스 문서를 가로질러 커밋된 모든 조작(획/도형/텍스트 추가, 지우기, 전체 지우기, 변형, 선택 삭제)을
/// 시간순으로 기록하고, Alt+Shift+6은 어느 모니터에서 일어났든 가장 최근 조작을 되돌린다.
/// Alt+Shift+7 전체 지우기는 모든 서피스를 비우며 하나의 원장 항목이 된다.
///
/// 문서 참조 정책 (리더 결정 LD-2 / 옵션 C2, 범위 축소):
/// <list type="bullet">
/// <item><see cref="RecordAdd"/>·<see cref="RecordTransform"/>·<see cref="RecordDeleteSelection"/>는
/// **문서-비의존**이다. 이관(f7)을 몇 번 거쳤든 undo 시점에 현재 소유자를 찾아 올바르게 동작한다.</item>
/// <item><see cref="RecordErase"/>·<see cref="RecordClearAll"/>은 기록 시점 문서 참조를 유지한다.
/// 지워진 요소는 어느 문서에도 없어 이관 대상이 될 수 없으므로 lookup이 항상 null이 되어 도달 불가 코드가 된다.</item>
/// </list>
/// </summary>
public sealed class UndoLedger
{
    private readonly List<IUndoOperation> _operations = [];
    private readonly Func<AnnotationElement, AnnotationDocument?> _ownerLookup;
    private readonly SelectionModel _selection;

    /// <param name="ownerLookup">요소의 **현재** 소유 문서 조회. 어느 문서에도 없으면 null.</param>
    /// <param name="selection">소유권 이동 구간의 선택 무효화를 억제하기 위한 선택집합 (LD-5).</param>
    public UndoLedger(Func<AnnotationElement, AnnotationDocument?> ownerLookup, SelectionModel selection)
    {
        _ownerLookup = ownerLookup;
        _selection = selection;
    }

    public int Count => _operations.Count;

    /// <summary>undo로 문서에서 제거된 요소 (페이드 취소 연동용).</summary>
    public event Action<AnnotationElement>? ElementRemovedByUndo;

    /// <summary>요소 추가 기록. 문서를 잡지 않는다 — undo 시점에 현재 소유자를 조회한다 (LD-2).</summary>
    public void RecordAdd(AnnotationElement element) =>
        _operations.Add(new AddOperation(this, element));

    public void RecordErase(AnnotationDocument document, AnnotationElement element, int index) =>
        _operations.Add(new EraseOperation(document, element, index));

    public void RecordClearAll(IReadOnlyList<(AnnotationDocument Document, IReadOnlyList<AnnotationElement> Snapshot)> cleared)
    {
        if (cleared.Any(c => c.Snapshot.Count > 0))
        {
            _operations.Add(new ClearAllOperation(cleared));
        }
    }

    /// <summary>
    /// 변형 1회 = 원장 1항목 (f3, SEL-12). 다중 선택도 하나로 묶이며 실행취소 1번에 전부 되돌아간다.
    /// 모니터 간 이관(f7)이 함께 일어났으면 소유권도 같은 항목에서 복귀한다 (SEL-AC-10).
    /// </summary>
    public void RecordTransform(IReadOnlyList<TransformDelta> deltas)
    {
        if (deltas.Count == 0)
        {
            return;
        }
        _operations.Add(new TransformOperation(this, deltas));
    }

    /// <summary>다중 선택 삭제 = 원장 1항목 (f3, SEL-13). 실행취소 1번으로 전부 원래 인덱스에 복원된다.</summary>
    public void RecordDeleteSelection(
        IReadOnlyList<(AnnotationDocument Document, AnnotationElement Element, int Index)> removed)
    {
        if (removed.Count == 0)
        {
            return;
        }
        _operations.Add(new DeleteSelectionOperation(removed));
    }

    /// <summary>가장 최근 조작 1건 되돌리기. 원장이 비었거나 소유자를 못 찾으면 false.</summary>
    public bool Undo()
    {
        if (_operations.Count == 0)
        {
            return false;
        }
        var operation = _operations[^1];
        _operations.RemoveAt(_operations.Count - 1);
        return operation.Undo();
    }

    /// <summary>
    /// 페이드로 소멸한 요소의 원장 항목 정리: 남은 Add 항목이 이미 사라진 요소를
    /// 가리키지 않도록 제거한다 (지우기 항목은 재삽입이 여전히 유효하므로 유지).
    /// </summary>
    public void PurgeElement(AnnotationElement element) =>
        _operations.RemoveAll(op => op is AddOperation add && ReferenceEquals(add.Element, element));

    private interface IUndoOperation
    {
        /// <returns>실제로 되돌렸으면 true. 무증상 무동작을 막기 위해 실패를 반드시 보고한다.</returns>
        bool Undo();
    }

    private sealed class AddOperation(UndoLedger ledger, AnnotationElement element) : IUndoOperation
    {
        public AnnotationElement Element { get; } = element;

        public bool Undo()
        {
            // 이관을 몇 번 거쳤든 현재 소유자를 찾는다 — 낡은 문서 참조로 조용히 실패하지 않는다 (R3).
            var owner = ledger._ownerLookup(Element);
            if (owner is null)
            {
                // 무증상 금지: 원장 항목은 이미 소비됐고 아무것도 되돌아가지 않았음을 반드시 남긴다.
                Log.Info($"실행취소: 요소 {Element.Id}의 소유 문서를 찾지 못해 되돌리지 못했습니다.");
                return false;
            }
            if (!owner.Remove(Element))
            {
                Log.Info($"실행취소: 요소 {Element.Id}가 소유 문서에서 이미 사라져 되돌리지 못했습니다.");
                return false;
            }
            ledger.ElementRemovedByUndo?.Invoke(Element);
            return true;
        }
    }

    private sealed class EraseOperation(AnnotationDocument document, AnnotationElement element, int index)
        : IUndoOperation
    {
        public bool Undo()
        {
            document.Insert(index, element);
            return true;
        }
    }

    private sealed class ClearAllOperation(
        IReadOnlyList<(AnnotationDocument Document, IReadOnlyList<AnnotationElement> Snapshot)> cleared)
        : IUndoOperation
    {
        public bool Undo()
        {
            foreach (var (document, snapshot) in cleared)
            {
                foreach (var element in snapshot)
                {
                    document.Add(element);
                }
            }
            return true;
        }
    }

    private sealed class TransformOperation(UndoLedger ledger, IReadOnlyList<TransformDelta> deltas)
        : IUndoOperation
    {
        public bool Undo()
        {
            // 역순 복원: 같은 요소가 여러 델타에 걸쳐 있어도 기록 순서의 역이 올바른 결과를 낸다.
            for (int i = deltas.Count - 1; i >= 0; i--)
            {
                var delta = deltas[i];
                delta.Element.TransformState = delta.Before;

                if (!ReferenceEquals(delta.AfterOwner, delta.BeforeOwner))
                {
                    // 소유권 복귀 (SEL-AC-10). Remove가 조건 없이 ElementRemoved를 발화하므로
                    // 억제 스코프 안에서 수행하지 않으면 이관 undo에서 선택이 통째로 비워진다 (LD-5).
                    using (ledger._selection.SuppressInvalidation())
                    {
                        delta.AfterOwner.Remove(delta.Element);
                        delta.BeforeOwner.Add(delta.Element);
                    }
                    Log.Info(
                        $"실행취소: 요소 {delta.Element.Id} 소유권 복귀 " +
                        $"{delta.AfterOwner.SurfaceId} → {delta.BeforeOwner.SurfaceId}");
                }

                // 상태 복원을 뷰까지 전파한다. Add 경로를 탄 경우에도 새 시각물이 복원된 상태를 읽도록
                // 마지막에 한 번 더 발화한다 (R15).
                delta.BeforeOwner.RaiseElementTransformChanged(delta.Element);
            }
            return true;
        }
    }

    private sealed class DeleteSelectionOperation(
        IReadOnlyList<(AnnotationDocument Document, AnnotationElement Element, int Index)> removed)
        : IUndoOperation
    {
        public bool Undo()
        {
            // **오름차순** 삽입이 올바르다. 역순으로 넣으면 연속된 인덱스를 함께 지웠을 때 깨진다:
            // [a,b,c,d]에서 a,b,c를 지우면 [d]가 남고, 역순(c→2, b→1, a→0) 삽입은
            // [a,d,b,c]를 만든다. 오름차순(a→0, b→1, c→2)은 앞에서부터 자리를 채워
            // 매 삽입 시점에 목표 인덱스가 이미 유효해지므로 항상 원본 배열을 복원한다.
            foreach (var (document, element, index) in removed)
            {
                document.Insert(index, element);
            }
            return true;
        }
    }
}
