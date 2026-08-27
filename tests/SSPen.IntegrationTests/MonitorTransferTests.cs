using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using SSPen.Interop;
using Xunit;

namespace SSPen.IntegrationTests;

/// <summary>
/// 모니터 간 이관 실기 검증 (SEL-14, SEL-AC-5/10/18). 실제 3서피스를 띄우고 요소를 옮긴다.
///
/// 리그 한계 (R18): 3모니터가 균일 100% DPI라 <c>r = 1</c>이다. 따라서 DPI 보정 결함은
/// **여기서 절대 드러나지 않는다** — 그 방어선은 헤드리스 <c>RebaseState_*</c> 증인뿐이다.
/// </summary>
public class MonitorTransferTests
{
    private sealed record Rig(
        List<ContentSurfaceWindow> Surfaces,
        SelectionModel Selection,
        UndoLedger Ledger,
        AppState State)
    {
        public AnnotationDocument DocumentOf(int index) => Surfaces[index].Document;

        public AnnotationDocument? OwnerOf(AnnotationElement element) =>
            Surfaces.FirstOrDefault(s => s.Document.Elements.Contains(element))?.Document;

        public ContentSurfaceWindow SurfaceOwning(AnnotationDocument document) =>
            Surfaces.First(s => ReferenceEquals(s.Document, document));
    }

    private static Rig CreateRig()
    {
        var state = new AppState { ActiveTool = ToolKind.Select };
        var selection = new SelectionModel();
        var surfaces = new List<ContentSurfaceWindow>();
        var fading = new FadingInkController(new FadeSchedulerCore());

        AnnotationDocument? Owner(AnnotationElement e) =>
            surfaces.FirstOrDefault(s => s.Document.Elements.Contains(e))?.Document;

        var ledger = new UndoLedger(Owner, selection);

        foreach (var monitor in MonitorTopology.Enumerate())
        {
            var document = new AnnotationDocument(monitor.DeviceName);
            selection.AttachTo(document);
            surfaces.Add(new ContentSurfaceWindow(
                monitor, state, document, ledger, fading,
                selection, Owner, _ => 1.0,
                (deltas, _) => ledger.RecordTransform(deltas), () => { }, () => 0));
        }
        return new Rig(surfaces, selection, ledger, state);
    }

    private static StrokeElement NewStroke() =>
        new([new Point(100, 100), new Point(200, 180)], Colors.Blue, 5, isHighlighter: false);

    /// <summary>
    /// 서피스를 프로덕션과 똑같은 이관 후보 목록으로 투사 (<c>AppController.TransferSurfaces</c> 동일 형태).
    /// </summary>
    private static List<TransferSurface> Surfaces(Rig rig) =>
        [.. rig.Surfaces.Select(s => new TransferSurface(s.Document, s.Monitor.Bounds, s.DpiScale))];

    /// <summary>
    /// 이관을 **프로덕션 경로로** 수행한다. 절차를 재구현하지 않는 것이 핵심이다 —
    /// 재구현하면 프로덕션의 순서(R19)나 억제 스코프(LD-5)를 뒤집어도 이 증인들이 전부 초록불로 남는다.
    /// </summary>
    private static IReadOnlyList<TransformDelta> Transfer(
        Rig rig, AnnotationElement element, ContentSurfaceWindow target)
    {
        var owner = rig.OwnerOf(element)!;
        var delta = new TransformDelta(
            element, element.TransformState, element.TransformState, owner, owner);
        var surfaces = Surfaces(rig);
        var to = surfaces.First(s => ReferenceEquals(s.Document, target.Document));
        return SelectionTransfer.Execute([delta], surfaces, to, rig.Selection);
    }

    private static void ShowAll(Rig rig)
    {
        foreach (var surface in rig.Surfaces)
        {
            surface.Show();
        }
        StaRunner.PumpMessages();
    }

    private static void CloseAll(Rig rig)
    {
        foreach (var surface in rig.Surfaces)
        {
            surface.Close();
        }
    }

    [Fact]
    public void Transfer_ElementDroppedOnOtherMonitor_PreservesId() => StaRunner.Run(() =>
    {
        var rig = CreateRig();
        try
        {
            ShowAll(rig);
            var element = NewStroke();
            long id = element.Id;
            rig.DocumentOf(0).Add(element);

            Transfer(rig, element, rig.Surfaces[1]);
            StaRunner.PumpMessages();

            // SEL-ARCH-1: 인스턴스 교체 금지 — 같은 참조, 같은 Id.
            Assert.Same(element, rig.DocumentOf(1).Elements[0]);
            Assert.Equal(id, rig.DocumentOf(1).Elements[0].Id);
            Assert.Empty(rig.DocumentOf(0).Elements);
        }
        finally
        {
            CloseAll(rig);
        }
    });

