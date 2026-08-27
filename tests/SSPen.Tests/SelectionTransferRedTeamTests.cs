using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using SSPen.Interop;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// Generation 3 delta QA — red-team of the NEW <see cref="SelectionTransfer"/> seam
/// (<c>ResolveTarget</c>/<c>Execute</c>) that did not exist in the prior red-team pass.
/// Production and tests share this exact code path (per its own doc comment), so these
/// witnesses cannot be fooled by a test-local reimplementation.
///
/// Attacks: R19 ordering (Remove -&gt; rebase -&gt; Add), LD-5 suppression scope, duplicate
/// deltas, empty delta list, a delta whose AfterOwner is absent from the surfaces list,
/// self-transfer no-op (target == source), and monitor-boundary half-open edge probing.
/// </summary>
public class SelectionTransferRedTeamTests
{
    private static StrokeElement NewStroke(params Point[] pts) =>
        new(pts, Colors.Black, thickness: 3, isHighlighter: false);

    private static readonly PhysicalRect Left = new(-1920, 0, 1920, 1080);
    private static readonly PhysicalRect Center = new(0, 0, 1920, 1080);
    private static readonly PhysicalRect Right = new(1920, 0, 1920, 1080);

    // =====================================================================
    // ResolveTarget: half-open boundary edges (Contains: px>=X && px<Right && py>=Y && py<Bottom)
    // =====================================================================

    [Fact]
    public void ResolveTarget_DropExactlyOnSeam_BelongsToRightMonitorNotLeft()
    {
        // Center monitor spans x in [0, 1920). x=1920 is Center's exclusive right edge and
        // Right monitor's inclusive left edge — the drop must resolve to Right, not Center,
        // and must not double-match both.
        var surfaces = new[]
        {
            new TransferSurface(new AnnotationDocument("Center"), Center, 1.0),
            new TransferSurface(new AnnotationDocument("Right"), Right, 1.0),
        };

        var hit = SelectionTransfer.ResolveTarget(surfaces, 1920, 500);

        Assert.NotNull(hit);
        Assert.Equal("Right", hit!.Value.Document.SurfaceId);
    }

    [Fact]
    public void ResolveTarget_DropOnePixelInsideSeam_BelongsToCenterMonitor()
    {
        var surfaces = new[]
        {
            new TransferSurface(new AnnotationDocument("Center"), Center, 1.0),
            new TransferSurface(new AnnotationDocument("Right"), Right, 1.0),
        };

        var hit = SelectionTransfer.ResolveTarget(surfaces, 1919, 500);

        Assert.NotNull(hit);
        Assert.Equal("Center", hit!.Value.Document.SurfaceId);
    }

    [Fact]
    public void ResolveTarget_DropAtOriginCorner_BelongsToCenterMonitor()
    {
        // Left/Center seam at x=0: Center's inclusive left edge.
        var surfaces = new[]
        {
            new TransferSurface(new AnnotationDocument("Left"), Left, 1.0),
            new TransferSurface(new AnnotationDocument("Center"), Center, 1.0),
        };

        var hit = SelectionTransfer.ResolveTarget(surfaces, 0, 0);

        Assert.NotNull(hit);
        Assert.Equal("Center", hit!.Value.Document.SurfaceId);
    }

    [Fact]
    public void ResolveTarget_DropOnePixelLeftOfOrigin_BelongsToLeftMonitor()
    {
        var surfaces = new[]
        {
            new TransferSurface(new AnnotationDocument("Left"), Left, 1.0),
            new TransferSurface(new AnnotationDocument("Center"), Center, 1.0),
        };

        var hit = SelectionTransfer.ResolveTarget(surfaces, -1, 0);

        Assert.NotNull(hit);
        Assert.Equal("Left", hit!.Value.Document.SurfaceId);
    }

