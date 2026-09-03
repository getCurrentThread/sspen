using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using SSPen.Interop;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="LedgerCommands"/>의 증인 (47단계 — R5, R7(c), SEL-12/SEL-13, f3, CRIT-06). 합성 루트가 갖고 있던 여섯 원장 명령을
/// 창·핀·서피스 없이 고정한다: 플러시가 원장 조작보다 <b>앞</b>인 순서, 전체 지우기·선택 삭제가 원장 1항목인 것, 클릭 통과 전환이
/// 제스처에만 매달리고 <c>SelectionChanged</c>에는 매달리지 않는 것(R5), 놓은 지점 유무에 따른 이관 판정과 실행취소의 소유권 복원.
/// </summary>
public class LedgerCommandsTests
{
    private sealed class Rig
    {
        public AppState State { get; } = new();
        public SelectionModel Selection { get; } = new();
        public List<AnnotationDocument> Documents { get; } = [];
        public UndoLedger Ledger { get; }
        public List<string> Trace { get; } = [];
        public List<TransferSurface> Surfaces { get; } = [];
        public int TransferQueries { get; private set; }
        public LedgerCommands Commands { get; }

        public Rig()
        {
            State.ActiveTool = ToolKind.Select;
            Selection.AttachTo(State); // 프로덕션과 같은 배선: ClickThrough=true → 도구 None → 선택 해제 (SEL-B-4)
            for (int i = 0; i < 2; i++)
            {
                var document = new AnnotationDocument($"D{i}");
                document.ElementRemoved += _ => Trace.Add($"removed:{document.SurfaceId}");
                Selection.AttachTo(document);
                Documents.Add(document);
            }
            Ledger = new UndoLedger(OwnerOf, Selection);
            Commands = new LedgerCommands(
                State, Selection, Ledger,
                documents: () => Documents,
                ownerOf: OwnerOf,
                flushPendingTransforms: () => Trace.Add("flush"),
                transferSurfaces: () =>
                {
                    TransferQueries++;
                    return Surfaces;
                },
                closePins: () => Trace.Add("close-pins"));
        }

        public AnnotationDocument? OwnerOf(AnnotationElement element) =>
            Documents.FirstOrDefault(d => d.Elements.Contains(element));

        public StrokeElement AddStroke(int documentIndex, double x, double y)
        {
            var stroke = new StrokeElement([new Point(x, y), new Point(x + 10, y)], Colors.Red, 2, isHighlighter: false);
            Documents[documentIndex].Add(stroke);
            Ledger.RecordAdd(stroke);
            return stroke;
        }
    }

    // ---- Undo ----

    [Fact]
    public void Undo_FlushesBeforeLedgerUndo()
    {
        var r = new Rig();
        r.AddStroke(0, 10, 10);

        r.Commands.Undo();

        Assert.Equal(["flush", "removed:D0"], r.Trace);
        Assert.Empty(r.Documents[0].Elements);
    }

    [Fact]
    public void Undo_EmptyLedger_StillFlushes()
    {
        var r = new Rig();

        r.Commands.Undo();

        Assert.Equal(["flush"], r.Trace);
    }

    // ---- ClearAll ----

    [Fact]
    public void ClearAll_FlushesFirst_ClearsEveryDocument_OneEntry_ClearsSelection_ClosesPinsLast()
    {
        var r = new Rig();
        var a = r.AddStroke(0, 10, 10);
        var b = r.AddStroke(1, 20, 20);
        r.Selection.Set([a]);

        r.Commands.ClearAll();

        Assert.Equal("flush", r.Trace[0]);
        Assert.Equal("close-pins", r.Trace[^1]);
        Assert.All(r.Documents, d => Assert.Empty(d.Elements));
        Assert.Empty(r.Selection.Elements);
        Assert.False(r.State.ClickThrough); // 전체 지우기는 해제 제스처가 아니다 (R5의 세 경로에 없다)

        Assert.True(r.Ledger.Undo()); // 1항목 — 실행취소 1번으로 두 문서 모두 복원
        Assert.Same(a, Assert.Single(r.Documents[0].Elements));
        Assert.Same(b, Assert.Single(r.Documents[1].Elements));
    }

    [Fact]
    public void ClearAll_NothingToClear_StillFlushesAndClosesPins_Today()
    {
        var r = new Rig();

        r.Commands.ClearAll();

        Assert.Equal(["flush", "close-pins"], r.Trace);
    }

    // ---- DeleteSelection ----

