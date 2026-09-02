using System.Windows;
using SSPen.Annotation;
using Xunit;

using static SSPen.Tests.TestGeometry;

namespace SSPen.Tests;

/// <summary>
/// <see cref="UndoLedger"/> 적대적/경계 케이스 — 리팩터링 19단계에서 SelectionRedTeamTests C3·C4·C5절을
/// 글자 그대로 옮겼다 (대상 타입 1:1: 기본 계약은 <see cref="UndoLedgerTests"/>, 레드팀은 여기).
///
/// 목적은 확인이 아니라 파괴다: 이관 뒤 역순 undo의 stale reference 함정(C3), 빈 입력·no-op의 유령 원장
/// 항목(C4), 선택 억제 스코프와 undo-of-Add 경로의 상호 불가침(C5)을 찌른다.
///
/// 분류 근거(애매했던 둘): <c>DeleteSelection_ThenUndo_ThenDeleteSameElementsAgain_…</c>는
/// <c>SelectionOperations.PlanDelete</c>를 부르지만 그것은 준비 단계이고, 단언은 원장 undo가 같은 인덱스를
/// 두 번 복원하는가에 있다. <c>TransformOperationUndo_WithOwnershipChange_…</c>는 관측 대상이
/// <see cref="SelectionModel"/>이지만 찌르는 경로는 <c>UndoLedger.TransformOperation.Undo</c>의 소유권 분기다
/// (AGENTS.md가 못박은 두 억제 호출 지점 중 하나). C4 가운데 <c>PlanDelete_AllElementsOrphaned_…</c>만은
/// 원장을 만들지 않으므로 SelectionOperationsTests로 갔다.
///
/// 참조 계약: <c>.gjc/_session-.../specs/deep-interview-selection-tool.md</c> (SEL-AC-1..18),
/// <c>.../plans/ralplan/.../stage-05-revision.md</c> (R1..R24, ARCH-17/18/19/20/21).
/// </summary>
public class UndoLedgerRedTeamTests
{
    // ---- C3. Undo 후 더 오래된 조작 undo — 소유 문서가 이관으로 바뀐 뒤 stale reference 함정 ----

    [Fact]
    public void Undo_TransferThenOlderTransform_OnDifferentElement_BothResolveByCurrentOwner()
    {
        var d1 = new AnnotationDocument("M1");
        var d2 = new AnnotationDocument("M2");
        var docs = new[] { d1, d2 };
        var selection = new SelectionModel();
        var ledger = new UndoLedger(e => docs.FirstOrDefault(d => d.Elements.Contains(e)), selection);

        var older = NewStroke(new Point(0, 0));
        var moved = NewStroke(new Point(5, 5));
        d1.Add(older);
        d1.Add(moved);

        // 오래된 조작: older에 변형을 기록 (아직 이관 없음).
        var olderBefore = older.TransformState;
        var olderAfter = TransformMath.Translate(olderBefore, new Vector(1, 1));
        older.TransformState = olderAfter;
        ledger.RecordTransform([new TransformDelta(older, olderBefore, olderAfter, d1, d1)]);

        // 최신 조작: moved를 D1 → D2로 이관.
        var movedBefore = moved.TransformState;
        var movedAfter = TransformMath.Translate(movedBefore, new Vector(1920, 0));
        d1.Remove(moved);
        moved.TransformState = movedAfter;
        d2.Add(moved);
        ledger.RecordTransform([new TransformDelta(moved, movedBefore, movedAfter, d1, d2)]);

        // undo 1: 이관을 되돌린다 (moved: D2 → D1).
        Assert.True(ledger.Undo());
        Assert.Contains(moved, d1.Elements);
        Assert.DoesNotContain(moved, d2.Elements);

        // undo 2: 더 오래된 older 변형을 되돌린다. older는 이관을 겪지 않았으므로 D1에 그대로 있어야 한다.
        Assert.True(ledger.Undo());
        Assert.Equal(olderBefore, older.TransformState);
        Assert.Contains(older, d1.Elements);
    }

