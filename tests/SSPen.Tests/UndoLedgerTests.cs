using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// WI-5: 전역 시간순 undo 원장 (CRIT-1/ARCH-7 확정 사양) —
/// 3개 서피스 문서를 가로지르는 시간순 undo, 전체 지우기, 페이드 취소 연동.
/// SEL-12/SEL-13으로 변형·선택 삭제가 추가되고, LD-2로 Add가 문서-비의존이 되었다.
/// </summary>
public class UndoLedgerTests
{
    private static StrokeElement NewStroke() =>
        new([new Point(0, 0), new Point(10, 10)], Colors.Black, 3, isHighlighter: false);

    private sealed record Rig(
        UndoLedger Ledger,
        SelectionModel Selection,
        AnnotationDocument D1,
        AnnotationDocument D2,
        AnnotationDocument D3);

    /// <summary>
    /// LD-2: 원장이 <c>ownerLookup</c>을 주입받으므로 테스트가 문서 집합을 소유하고 조회를 제공한다.
    /// 프로덕션의 <c>AppController</c> 배선(<c>_surfaces</c> 선형 주사)과 같은 의미다.
    /// </summary>
    private static Rig Setup(bool attachSelection = false)
    {
        var d1 = new AnnotationDocument("M1");
        var d2 = new AnnotationDocument("M2");
        var d3 = new AnnotationDocument("M3");
        var documents = new[] { d1, d2, d3 };
        var selection = new SelectionModel();
        var ledger = new UndoLedger(
            element => documents.FirstOrDefault(d => d.Elements.Contains(element)),
            selection);
        if (attachSelection)
        {
            foreach (var document in documents)
            {
                selection.AttachTo(document);
            }
        }
        return new Rig(ledger, selection, d1, d2, d3);
    }

    private static TransformDelta Delta(
        AnnotationElement element,
        ElementTransformState before,
        ElementTransformState after,
        AnnotationDocument beforeOwner,
        AnnotationDocument afterOwner) =>
        new(element, before, after, beforeOwner, afterOwner);

    // ---- 기존 계약 (전건 유지) ----

    [Fact]
    public void Undo_IsGloballyChronological_AcrossDocuments()
    {
        var rig = Setup();
        var first = NewStroke();
        var second = NewStroke();

        // 모니터 1에 그리고, 그다음 모니터 2에 그린다.
        rig.D1.Add(first);
        rig.Ledger.RecordAdd(first);
        rig.D2.Add(second);
        rig.Ledger.RecordAdd(second);

        // 첫 undo: 모니터 2의 획(가장 최근)이 사라져야 한다 — AC-9 크로스 모니터 계약.
        Assert.True(rig.Ledger.Undo());
        Assert.Empty(rig.D2.Elements);
        Assert.Single(rig.D1.Elements);

        Assert.True(rig.Ledger.Undo());
        Assert.Empty(rig.D1.Elements);

        Assert.False(rig.Ledger.Undo()); // 원장이 비면 false
    }

    [Fact]
    public void Undo_Erase_RestoresElementAtOriginalIndex()
    {
        var rig = Setup();
        var bottom = NewStroke();
        var middle = NewStroke();
        var top = NewStroke();
        foreach (var s in (StrokeElement[])[bottom, middle, top])
        {
            rig.D1.Add(s);
            rig.Ledger.RecordAdd(s);
        }

        // 중간 획 지우기 → undo → 원래 z-순서 위치로 복원.
        int index = rig.D1.IndexOf(middle);
        rig.D1.Remove(middle);
        rig.Ledger.RecordErase(rig.D1, middle, index);

        Assert.True(rig.Ledger.Undo());
        Assert.Equal(new AnnotationElement[] { bottom, middle, top }, rig.D1.Elements);
    }

    [Fact]
    public void ClearAll_IsSingleLedgerEntry_AndUndoRestoresAllSurfaces()
    {
        var rig = Setup();
        var s1 = NewStroke();
        var s2 = NewStroke();
        rig.D1.Add(s1);
        rig.Ledger.RecordAdd(s1);
        rig.D2.Add(s2);
        rig.Ledger.RecordAdd(s2);

        // Alt+Shift+7: 모든 서피스 지우기 — 원장 항목 1개.
        var cleared = new List<(AnnotationDocument, IReadOnlyList<AnnotationElement>)>
        {
            (rig.D1, rig.D1.Clear()),
            (rig.D2, rig.D2.Clear()),
            (rig.D3, rig.D3.Clear()),
        };
        rig.Ledger.RecordClearAll(cleared);
        Assert.Empty(rig.D1.Elements);
        Assert.Empty(rig.D2.Elements);

        // undo 1번으로 전부 복원.
        Assert.True(rig.Ledger.Undo());
        Assert.Equal(new AnnotationElement[] { s1 }, rig.D1.Elements);
        Assert.Equal(new AnnotationElement[] { s2 }, rig.D2.Elements);
        Assert.Empty(rig.D3.Elements);
    }