    [Fact]
    public void DeleteSelection_FlushesFirst_RemovesAcrossDocuments_OneEntry_ThenEngagesClickThrough()
    {
        var r = new Rig();
        var a = r.AddStroke(0, 10, 10);
        var b = r.AddStroke(1, 20, 20);
        var c = r.AddStroke(0, 30, 30);
        r.Selection.Set([a, b]);

        r.Commands.DeleteSelection();

        Assert.Equal("flush", r.Trace[0]);
        Assert.Contains("removed:D0", r.Trace);
        Assert.Contains("removed:D1", r.Trace);
        Assert.Same(c, Assert.Single(r.Documents[0].Elements));
        Assert.Empty(r.Documents[1].Elements);
        Assert.True(r.State.ClickThrough);       // 삭제 완료는 명시적 해제 제스처다 (R5)
        Assert.Equal(ToolKind.None, r.State.ActiveTool);
        Assert.Empty(r.Selection.Elements);

        Assert.True(r.Ledger.Undo());            // 1항목 — 원래 자리(인덱스)로 복원
        Assert.Equal([a, c], r.Documents[0].Elements);
        Assert.Same(b, Assert.Single(r.Documents[1].Elements));
    }

    [Fact]
    public void DeleteSelection_EmptySelection_FlushesOnly_NoClickThrough_Today()
    {
        var r = new Rig();
        r.AddStroke(0, 10, 10);

        r.Commands.DeleteSelection();

        Assert.Equal(["flush"], r.Trace);
        Assert.False(r.State.ClickThrough);
        Assert.Single(r.Documents[0].Elements);
    }

    // ---- 클릭 통과 전환 (R5) ----

    [Fact]
    public void EngageClickThrough_ClearsSelection_SetsClickThrough_DropsTool()
    {
        var r = new Rig();
        r.Selection.Set([r.AddStroke(0, 10, 10)]);

        r.Commands.EngageClickThrough();

        Assert.Empty(r.Selection.Elements);
        Assert.True(r.State.ClickThrough);
        Assert.Equal(ToolKind.None, r.State.ActiveTool);
        Assert.Empty(r.Trace); // 원장을 건드리지 않는다 — 플러시 없음
    }

    [Fact]
    public void ClearSelectionByEscape_EmptySelection_DoesNothing()
    {
        var r = new Rig();

        r.Commands.ClearSelectionByEscape();

        Assert.False(r.State.ClickThrough);
        Assert.Equal(ToolKind.Select, r.State.ActiveTool);
    }

    [Fact]
    public void ClearSelectionByEscape_WithSelection_Engages()
    {
        var r = new Rig();
        r.Selection.Set([r.AddStroke(0, 10, 10)]);

        r.Commands.ClearSelectionByEscape();

        Assert.True(r.State.ClickThrough);
        Assert.Empty(r.Selection.Elements);
    }

    /// <summary>R5: 선택이 비는 것 자체는 해제 제스처가 아니다 — 도구 전환 경로에서 클릭 통과가 켜지면 펜 버튼이 곧바로 풀린다.</summary>
    [Fact]
    public void SelectionChanged_AloneNeverEngagesClickThrough()
    {
        var r = new Rig();
        var a = r.AddStroke(0, 10, 10);
        r.Selection.Set([a]);

        r.Selection.Clear();
        Assert.False(r.State.ClickThrough);

        r.Selection.Set([a]);
        r.State.ActiveTool = ToolKind.Pen; // 도구 전환 → SEL-B-4로 선택이 빈다
        Assert.Empty(r.Selection.Elements);
        Assert.False(r.State.ClickThrough);
    }

    // ---- CommitTransform ----

    private static ElementTransformState Moved(double dx, double dy) =>
        ElementTransformState.Identity with { Translation = new Vector(dx, dy) };

    private static TransformDelta Delta(AnnotationElement element, AnnotationDocument owner, ElementTransformState after)
    {
        var before = element.TransformState;
        element.TransformState = after; // 드래그 중 이미 적용된 상태에서 확정이 온다
        return new TransformDelta(element, before, after, owner, owner);
    }

