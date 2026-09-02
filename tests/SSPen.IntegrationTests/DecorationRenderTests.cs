using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using SSPen.Interop;
using Xunit;

namespace SSPen.IntegrationTests;

/// <summary>
/// 선택 장식 레이어 실기 검증 (SEL-AC-1/SEL-AC-15). 실제 <see cref="ContentSurfaceWindow"/>를 띄우고
/// 장식 시각 트리가 채워지는지, 캡처용 숨김이 선택을 건드리지 않는지 확인한다.
/// <c>Application</c>은 만들지 않는다 (LD-4/R24): 창만 띄우므로 애초에 불요하다.
/// </summary>
public class DecorationRenderTests
{
    /// <summary>경계 1 + 크기 핸들 8 + 회전 스템 1 + 회전 핸들 1 = 11.</summary>
    private const int DecorationsPerElement = 11;

    private sealed record Rig(
        ContentSurfaceWindow Surface,
        AnnotationDocument Document,
        SelectionModel Selection,
        AppState State);

    private static Rig CreateSurface()
    {
        var monitor = MonitorTopology.Enumerate()[0];
        var state = new AppState { ActiveTool = ToolKind.Select };
        var document = new AnnotationDocument(monitor.DeviceName);
        var selection = new SelectionModel();
        selection.AttachTo(document);
        var ledger = new UndoLedger(
            e => document.Elements.Contains(e) ? document : null, selection);
        var surface = new ContentSurfaceWindow(
            monitor,
            state,
            document,
            ledger,
            new FadingInkController(new FadeSchedulerCore()),
            selection,
            e => document.Elements.Contains(e) ? document : null,
            _ => 1.0,
            (deltas, _) => ledger.RecordTransform(deltas),
            () => { },
            () => 0,
            (rows, columns) => $"{rows}x{columns}");
        return new Rig(surface, document, selection, state);
    }

    private static StrokeElement NewStroke() =>
        new([new Point(100, 100), new Point(300, 250)], Colors.Red, 6, isHighlighter: false);

    /// <summary>장식 레이어는 <c>_root</c>의 마지막 자식이다 (최상단 계약).</summary>
    private static System.Windows.Controls.Canvas DecorationLayer(ContentSurfaceWindow surface)
    {
        var root = (System.Windows.Controls.Grid)surface.Content;
        return (System.Windows.Controls.Canvas)root.Children[^1];
    }

    [Fact]
    public void Select_SingleElement_PopulatesDecorationLayer() => StaRunner.Run(() =>
    {
        var rig = CreateSurface();
        try
        {
            rig.Surface.Show();
            StaRunner.PumpMessages();

            var element = NewStroke();
            rig.Document.Add(element);
            rig.Selection.Set([element]);
            StaRunner.PumpMessages();

            var layer = DecorationLayer(rig.Surface);
            Assert.Equal(DecorationsPerElement, layer.Children.Count);
        }
        finally
        {
            rig.Surface.Close();
        }
    });

    [Fact]
    public void Deselect_ClearsDecorationLayer() => StaRunner.Run(() =>
    {
        var rig = CreateSurface();
        try
        {
            rig.Surface.Show();
            StaRunner.PumpMessages();

            var element = NewStroke();
            rig.Document.Add(element);
            rig.Selection.Set([element]);
            StaRunner.PumpMessages();
            Assert.NotEmpty(DecorationLayer(rig.Surface).Children);

            rig.Selection.Clear();
            StaRunner.PumpMessages();

            Assert.Empty(DecorationLayer(rig.Surface).Children);
        }
        finally
        {
            rig.Surface.Close();
        }
    });

    /// <summary>
    /// R15 핵심 증인: 모델만 되돌리고 시각물을 갱신하지 않으면 헤드리스 테스트는 초록불인데 화면이 틀린다.
    /// 여기서는 **시각물의 <c>RenderTransform</c>**을 직접 어서트한다.
    /// </summary>
    [Fact]
    public void Undo_AfterTransform_RestoresVisualRenderTransform() => StaRunner.Run(() =>
    {
        var rig = CreateSurface();
        try
        {
            rig.Surface.Show();
            StaRunner.PumpMessages();

            var element = NewStroke();
            rig.Document.Add(element);
            StaRunner.PumpMessages();

            var ink = (System.Windows.Controls.Canvas)
                ((System.Windows.Controls.Grid)rig.Surface.Content).Children[1];
            var visual = (FrameworkElement)ink.Children[0];
            var identity = ((MatrixTransform)visual.RenderTransform).Matrix;

            // 변형 커밋: 이동 + 확대. 알림은 원장의 공개 경로로 흔러가게 둔다 —
            // RaiseElementTransformChanged는 internal(ARCH-01)이며 그 경계를 허물지 않는다.
            var before = element.TransformState;
            var after = new ElementTransformState(2, 2, 0, new Vector(120, 60));
            var ledger = new UndoLedger(
                e => rig.Document.Elements.Contains(e) ? rig.Document : null, rig.Selection);

            // 먼저 역방향으로 1회 undo해 변형을 적용시킨다 (Before=after, After=before).
            element.TransformState = before;
            ledger.RecordTransform([new TransformDelta(element, after, before, rig.Document, rig.Document)]);
            Assert.True(ledger.Undo());
            StaRunner.PumpMessages();

            var moved = ((MatrixTransform)visual.RenderTransform).Matrix;
            Assert.Equal(after, element.TransformState);
            Assert.NotEqual(identity, moved);

            // 다시 undo → 시각물이 원래 행렬로 되돌아와야 한다 (R15).
            ledger.RecordTransform([new TransformDelta(element, before, after, rig.Document, rig.Document)]);
            Assert.True(ledger.Undo());
            StaRunner.PumpMessages();

            Assert.Equal(before, element.TransformState);
            Assert.Equal(identity, ((MatrixTransform)visual.RenderTransform).Matrix);
        }
        finally
        {
            rig.Surface.Close();
        }
    });

    /// <summary>SEL-AC-15: 캡처용 장식 숨김/복원 왕복이 선택집합을 건드리지 않는다.</summary>
    [Fact]
    public void SetDecorationsVisible_RoundTrip_LeavesSelectionUnchanged() => StaRunner.Run(() =>
    {
        var rig = CreateSurface();
        try
        {
            rig.Surface.Show();
            StaRunner.PumpMessages();

            var element = NewStroke();
            rig.Document.Add(element);
            rig.Selection.Set([element]);
            StaRunner.PumpMessages();

            var layer = DecorationLayer(rig.Surface);

            rig.Surface.SetDecorationsVisible(false);
            StaRunner.PumpMessages();
            Assert.Equal(Visibility.Collapsed, layer.Visibility);
            Assert.True(rig.Selection.Contains(element), "캡처 숨김이 선택을 해제하면 안 된다 (SEL-AC-15).");

            rig.Surface.SetDecorationsVisible(true);
            StaRunner.PumpMessages();
            Assert.Equal(Visibility.Visible, layer.Visibility);
            Assert.True(rig.Selection.Contains(element));
            Assert.Equal(DecorationsPerElement, layer.Children.Count);
        }
        finally
        {
            rig.Surface.Close();
        }
    });
}