    [Fact]
    public void Undo_TransferTwiceAcrossThreeDocuments_ThenUndoBoth_RestoresOriginalOwnerChain()
    {
        // 요소가 M1 → M2 → M3로 두 번 이관된 뒤 두 undo로 M1까지 완전히 되돌아가야 한다.
        var d1 = new AnnotationDocument("M1");
        var d2 = new AnnotationDocument("M2");
        var d3 = new AnnotationDocument("M3");
        var docs = new[] { d1, d2, d3 };
        var selection = new SelectionModel();
        var ledger = new UndoLedger(e => docs.FirstOrDefault(d => d.Elements.Contains(e)), selection);

        var element = NewStroke(new Point(0, 0));
        d1.Add(element);

        var s0 = element.TransformState;
        var s1 = TransformMath.Translate(s0, new Vector(1920, 0));
        d1.Remove(element);
        element.TransformState = s1;
        d2.Add(element);
        ledger.RecordTransform([new TransformDelta(element, s0, s1, d1, d2)]);

        var s2 = TransformMath.Translate(s1, new Vector(1920, 0));
        d2.Remove(element);
        element.TransformState = s2;
        d3.Add(element);
        ledger.RecordTransform([new TransformDelta(element, s1, s2, d2, d3)]);

        Assert.True(ledger.Undo()); // M3 → M2
        Assert.Contains(element, d2.Elements);
        Assert.Equal(s1, element.TransformState);

        Assert.True(ledger.Undo()); // M2 → M1
        Assert.Contains(element, d1.Elements);
        Assert.Equal(s0, element.TransformState);
        Assert.DoesNotContain(element, d2.Elements);
        Assert.DoesNotContain(element, d3.Elements);
    }

    // ---- C4. 빈 입력 / no-op — 유령 원장 항목 사냥 ----

    [Fact]
    public void RecordTransform_NoActualChange_StillRecordsIfCalled_ButProductionGuardsAgainstIt()
    {
        // UndoLedger.RecordTransform 자체는 "델타가 비어 있지 않으면" 무조건 기록한다 —
        // "값이 실제로 바뀌었는지"는 검사하지 않는다. 이 계약을 명시적으로 고정한다.
        // (실사용 시 no-op 방지는 TransformCommitPlan.Build의 `before == after` 필터가 담당하며,
        //  그 증인은 TransformCommitPlanTests.Build_UnchangedElement_EmitsNothing이다 —
        //  순수 원장 계층 자체는 방어하지 않는다는 것이 여기서 확인해야 할 red-team 발견.)
        var doc = new AnnotationDocument("M1");
        var selection = new SelectionModel();
        var ledger = new UndoLedger(e => doc, selection);
        var element = NewStroke(new Point(0, 0));
        doc.Add(element);

        var same = element.TransformState; // Before == After, 실제로는 아무 변화 없음.
        ledger.RecordTransform([new TransformDelta(element, same, same, doc, doc)]);

        Assert.Equal(1, ledger.Count);

        Assert.True(ledger.Undo());
        Assert.Equal(same, element.TransformState); // 상태는 불변이었으므로 undo도 무해하다.
    }

    [Fact]
    public void RecordDeleteSelection_EmptyList_RecordsNothing_NoPhantomEntry()
    {
        var doc = new AnnotationDocument("M1");
        var selection = new SelectionModel();
        var ledger = new UndoLedger(e => doc, selection);

        ledger.RecordDeleteSelection([]);

        Assert.Equal(0, ledger.Count);
        Assert.False(ledger.Undo());
    }