    [Fact]
    public void CommitTransform_NullDrop_SkipsTransferQuery_RecordsUndoableEntry()
    {
        var r = new Rig();
        var a = r.AddStroke(0, 10, 10);
        var delta = Delta(a, r.Documents[0], Moved(5, 5));

        r.Commands.CommitTransform([delta], dropPhysical: null);

        Assert.Equal(0, r.TransferQueries);       // 크기·회전·휠은 어디에도 놓지 않는다 — 이관 판정 생략
        Assert.Empty(r.Trace);                    // 확정 자체는 플러시하지 않는다 (휠 세션이 이 경로로 들어온다)
        Assert.True(r.Ledger.Undo());
        Assert.Equal(ElementTransformState.Identity, a.TransformState);
        Assert.Same(r.Documents[0], r.OwnerOf(a));
    }

    private static void TwoSurfaces(Rig r)
    {
        r.Surfaces.Add(new TransferSurface(r.Documents[0], new PhysicalRect(0, 0, 100, 100), 1.0));
        r.Surfaces.Add(new TransferSurface(r.Documents[1], new PhysicalRect(200, 0, 100, 100), 1.0));
    }

    [Fact]
    public void CommitTransform_DropInGap_QueriesSurfacesOnce_KeepsOwner()
    {
        var r = new Rig();
        TwoSurfaces(r);
        var a = r.AddStroke(0, 10, 10);
        var delta = Delta(a, r.Documents[0], Moved(5, 5));

        r.Commands.CommitTransform([delta], dropPhysical: (150, 50)); // 두 모니터 사이 공백 (CRIT-06)

        Assert.Equal(1, r.TransferQueries);
        Assert.Same(r.Documents[0], r.OwnerOf(a));
        Assert.True(r.Ledger.Undo());
        Assert.Equal(ElementTransformState.Identity, a.TransformState);
    }

    [Fact]
    public void CommitTransform_DropOnOtherSurface_TransfersOwnership_AndUndoReturnsIt()
    {
        var r = new Rig();
        TwoSurfaces(r);
        var a = r.AddStroke(0, 10, 10);
        r.Selection.Set([a]);
        var delta = Delta(a, r.Documents[0], Moved(230, 0));

        r.Commands.CommitTransform([delta], dropPhysical: (250, 50));

        Assert.Same(r.Documents[1], r.OwnerOf(a));   // 이관됐고
        Assert.Contains(a, r.Selection.Elements);     // LD-5: 이관 중 선택은 살아남는다
        Assert.True(r.Ledger.Undo());                 // 실행된(소유권 반영) 델타가 원장에 실렸으므로
        Assert.Same(r.Documents[0], r.OwnerOf(a));    // 실행취소가 소유권까지 되돌린다 (SEL-AC-10)
        Assert.Equal(ElementTransformState.Identity, a.TransformState);
    }

    /// <summary>
    /// 되돌릴 것이 없으면 실패를 <b>돌려준다</b>. 이 값을 버리던 시절에는 사용자가 보기에
    /// 단축키를 눌러도 아무 일이 없어 고장과 무동작을 구별할 수 없었다.
    /// </summary>
    [Fact]
    public void Undo_EmptyLedger_ReportsFailure()
    {
        var r = new Rig();

        Assert.False(r.Commands.Undo());
        Assert.Contains("flush", r.Trace); // 실패해도 플러시는 선두다 (R7(c) 순서는 결과와 무관하다).
    }

    [Fact]
    public void Undo_WithAnOperation_ReportsSuccess()
    {
        var r = new Rig();
        r.AddStroke(0, 10, 10);

        Assert.True(r.Commands.Undo());
    }

    /// <summary>전체 지우기는 지운 요소 수를 돌려준다 — 셸이 무엇을 지웠는지 알릴 근거다.</summary>
    [Fact]
    public void ClearAll_ReturnsTheClearedElementCountAcrossDocuments()
    {
        var r = new Rig();
        r.AddStroke(0, 10, 10);
        r.AddStroke(0, 30, 10);
        r.AddStroke(1, 10, 10);

        Assert.Equal(3, r.Commands.ClearAll());
    }

    /// <summary>확인 대화상자는 지우기 <b>전에</b> 물어야 하므로 상태를 바꾸지 않는 조회가 따로 있다.</summary>
    [Fact]
    public void ClearableCount_DoesNotMutate_AndMatchesWhatClearAllWouldRemove()
    {
        var r = new Rig();
        r.AddStroke(0, 10, 10);
        r.AddStroke(1, 10, 10);

        int expected = r.Commands.ClearableCount();
        Assert.Equal(2, expected);
        Assert.Equal(2, r.Commands.ClearableCount()); // 두 번 물어도 값이 같다 (부작용 없음)
        Assert.Equal(expected, r.Commands.ClearAll());
        Assert.Equal(0, r.Commands.ClearableCount());
    }
}