    [Fact]
    public void ClearAll_WhenAllEmpty_RecordsNothing()
    {
        var rig = Setup();
        rig.Ledger.RecordClearAll([(rig.D1, rig.D1.Clear()), (rig.D2, rig.D2.Clear()), (rig.D3, rig.D3.Clear())]);
        Assert.Equal(0, rig.Ledger.Count);
    }

    [Fact]
    public void UndoOfAdd_NotifiesRemoval_ForFadeCancellation()
    {
        var rig = Setup();
        var controller = new FadingInkController(new FadeSchedulerCore())
        {
            Active = true,
            Duration = TimeSpan.FromSeconds(3),
        };
        rig.Ledger.ElementRemovedByUndo += controller.OnElementRemoved;

        var stroke = NewStroke();
        rig.D1.Add(stroke);
        rig.Ledger.RecordAdd(stroke);
        controller.OnElementCommitted(stroke, DateTime.UtcNow);
        Assert.Equal(1, controller.Core.PendingCount);

        // undo → 문서에서 제거 + 보류 페이드 취소 (CRIT-1 상호작용).
        Assert.True(rig.Ledger.Undo());
        Assert.Empty(rig.D1.Elements);
        Assert.Equal(0, controller.Core.PendingCount);
    }

    [Fact]
    public void PurgeElement_RemovesStaleAddEntries()
    {
        var rig = Setup();
        var faded = NewStroke();
        var kept = NewStroke();
        rig.D1.Add(faded);
        rig.Ledger.RecordAdd(faded);
        rig.D1.Add(kept);
        rig.Ledger.RecordAdd(kept);

        // 페이드 완료: 요소는 이미 문서에서 사라졌고 원장 항목도 정리된다.
        rig.D1.Remove(faded);
        rig.Ledger.PurgeElement(faded);
        Assert.Equal(1, rig.Ledger.Count);

        // 남은 undo는 kept만 제거.
        Assert.True(rig.Ledger.Undo());
        Assert.Empty(rig.D1.Elements);
        Assert.False(rig.Ledger.Undo());
    }

    // ---- LD-2: 문서-비의존 Add ----

    [Fact]
    public void Undo_AfterTransfer_ThenUndoOlderStroke_RemovesFromCurrentOwner()
    {
        // 사전 부검 1: 이관된 요소의 Add 항목이 낡은 문서를 가리키면 원장 항목만 소비되고
        // 화면은 그대로인 무증상 버그가 난다. ownerLookup은 현재 소유자를 찾으므로 그런 일이 없다.
        var rig = Setup();
        var moved = NewStroke();
        var later = NewStroke();

        rig.D1.Add(moved);
        rig.Ledger.RecordAdd(moved);
        rig.D2.Add(later);
        rig.Ledger.RecordAdd(later);

        // 이관: 요소가 D1 → D2로 옮겨간다 (Add 항목은 여전히 원장에 남아 있다).
        rig.D1.Remove(moved);
        rig.D2.Add(moved);

        Assert.True(rig.Ledger.Undo());          // later 제거
        Assert.DoesNotContain(later, rig.D2.Elements);

        Assert.True(rig.Ledger.Undo());          // moved를 **현재 소유자** D2에서 제거
        Assert.DoesNotContain(moved, rig.D2.Elements);
        Assert.Empty(rig.D1.Elements);
        Assert.Empty(rig.D2.Elements);
    }

    [Fact]
    public void Undo_OwnerMissing_LogsAndReturnsFalse()
    {
        // 무증상 금지 계약: 소유자를 못 찾으면 항목은 소비하되 false를 돌려준다.
        var selection = new SelectionModel();
        var ledger = new UndoLedger(_ => null, selection);
        var orphan = NewStroke();
        ledger.RecordAdd(orphan);

        Assert.Equal(1, ledger.Count);
        Assert.False(ledger.Undo());
        Assert.Equal(0, ledger.Count);
    }

    // ---- SEL-12: TransformOperation ----