    /// <summary>R2: 이관은 반드시 Remove→Add 공개 경로를 타야 원본 창에 유령 시각물이 남지 않는다.</summary>
    [Fact]
    public void Transfer_LeavesNoOrphanVisualOnSource() => StaRunner.Run(() =>
    {
        var rig = CreateRig();
        try
        {
            ShowAll(rig);
            var element = NewStroke();
            rig.DocumentOf(0).Add(element);
            StaRunner.PumpMessages();

            var sourceInk = InkCanvasOf(rig.Surfaces[0]);
            var targetInk = InkCanvasOf(rig.Surfaces[1]);
            Assert.Single(sourceInk.Children);

            Transfer(rig, element, rig.Surfaces[1]);
            StaRunner.PumpMessages();

            Assert.Empty(sourceInk.Children);
            Assert.Single(targetInk.Children);
        }
        finally
        {
            CloseAll(rig);
        }
    });

    /// <summary>LD-5: 억제 스코프가 없으면 Remove의 ElementRemoved가 선택을 통째로 비운다 (SEL-AC-5).</summary>
    [Fact]
    public void Transfer_RemoveThenAdd_KeepsSelection() => StaRunner.Run(() =>
    {
        var rig = CreateRig();
        try
        {
            ShowAll(rig);
            var element = NewStroke();
            rig.DocumentOf(0).Add(element);
            rig.Selection.Set([element]);

            Transfer(rig, element, rig.Surfaces[1]);
            StaRunner.PumpMessages();

            Assert.True(rig.Selection.Contains(element), "이관 도중 선택이 유지되어야 한다 (SEL-AC-5).");
        }
        finally
        {
            CloseAll(rig);
        }
    });

    /// <summary>
    /// LD-5 **반대 방향**: 억제가 과잉 적용되면 진짜 제거(지우개·페이드)에서도 선택이 남아
    /// 댕글링 참조가 된다 (R17/R22).
    /// </summary>
    [Fact]
    public void RealRemoval_OutsideSuppressionScope_DropsFromSelection() => StaRunner.Run(() =>
    {
        var rig = CreateRig();
        try
        {
            ShowAll(rig);
            var element = NewStroke();
            rig.DocumentOf(0).Add(element);
            rig.Selection.Set([element]);

            rig.DocumentOf(0).Remove(element); // 억제 스코프 **밖**의 진짜 제거.
            StaRunner.PumpMessages();

            Assert.False(rig.Selection.Contains(element), "진짜 제거는 선택에서 떨어져야 한다 (R17).");
        }
        finally
        {
            CloseAll(rig);
        }
    });

    [Fact]
    public void Transfer_RotatedElement_PreservesRotationAndScale() => StaRunner.Run(() =>
    {
        var rig = CreateRig();
        try
        {
            ShowAll(rig);
            var element = NewStroke();
            element.TransformState = new ElementTransformState(2.5, 1.5, 37, new Vector(20, -10));
            rig.DocumentOf(0).Add(element);

            Transfer(rig, element, rig.Surfaces[2]);
            StaRunner.PumpMessages();

            // 균일 DPI 리그(r=1)이므로 스케일·회전이 그대로 보존된다.
            Assert.Equal(2.5, element.TransformState.ScaleX, 9);
            Assert.Equal(1.5, element.TransformState.ScaleY, 9);
            Assert.Equal(37, element.TransformState.AngleDegrees, 9);
        }
        finally
        {
            CloseAll(rig);
        }
    });

    /// <summary>
    /// R19: Add가 <c>ElementAdded</c> → <c>BuildVisual</c>을 동기 호출하므로 보정은 **Add 이전**이어야 한다.
    /// Add 이후 보정하면 새 시각물이 낡은 행렬로 굳고 갱신할 후속 이벤트가 없다.
    /// </summary>
    [Fact]
    public void Transfer_RebasesBeforeAdd_VisualUsesCorrectedState() => StaRunner.Run(() =>
    {
        var rig = CreateRig();
        try
        {
            ShowAll(rig);
            var element = NewStroke();
            element.TransformState = ElementTransformState.Identity with { Translation = new Vector(33, 21) };
            rig.DocumentOf(0).Add(element);

            Transfer(rig, element, rig.Surfaces[1]);
            StaRunner.PumpMessages();

            var visual = (FrameworkElement)InkCanvasOf(rig.Surfaces[1]).Children[0];
            var actual = ((MatrixTransform)visual.RenderTransform).Matrix;

            // 새 시각물의 행렬이 **보정된 현재 상태**와 일치해야 한다.
            Assert.Equal(AnnotationVisualFactory.RenderMatrixFor(element), actual);
        }
        finally
        {
            CloseAll(rig);
        }
    });