    [Fact]
    public void ResolveTarget_DropAtExclusiveBottomEdge_BelongsToNoMonitor()
    {
        // Bottom is exclusive (py < Bottom). y=1080 is exactly Bottom for a 1080-tall monitor
        // and there is no monitor below it in this rig, so this must resolve to null (CRIT-06),
        // not silently snap to the monitor above.
        var surfaces = new[] { new TransferSurface(new AnnotationDocument("Center"), Center, 1.0) };

        var hit = SelectionTransfer.ResolveTarget(surfaces, 500, 1080);

        Assert.Null(hit);
    }

    [Fact]
    public void ResolveTarget_DropOnePixelAboveBottomEdge_BelongsToMonitor()
    {
        var surfaces = new[] { new TransferSurface(new AnnotationDocument("Center"), Center, 1.0) };

        var hit = SelectionTransfer.ResolveTarget(surfaces, 500, 1079);

        Assert.NotNull(hit);
    }

    [Fact]
    public void ResolveTarget_DropInGapBetweenMonitors_ReturnsNull()
    {
        // Deliberate gap: Right monitor starts at x=2000, not 1920 — simulates a real
        // topology where monitors are not perfectly abutted (CRIT-06 must hold here too).
        var gappedRight = new PhysicalRect(2000, 0, 1920, 1080);
        var surfaces = new[]
        {
            new TransferSurface(new AnnotationDocument("Center"), Center, 1.0),
            new TransferSurface(new AnnotationDocument("Right"), gappedRight, 1.0),
        };

        var hit = SelectionTransfer.ResolveTarget(surfaces, 1950, 500);

        Assert.Null(hit);
    }

    [Fact]
    public void ResolveTarget_EmptySurfaceList_ReturnsNull_NoCrash()
    {
        Assert.Null(SelectionTransfer.ResolveTarget([], 0, 0));
    }

    // =====================================================================
    // Execute: R19 ordering (Remove -> rebase -> Add)
    // =====================================================================

    [Fact]
    public void Execute_RebasesBeforeAdd_ElementCarriesCorrectedStateAtAddTime()
    {
        // R19: if Add happened before rebase, the element would already be in the target
        // document with the STALE (pre-rebase) TransformState at the moment ElementAdded fires.
        // We capture the state visible to an ElementAdded subscriber to prove the order.
        var source = new AnnotationDocument("Src");
        var target = new AnnotationDocument("Tgt");
        var selection = new SelectionModel();
        var element = NewStroke(new Point(0, 0));
        source.Add(element);

        var before = element.TransformState;
        // Use a 1.5x DPI ratio so rebase actually changes the scale component —
        // any state visible at Add-time must already reflect the corrected value.
        var surfaces = new[]
        {
            new TransferSurface(source, Center, 1.0),
            new TransferSurface(target, Left, 1.5),
        };

        ElementTransformState? stateAtAddTime = null;
        target.ElementAdded += e => stateAtAddTime = e.TransformState;

        var deltas = new[] { new TransformDelta(element, before, before, source, source) };
        SelectionTransfer.Execute(deltas, surfaces, surfaces[1], selection);

        Assert.NotNull(stateAtAddTime);
        // Correct order means the DPI-rebased scale (before.ScaleX * (1.0/1.5)) is already
        // in place at Add-time, not the stale pre-rebase 1.0.
        Assert.Equal(before.ScaleX * (1.0 / 1.5), stateAtAddTime!.Value.ScaleX, 1e-9);
        Assert.Equal(element.TransformState, stateAtAddTime.Value);
    }

    [Fact]
    public void Execute_ElementRemovedFromSourceBeforeAddedToTarget()
    {
        // Corollary of R19: at the instant ElementAdded fires on target, source must already
        // be empty of the element (Remove happened first).
        var source = new AnnotationDocument("Src");
        var target = new AnnotationDocument("Tgt");
        var selection = new SelectionModel();
        var element = NewStroke(new Point(0, 0));
        source.Add(element);

        var surfaces = new[]
        {
            new TransferSurface(source, Center, 1.0),
            new TransferSurface(target, Left, 1.0),
        };

        bool sourceEmptyAtAddTime = false;
        target.ElementAdded += _ => sourceEmptyAtAddTime = source.Elements.Count == 0;

        var deltas = new[]
        {
            new TransformDelta(element, element.TransformState, element.TransformState, source, source),
        };
        SelectionTransfer.Execute(deltas, surfaces, surfaces[1], selection);

        Assert.True(sourceEmptyAtAddTime, "R19: source must be emptied before target.Add fires.");
    }

