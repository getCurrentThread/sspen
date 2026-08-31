using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using SSPen.Shell;
using Xunit;

namespace SSPen.E2ETests;

public class DrawingWorkflowE2ETests
{
    [Fact]
    public void PenDrawing_CreatesStrokeVisual_AndRecordsUndo() => E2EAppFixture.Run(actor =>
    {
        var strokeColor = Colors.Red;

        actor
            .SelectTool(ToolKind.Pen)
            .SetColor(strokeColor)
            .SetThickness(ThicknessStep.Medium)
            .DrawStroke(new Point(100, 100), new Point(300, 200), monitorIndex: 1);

        var doc = actor.Document(monitorIndex: 1);
        Assert.Single(doc.Elements);

        var stroke = Assert.IsType<StrokeElement>(doc.Elements[0]);
        Assert.Equal(strokeColor, stroke.Color);
        Assert.Equal(6.0, stroke.Thickness);
        Assert.False(stroke.IsHighlighter);

        // 캔버스 시각물 확인
        var canvas = actor.Surface(monitorIndex: 1).InkCanvas;
        Assert.Single(canvas.Children);

        // 실행 취소 (Undo)
        actor.Undo();
        Assert.Empty(doc.Elements);
        Assert.Empty(canvas.Children);
    });

    [Fact]
    public void HighlighterDrawing_CreatesSemiTransparentElement() => E2EAppFixture.Run(actor =>
    {
        actor
            .SelectTool(ToolKind.Highlighter)
            .SetColor(Colors.Yellow)
            .SetThickness(ThicknessStep.Medium)
            .DrawStroke(new Point(150, 150), new Point(400, 150), monitorIndex: 1);

        var doc = actor.Document(monitorIndex: 1);
        Assert.Single(doc.Elements);

        var stroke = Assert.IsType<StrokeElement>(doc.Elements[0]);
        Assert.True(stroke.IsHighlighter);
        Assert.Equal(Colors.Yellow, stroke.Color);
        Assert.Equal(18.0, stroke.Thickness); // 6 * 3 = 18.0
    });

    [Fact]
    public void Eraser_RemovesIntersectingStroke_AndUndoRestoresIt() => E2EAppFixture.Run(actor =>
    {
        // 1. 획 생성
        actor
            .SelectTool(ToolKind.Pen)
            .DrawStroke(new Point(100, 100), new Point(300, 100), monitorIndex: 1);

        var doc = actor.Document(monitorIndex: 1);
        Assert.Single(doc.Elements);

        // 2. 지우개로 획 중간 지점 클릭 삭제
        actor.EraseAt(new Point(200, 100), monitorIndex: 1);
        Assert.Empty(doc.Elements);

        // 3. 실행 취소로 복원
        actor.Undo();
        Assert.Single(doc.Elements);
    });

    [Fact]
    public void Toolbar_ScrollWheel_CyclesActiveTool() => E2EAppFixture.Run(actor =>
    {
        var toolbar = actor.App.Toolbar;
        Assert.NotNull(toolbar);

        actor.SelectTool(ToolKind.Pen);
        Assert.Equal(ToolKind.Pen, actor.State.ActiveTool);

        // 툴바 위에서 마우스 휠 아래로 스크롤 (-120): Pen -> Highlighter
        actor.State.ActiveTool = ToolbarStateMap.NextToolByWheel(actor.State.ActiveTool, -120);
        actor.Pump();
        Assert.Equal(ToolKind.Highlighter, actor.State.ActiveTool);

        // 다시 스크롤 (-120): Highlighter -> Eraser
        actor.State.ActiveTool = ToolbarStateMap.NextToolByWheel(actor.State.ActiveTool, -120);
        actor.Pump();
        Assert.Equal(ToolKind.Eraser, actor.State.ActiveTool);

        // 위로 스크롤 (+120): Eraser -> Highlighter
        actor.State.ActiveTool = ToolbarStateMap.NextToolByWheel(actor.State.ActiveTool, 120);
        actor.Pump();
        Assert.Equal(ToolKind.Highlighter, actor.State.ActiveTool);
    });
}