    /// <summary>SEL-AC-18: 여러 요소를 옮겨도 원본 상대 z순서가 대상 최상단에 그대로 재현된다.</summary>
    [Fact]
    public void Transfer_MultipleElements_PreservesRelativeOrderOnTarget() => StaRunner.Run(() =>
    {
        var rig = CreateRig();
        try
        {
            ShowAll(rig);
            var bottom = NewStroke();
            var middle = NewStroke();
            var top = NewStroke();
            foreach (var s in (StrokeElement[])[bottom, middle, top])
            {
                rig.DocumentOf(0).Add(s);
            }
            var existing = NewStroke();
            rig.DocumentOf(1).Add(existing);

            // 선택 순서를 z순서와 반대로 줘도 프로덕션 경로가 원본 인덱스 오름차순으로 정규화한다.
            var source = rig.DocumentOf(0);
            var deltas = new[] { top, bottom, middle }
                .Select(e => new TransformDelta(
                    e, e.TransformState, e.TransformState, source, source))
                .ToList();
            var surfaces = Surfaces(rig);
            var to = surfaces.First(s => ReferenceEquals(s.Document, rig.DocumentOf(1)));
            SelectionTransfer.Execute(deltas, surfaces, to, rig.Selection);
            StaRunner.PumpMessages();

            Assert.Equal(
                new AnnotationElement[] { existing, bottom, middle, top },
                rig.DocumentOf(1).Elements);
        }
        finally
        {
            CloseAll(rig);
        }
    });

    /// <summary>SEL-AC-10: 이관 undo는 상태와 **소유권**을 함께 되돌리고 선택을 유지한다.</summary>
    [Fact]
    public void Undo_AfterCrossMonitorMove_RestoresOriginalSurface() => StaRunner.Run(() =>
    {
        var rig = CreateRig();
        try
        {
            ShowAll(rig);
            var element = NewStroke();
            rig.DocumentOf(0).Add(element);
            rig.Selection.Set([element]);

            var before = element.TransformState;
            Transfer(rig, element, rig.Surfaces[1]);
            rig.Ledger.RecordTransform(
            [
                new TransformDelta(
                    element, before, element.TransformState, rig.DocumentOf(0), rig.DocumentOf(1)),
            ]);
            StaRunner.PumpMessages();
            Assert.Contains(element, rig.DocumentOf(1).Elements);

            Assert.True(rig.Ledger.Undo());
            StaRunner.PumpMessages();

            Assert.Contains(element, rig.DocumentOf(0).Elements);
            Assert.DoesNotContain(element, rig.DocumentOf(1).Elements);
            Assert.Equal(before, element.TransformState);
            Assert.True(rig.Selection.Contains(element), "이관 undo에서 선택이 유지되어야 한다 (SEL-AC-10).");

            // 시각물도 원본 서피스로 돌아와야 한다 (R2/R15).
            Assert.Single(InkCanvasOf(rig.Surfaces[0]).Children);
            Assert.Empty(InkCanvasOf(rig.Surfaces[1]).Children);
        }
        finally
        {
            CloseAll(rig);
        }
    });

    /// <summary>
    /// CRIT-06: 어느 모니터에도 걸치지 않은 지점에 놓으면 이관하지 않고 원본을 유지한다.
    /// 판정 자체를 **프로덕션 함수**(<c>SelectionTransfer.ResolveTarget</c>)에 물어본다 —
    /// 테스트가 직접 Contains를 돌면 프로덕션 분기를 뒤집어도 초록불로 남는 동어반복이 된다.
    /// </summary>
    [Fact]
    public void DropOutsideAllMonitors_KeepsOriginalOwner() => StaRunner.Run(() =>
    {
        var rig = CreateRig();
        try
        {
            ShowAll(rig);
            var element = NewStroke();
            rig.DocumentOf(0).Add(element);
            rig.Selection.Set([element]);
            var surfaces = Surfaces(rig);

            // 가상 스크린 밖 지점 — 모니터 사이 공백이나 화면 밖으로 놓은 경우.
            var virtualScreen = MonitorTopology.VirtualScreen();
            var target = SelectionTransfer.ResolveTarget(
                surfaces, virtualScreen.Right + 500, virtualScreen.Bottom + 500);

            Assert.Null(target);

            // 대상이 없으면 프로덕션은 Execute를 아예 부르지 않고 원본 델타를 그대로 원장에 싣는다.
            Assert.Contains(element, rig.DocumentOf(0).Elements);
            Assert.True(rig.Selection.Contains(element));

            // 그리고 화면 안 지점은 반드시 대상을 찾아야 한다 — 판정이 항상 null이 아님을 못박는다.
            var inside = rig.Surfaces[1].Monitor.Bounds;
            var found = SelectionTransfer.ResolveTarget(
                surfaces, inside.X + inside.Width / 2, inside.Y + inside.Height / 2);
            Assert.NotNull(found);
            Assert.Same(rig.DocumentOf(1), found!.Value.Document);
        }
        finally
        {
            CloseAll(rig);
        }
    });

    private static System.Windows.Controls.Canvas InkCanvasOf(ContentSurfaceWindow surface) =>
        (System.Windows.Controls.Canvas)((System.Windows.Controls.Grid)surface.Content).Children[1];
}