    [Fact]
    public void Undo_Transform_RestoresAllElementsInOneStep()
    {
        var rig = Setup();
        var a = NewStroke();
        var b = NewStroke();
        rig.D1.Add(a);
        rig.D1.Add(b);

        var beforeA = a.TransformState;
        var beforeB = b.TransformState;
        var afterA = TransformMath.Translate(beforeA, new Vector(100, 0));
        var afterB = new ElementTransformState(2, 2, 30, new Vector(5, 5));
        a.TransformState = afterA;
        b.TransformState = afterB;

        rig.Ledger.RecordTransform(
        [
            Delta(a, beforeA, afterA, rig.D1, rig.D1),
            Delta(b, beforeB, afterB, rig.D1, rig.D1),
        ]);
        Assert.Equal(1, rig.Ledger.Count); // 다중 요소 변형 = 원장 1항목 (f3)

        Assert.True(rig.Ledger.Undo());
        Assert.Equal(beforeA, a.TransformState);
        Assert.Equal(beforeB, b.TransformState);
    }

    [Fact]
    public void Undo_Transform_PreservesInstanceAndZOrder()
    {
        var rig = Setup();
        var bottom = NewStroke();
        var middle = NewStroke();
        var top = NewStroke();
        foreach (var s in (StrokeElement[])[bottom, middle, top])
        {
            rig.D1.Add(s);
        }
        long middleId = middle.Id;

        var before = middle.TransformState;
        var after = TransformMath.Translate(before, new Vector(40, 40));
        middle.TransformState = after;
        rig.Ledger.RecordTransform([Delta(middle, before, after, rig.D1, rig.D1)]);

        Assert.True(rig.Ledger.Undo());

        // 인스턴스 교체 금지 (SEL-ARCH-1): 같은 참조, 같은 Id, 같은 z순서.
        Assert.Equal(new AnnotationElement[] { bottom, middle, top }, rig.D1.Elements);
        Assert.Same(middle, rig.D1.Elements[1]);
        Assert.Equal(middleId, rig.D1.Elements[1].Id);
    }

    [Fact]
    public void Undo_TransformWithOwnershipChange_RestoresOriginalDocument()
    {
        var rig = Setup();
        var element = NewStroke();
        rig.D1.Add(element);

        // 이관 커밋: D1 → D2.
        var before = element.TransformState;
        var after = TransformMath.Translate(before, new Vector(1920, 0));
        rig.D1.Remove(element);
        element.TransformState = after;
        rig.D2.Add(element);
        rig.Ledger.RecordTransform([Delta(element, before, after, rig.D1, rig.D2)]);

        Assert.True(rig.Ledger.Undo());

        // SEL-AC-10: 소유권까지 원래 모니터로 되돌아온다.
        Assert.Contains(element, rig.D1.Elements);
        Assert.DoesNotContain(element, rig.D2.Elements);
        Assert.Equal(before, element.TransformState);
    }

    [Fact]
    public void Undo_TransferWithOwnershipChange_KeepsSelection()
    {
        // LD-5: 소유권 이동은 억제 스코프 안에서 일어나므로 선택이 살아남는다.
        var rig = Setup(attachSelection: true);
        var element = NewStroke();
        rig.D2.Add(element);
        rig.Selection.Set([element]);

        var before = element.TransformState;
        var after = TransformMath.Translate(before, new Vector(1920, 0));
        rig.Ledger.RecordTransform([Delta(element, before, after, rig.D1, rig.D2)]);

        Assert.True(rig.Ledger.Undo());

        Assert.Contains(element, rig.D1.Elements);
        Assert.True(rig.Selection.Contains(element), "이관 undo에서 선택이 유지되어야 한다 (SEL-AC-10).");
    }

    [Fact]
    public void Undo_TransformNotifiesViewThroughDocumentChannel()
    {
        // R15: 모델만 되돌리고 뷰에 알리지 않으면 테스트는 초록불인데 화면이 틀린다.
        var rig = Setup();
        var element = NewStroke();
        rig.D1.Add(element);
        var notified = new List<AnnotationElement>();
        rig.D1.ElementTransformChanged += notified.Add;

        var before = element.TransformState;
        var after = TransformMath.Translate(before, new Vector(10, 10));
        element.TransformState = after;
        rig.Ledger.RecordTransform([Delta(element, before, after, rig.D1, rig.D1)]);

        Assert.True(rig.Ledger.Undo());

        Assert.Single(notified);
        Assert.Same(element, notified[0]);
    }