    // =====================================================================
    // Execute: LD-5 suppression scope
    // =====================================================================

    [Fact]
    public void Execute_SuppressesInvalidationDuringTransfer_SelectionSurvives()
    {
        var source = new AnnotationDocument("Src");
        var target = new AnnotationDocument("Tgt");
        var selection = new SelectionModel();
        selection.AttachTo(source);
        selection.AttachTo(target);
        var element = NewStroke(new Point(0, 0));
        source.Add(element);
        selection.Set([element]);

        var surfaces = new[]
        {
            new TransferSurface(source, Center, 1.0),
            new TransferSurface(target, Left, 1.0),
        };
        var deltas = new[]
        {
            new TransformDelta(element, element.TransformState, element.TransformState, source, source),
        };

        SelectionTransfer.Execute(deltas, surfaces, surfaces[1], selection);

        Assert.True(selection.Contains(element), "SEL-AC-5: transfer must not drop selection.");
        Assert.Contains(element, target.Elements);
    }

    [Fact]
    public void Execute_ReturnsDeltasWithUpdatedAfterOwnerAndAfterState_ForLedgerRecording()
    {
        // SEL-AC-10 depends on the returned deltas carrying the NEW owner, since these are
        // what UndoLedger.RecordTransform stores.
        var source = new AnnotationDocument("Src");
        var target = new AnnotationDocument("Tgt");
        var selection = new SelectionModel();
        var element = NewStroke(new Point(0, 0));
        source.Add(element);
        var before = element.TransformState;

        var surfaces = new[]
        {
            new TransferSurface(source, Center, 1.0),
            new TransferSurface(target, Left, 1.0),
        };
        var deltas = new[] { new TransformDelta(element, before, before, source, source) };

        var result = SelectionTransfer.Execute(deltas, surfaces, surfaces[1], selection);

        Assert.Single(result);
        Assert.Same(target, result[0].AfterOwner);
        Assert.Same(source, result[0].BeforeOwner); // Before-side unchanged — undo must return here.
        Assert.Equal(element.TransformState, result[0].After);
    }

    // =====================================================================
    // Execute: duplicate deltas
    // =====================================================================

    [Fact]
    public void Execute_DuplicateElementInDeltas_TransfersExactlyOnce_NoThrow()
    {
        var source = new AnnotationDocument("Src");
        var target = new AnnotationDocument("Tgt");
        var selection = new SelectionModel();
        var element = NewStroke(new Point(0, 0));
        source.Add(element);

        var surfaces = new[]
        {
            new TransferSurface(source, Center, 1.0),
            new TransferSurface(target, Left, 1.0),
        };
        // Same element appears twice — pathological caller input (leader confirmed this is
        // now handled last-write-wins + HashSet de-dup, not thrown).
        var deltas = new[]
        {
            new TransformDelta(element, element.TransformState, element.TransformState, source, source),
            new TransformDelta(element, element.TransformState, element.TransformState, source, source),
        };

        var result = SelectionTransfer.Execute(deltas, surfaces, surfaces[1], selection);

        Assert.Single(result);
        Assert.Single(target.Elements);
        Assert.Empty(source.Elements);
    }