    [Fact]
    public void RecordTransform_EmptyDeltaList_RecordsNothing_NoPhantomEntry()
    {
        var doc = new AnnotationDocument("M1");
        var selection = new SelectionModel();
        var ledger = new UndoLedger(e => doc, selection);

        ledger.RecordTransform([]);

        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public void DeleteSelection_ThenUndo_ThenDeleteSameElementsAgain_UndoRestoresCorrectPositionEachTime()
    {
        // 삭제→undo→재삭제 왕복 — 원장이 같은 인덱스를 두 번 재사용해도 안전한지.
        var doc = new AnnotationDocument("M1");
        var selection = new SelectionModel();
        selection.AttachTo(doc);
        var ledger = new UndoLedger(e => doc, selection);

        var a = NewStroke(new Point(0, 0));
        var b = NewStroke(new Point(1, 1));
        var c = NewStroke(new Point(2, 2));
        foreach (var s in new[] { a, b, c }) doc.Add(s);
        selection.Set([b]);

        // 1차 삭제.
        var plan1 = SelectionOperations.PlanDelete(selection.Elements, e => doc);
        foreach (var entry in plan1) doc.Remove(entry.Element);
        ledger.RecordDeleteSelection([.. plan1.Select(e => (e.Document, e.Element, e.Index))]);
        selection.Clear();

        Assert.Equal(new AnnotationElement[] { a, c }, doc.Elements);

        // undo → b 복원.
        Assert.True(ledger.Undo());
        Assert.Equal(new AnnotationElement[] { a, b, c }, doc.Elements);

        // 2차 삭제 (같은 요소, 같은 인덱스 1).
        selection.Set([b]);
        var plan2 = SelectionOperations.PlanDelete(selection.Elements, e => doc);
        foreach (var entry in plan2) doc.Remove(entry.Element);
        ledger.RecordDeleteSelection([.. plan2.Select(e => (e.Document, e.Element, e.Index))]);

        Assert.Equal(new AnnotationElement[] { a, c }, doc.Elements);
        Assert.True(ledger.Undo());
        Assert.Equal(new AnnotationElement[] { a, b, c }, doc.Elements);
    }

    // ---- C5. 선택 억제 vs undo-of-Add 억제 경로 — 서로 침범하지 않는지 ----

    [Fact]
    public void UndoOfAdd_DoesNotUseSuppressionScope_SelectionDropsElement()
    {
        // undo-of-Add(AddOperation)는 SuppressInvalidation을 쓰지 않는다 — 이것이 진짜 제거이므로
        // 선택집합에서 반드시 떨어져야 한다 (계획서: "eraser/fade/undo-of-Add must DROP it").
        var doc = new AnnotationDocument("M1");
        var selection = new SelectionModel();
        selection.AttachTo(doc);
        var ledger = new UndoLedger(e => doc, selection);

        var element = NewStroke(new Point(0, 0));
        doc.Add(element);
        ledger.RecordAdd(element);
        selection.Set([element]);

        Assert.True(ledger.Undo());

        Assert.False(selection.Contains(element), "undo-of-Add는 억제 스코프 밖 — 선택에서 떨어져야 한다.");
        Assert.Empty(doc.Elements);
    }

    [Fact]
    public void TransformOperationUndo_WithOwnershipChange_UsesSuppressionScope_SelectionSurvives()
    {
        // 대조: TransformOperation.Undo의 소유권 변경 분기는 억제 스코프를 쓰므로 선택이 살아남아야 한다.
        var d1 = new AnnotationDocument("M1");
        var d2 = new AnnotationDocument("M2");
        var docs = new[] { d1, d2 };
        var selection = new SelectionModel();
        selection.AttachTo(d1);
        selection.AttachTo(d2);
        var ledger = new UndoLedger(e => docs.FirstOrDefault(d => d.Elements.Contains(e)), selection);

        var element = NewStroke(new Point(0, 0));
        d2.Add(element);
        selection.Set([element]);

        var before = element.TransformState;
        var after = TransformMath.Translate(before, new Vector(1920, 0));
        ledger.RecordTransform([new TransformDelta(element, before, after, d1, d2)]);

        Assert.True(ledger.Undo());

        Assert.True(selection.Contains(element), "이관 undo는 억제 스코프 안 — 선택이 유지되어야 한다 (SEL-AC-10).");
        Assert.Contains(element, d1.Elements);
    }
}