    [Fact]
    public void PurgeElement_AfterTransform_StillMatchesByReference()
    {
        // R1: 변형이 인스턴스를 교체하면 ReferenceEquals 계약이 깨져 페이드 purge가 낡은 항목을 놓친다.
        var rig = Setup();
        var element = NewStroke();
        rig.D1.Add(element);
        rig.Ledger.RecordAdd(element);

        element.TransformState = new ElementTransformState(3, 0.5, 77, new Vector(9, 9));
        rig.D1.Remove(element);
        rig.Ledger.PurgeElement(element);

        Assert.Equal(0, rig.Ledger.Count);
    }

    // ---- SEL-13: DeleteSelectionOperation ----

    [Fact]
    public void Undo_DeleteSelection_RestoresAllElementsAtOriginalIndices()
    {
        var rig = Setup();
        var a = NewStroke();
        var b = NewStroke();
        var c = NewStroke();
        var d = NewStroke();
        foreach (var s in (StrokeElement[])[a, b, c, d])
        {
            rig.D1.Add(s);
        }

        // b(1)와 d(3)를 함께 삭제 — 인덱스는 제거 **전에** 오름차순으로 수집한다.
        var removed = new List<(AnnotationDocument, AnnotationElement, int)>
        {
            (rig.D1, b, rig.D1.IndexOf(b)),
            (rig.D1, d, rig.D1.IndexOf(d)),
        };
        rig.D1.Remove(b);
        rig.D1.Remove(d);
        rig.Ledger.RecordDeleteSelection(removed);
        Assert.Equal(1, rig.Ledger.Count); // 다중 삭제 = 원장 1항목 (f3)

        Assert.True(rig.Ledger.Undo());

        Assert.Equal(new AnnotationElement[] { a, b, c, d }, rig.D1.Elements);
    }

    /// <summary>
    /// 연속 인덱스 회귀 감시: 역순 삽입은 여기서 반드시 깨진다.
    /// [a,b,c,d]에서 a,b,c를 지우면 [d]가 남고, 역순(c→2, b→1, a→0) 삽입은 [a,d,b,c]를 만든다.
    /// 띄어진 인덱스(1,3)만 테스트하면 이 결함이 숨는다 — 인접 인덱스를 명시적으로 고정한다.
    /// </summary>
    [Fact]
    public void Undo_DeleteSelection_AdjacentIndices_RestoresExactOriginalOrder()
    {
        var rig = Setup();
        var a = NewStroke();
        var b = NewStroke();
        var c = NewStroke();
        var d = NewStroke();
        foreach (var s in (StrokeElement[])[a, b, c, d])
        {
            rig.D1.Add(s);
        }

        // 인덱스 0,1,2를 함께 삭제 — 제거 전에 오름차순으로 수집.
        var removed = new List<(AnnotationDocument, AnnotationElement, int)>
        {
            (rig.D1, a, 0),
            (rig.D1, b, 1),
            (rig.D1, c, 2),
        };
        rig.D1.Remove(a);
        rig.D1.Remove(b);
        rig.D1.Remove(c);
        rig.Ledger.RecordDeleteSelection(removed);
        Assert.Equal(new AnnotationElement[] { d }, rig.D1.Elements);

        Assert.True(rig.Ledger.Undo());

        Assert.Equal(new AnnotationElement[] { a, b, c, d }, rig.D1.Elements);
    }

    [Fact]
    public void Undo_DeleteSelection_AcrossDocuments_RestoresEachToItsOwnDocument()
    {
        var rig = Setup();
        var onD1 = NewStroke();
        var onD2 = NewStroke();
        rig.D1.Add(onD1);
        rig.D2.Add(onD2);

        var removed = new List<(AnnotationDocument, AnnotationElement, int)>
        {
            (rig.D1, onD1, 0),
            (rig.D2, onD2, 0),
        };
        rig.D1.Remove(onD1);
        rig.D2.Remove(onD2);
        rig.Ledger.RecordDeleteSelection(removed);

        Assert.True(rig.Ledger.Undo());

        Assert.Equal(new AnnotationElement[] { onD1 }, rig.D1.Elements);
        Assert.Equal(new AnnotationElement[] { onD2 }, rig.D2.Elements);
    }

    [Fact]
    public void RecordTransformAndDeleteSelection_WithEmptyInput_RecordNothing()
    {
        var rig = Setup();

        rig.Ledger.RecordTransform([]);
        rig.Ledger.RecordDeleteSelection([]);

        Assert.Equal(0, rig.Ledger.Count);
    }
}