    [Fact]
    public void Execute_DuplicateElementInDeltas_LastWriteWins_UsesLastDeltasAfterValue()
    {
        // Duplicate deltas for the same element with DIFFERENT After values — verifies the
        // documented "last write wins" contract rather than an arbitrary/first-seen pick.
        var source = new AnnotationDocument("Src");
        var target = new AnnotationDocument("Tgt");
        var selection = new SelectionModel();
        var element = NewStroke(new Point(0, 0));
        source.Add(element);
        var before = element.TransformState;
        var firstAfter = TransformMath.Translate(before, new Vector(10, 0));
        var lastAfter = TransformMath.Translate(before, new Vector(999, 0));

        var surfaces = new[]
        {
            new TransferSurface(source, Center, 1.0),
            new TransferSurface(target, Left, 1.0),
        };
        var deltas = new[]
        {
            new TransformDelta(element, before, firstAfter, source, source),
            new TransformDelta(element, before, lastAfter, source, source),
        };

        var result = SelectionTransfer.Execute(deltas, surfaces, surfaces[1], selection);

        Assert.Single(result);

        // Assert against the EXACT expected rebase of each candidate. Comparing a pre-rebase
        // value against a post-rebase result cannot discriminate: the transfer adds a 1920px
        // origin offset, so a loose NotEqual passes under BOTH last-write-wins and
        // first-write-wins and therefore pins nothing.
        var expectedLast = SelectionOperations.RebaseState(
            lastAfter, element.LocalBounds, Center, 1.0, Left, 1.0);
        var expectedFirst = SelectionOperations.RebaseState(
            firstAfter, element.LocalBounds, Center, 1.0, Left, 1.0);

        Assert.Equal(expectedLast.Translation.X, element.TransformState.Translation.X, 1e-6);
        Assert.NotEqual(expectedFirst.Translation.X, element.TransformState.Translation.X, 1e-6);
        Assert.Equal(expectedLast.Translation.X, result[0].After.Translation.X, 1e-6);
    }

    // =====================================================================
    // Execute: empty delta list
    // =====================================================================

    [Fact]
    public void Execute_EmptyDeltaList_ReturnsEmpty_NoCrash()
    {
        var source = new AnnotationDocument("Src");
        var target = new AnnotationDocument("Tgt");
        var selection = new SelectionModel();
        var surfaces = new[]
        {
            new TransferSurface(source, Center, 1.0),
            new TransferSurface(target, Left, 1.0),
        };

        var result = SelectionTransfer.Execute([], surfaces, surfaces[1], selection);

        Assert.Empty(result);
    }

    // =====================================================================
    // Execute: delta whose AfterOwner is absent from the surfaces list
    // =====================================================================

    [Fact]
    public void Execute_DeltaOwnerNotInSurfacesList_TreatedAsNoTransfer_PassesThroughUnchanged()
    {
        // FindSurface returns null when AfterOwner isn't in `surfaces` — Execute's guard
        // `source is not { } from` routes this to "no transfer" (delta passed through as-is),
        // NOT a crash and NOT a silent phantom transfer.
        var orphanDocument = new AnnotationDocument("NotInSurfacesList");
        var target = new AnnotationDocument("Tgt");
        var selection = new SelectionModel();
        var element = NewStroke(new Point(0, 0));
        orphanDocument.Add(element);
        var before = element.TransformState;

        // Deliberately omit orphanDocument's surface from the list.
        var surfaces = new[] { new TransferSurface(target, Left, 1.0) };
        var deltas = new[] { new TransformDelta(element, before, before, orphanDocument, orphanDocument) };

        var result = SelectionTransfer.Execute(deltas, surfaces, surfaces[0], selection);

        Assert.Single(result);
        // Passed through unchanged — element must NOT have been silently moved into target.
        Assert.Equal(orphanDocument, result[0].AfterOwner);
        Assert.DoesNotContain(element, target.Elements);
        Assert.Contains(element, orphanDocument.Elements);
        Assert.Equal(before, element.TransformState); // Untouched — no partial rebase applied.
    }

    // =====================================================================
    // Execute: target == source (self-transfer no-op)
    // =====================================================================

    [Fact]
    public void Execute_TargetEqualsSource_IsNoOp_NoRebaseApplied()
    {
        var doc = new AnnotationDocument("M1");
        var selection = new SelectionModel();
        var element = NewStroke(new Point(0, 0));
        doc.Add(element);
        var before = element.TransformState;

        // Same-monitor "transfer" (e.g. drag ends within the originating monitor).
        var surface = new TransferSurface(doc, Center, 1.0);
        var deltas = new[] { new TransformDelta(element, before, before, doc, doc) };

        var result = SelectionTransfer.Execute(deltas, [surface], surface, selection);

        Assert.Single(result);
        Assert.Equal(before, element.TransformState); // No rebase applied — same DPI, same monitor.
        Assert.Same(doc, result[0].AfterOwner);
        Assert.Single(doc.Elements); // Not removed-then-readded; still the same instance in place.
        Assert.Same(element, doc.Elements[0]);
    }

    [Fact]
    public void Execute_TargetEqualsSource_DoesNotRaiseElementRemovedOrAdded()
    {
        // Stronger form of the no-op contract: self-transfer must not even touch the
        // Remove/Add event channel (which would otherwise spuriously invoke the suppression
        // scope, decoration rebuilds, etc. for zero actual state change).
        var doc = new AnnotationDocument("M1");
        var selection = new SelectionModel();
        var element = NewStroke(new Point(0, 0));
        doc.Add(element);

        // Subscribe AFTER the setup Add(), so these counters only observe what Execute does.
        int removedCount = 0;
        int addedCount = 0;
        doc.ElementRemoved += _ => removedCount++;
        doc.ElementAdded += _ => addedCount++;

        var surface = new TransferSurface(doc, Center, 1.0);
        var deltas = new[]
        {
            new TransformDelta(element, element.TransformState, element.TransformState, doc, doc),
        };

        SelectionTransfer.Execute(deltas, [surface], surface, selection);

        Assert.Equal(0, removedCount);
        Assert.Equal(0, addedCount);
    }

    // =====================================================================
    // Multi-document PlanTransferOrder semantics (new signature)
    // =====================================================================

    [Fact]
    public void PlanTransferOrder_ThreeDocuments_OrdersEachElementByOwnDocumentIndex()
    {
        var d1 = new AnnotationDocument("M1");
        var d2 = new AnnotationDocument("M2");
        var d3 = new AnnotationDocument("M3");
        var a1 = NewStroke(new Point(0, 0));
        var b1 = NewStroke(new Point(1, 1));
        var a2 = NewStroke(new Point(2, 2));
        var a3 = NewStroke(new Point(3, 3));
        d1.Add(a1);
        d1.Add(b1);
        d2.Add(a2);
        d3.Add(a3);

        var deltas = new[]
        {
            Delta(a1, d1), Delta(b1, d1), Delta(a2, d2), Delta(a3, d3),
        };

        // Selection order deliberately scrambled and cross-document.
        var order = SelectionOperations.PlanTransferOrder([a3, b1, a2, a1], deltas);

        Assert.Equal(4, order.Count);
        // Index-0 elements from their respective documents (a1, a2, a3) should all precede
        // the index-1 element (b1), with input-order used as the tiebreak among index-0 ties.
        int b1Position = order.ToList().IndexOf(b1);
        Assert.Equal(3, b1Position); // b1 (index 1 in d1) sorts strictly after all index-0 elements.
    }

    [Fact]
    public void PlanTransferOrder_ElementMissingFromDeltas_TreatedAsMissingOwner_SortsLast()
    {
        var d1 = new AnnotationDocument("M1");
        var present = NewStroke(new Point(0, 0));
        var notInDeltas = NewStroke(new Point(1, 1)); // Selected but has no corresponding delta.
        d1.Add(present);
        d1.Add(notInDeltas);

        var deltas = new[] { Delta(present, d1) }; // notInDeltas deliberately omitted.

        var order = SelectionOperations.PlanTransferOrder([notInDeltas, present], deltas);

        Assert.Equal(new AnnotationElement[] { present, notInDeltas }, order);
    }

    [Fact]
    public void PlanTransferOrder_DuplicateElementInSelectionList_AppearsTwice_StableByTiebreak()
    {
        // PlanTransferOrder does not itself de-duplicate — Execute's HashSet does that layer.
        // This pins the plan function's actual (permissive) contract so a future caller relying
        // on PlanTransferOrder alone for de-dup does not get a false sense of safety.
        var d1 = new AnnotationDocument("M1");
        var element = NewStroke(new Point(0, 0));
        d1.Add(element);
        var deltas = new[] { Delta(element, d1) };

        var order = SelectionOperations.PlanTransferOrder([element, element], deltas);

        Assert.Equal(2, order.Count);
        Assert.All(order, e => Assert.Same(element, e));
    }

    [Fact]
    public void PlanTransferOrder_EmptySelection_ReturnsEmpty()
    {
        Assert.Empty(SelectionOperations.PlanTransferOrder([], []));
    }

    [Fact]
    public void PlanTransferOrder_EmptyDeltas_AllElementsTreatedAsMissingOwner_StableOrder()
    {
        var a = NewStroke(new Point(0, 0));
        var b = NewStroke(new Point(1, 1));

        var order = SelectionOperations.PlanTransferOrder([b, a], []);

        // No deltas means no owner info for anyone -> all tie at int.MaxValue -> falls back
        // to original input order (stable sort).
        Assert.Equal(new AnnotationElement[] { b, a }, order);
    }

    private static TransformDelta Delta(AnnotationElement element, AnnotationDocument owner) =>
        new(element, element.TransformState, element.TransformState, owner, owner);

    // =====================================================================
    // R18 mixed-DPI RebaseState re-confirmation (now invoked from SelectionTransfer, not AppController)
    // =====================================================================

    [Fact]
    public void Execute_AppliesDpiRebase_WhenTransferringAcrossDifferentDpiSurfaces()
    {
        // End-to-end through the new seam: 100% -> 150% must scale by the ratio, exactly as
        // the previously-verified RebaseState unit witnesses predict — but now exercised via
        // the actual production entry point rather than calling RebaseState directly.
        var source = new AnnotationDocument("Src");
        var target = new AnnotationDocument("Tgt");
        var selection = new SelectionModel();
        var element = NewStroke(new Point(0, 0), new Point(100, 0));
        source.Add(element);
        element.TransformState = new ElementTransformState(2, 3, 20, new Vector(5, -5));
        var before = element.TransformState;

        var surfaces = new[]
        {
            new TransferSurface(source, Center, 1.0),
            new TransferSurface(target, Left, 1.5),
        };
        var deltas = new[] { new TransformDelta(element, before, before, source, source) };

        SelectionTransfer.Execute(deltas, surfaces, surfaces[1], selection);

        double ratio = 1.0 / 1.5;
        Assert.Equal(before.ScaleX * ratio, element.TransformState.ScaleX, 1e-9);
        Assert.Equal(before.ScaleY * ratio, element.TransformState.ScaleY, 1e-9);
        Assert.Equal(before.AngleDegrees, element.TransformState.AngleDegrees, 1e-9); // Angle DPI-invariant.
    }

    [Fact]
    public void Execute_SameDpiTransfer_LeavesScaleUnchanged()
    {
        // Contrast case: r=1 must leave scale untouched, matching the physical rig's reality
        // and confirming the seam doesn't apply spurious correction when DPI matches.
        var source = new AnnotationDocument("Src");
        var target = new AnnotationDocument("Tgt");
        var selection = new SelectionModel();
        var element = NewStroke(new Point(0, 0));
        source.Add(element);
        element.TransformState = new ElementTransformState(1.7, 0.9, 55, new Vector(3, 8));
        var before = element.TransformState;

        var surfaces = new[]
        {
            new TransferSurface(source, Center, 1.0),
            new TransferSurface(target, Left, 1.0),
        };
        var deltas = new[] { new TransformDelta(element, before, before, source, source) };

        SelectionTransfer.Execute(deltas, surfaces, surfaces[1], selection);

        Assert.Equal(before.ScaleX, element.TransformState.ScaleX, 1e-9);
        Assert.Equal(before.ScaleY, element.TransformState.ScaleY, 1e-9);
        Assert.Equal(before.AngleDegrees, element.TransformState.AngleDegrees, 1e-9);
    }
}
