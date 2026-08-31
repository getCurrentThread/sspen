using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

namespace SSPen.E2ETests;

public class ShapeAndTextWorkflowE2ETests
{
    [Fact]
    public void DrawRectangleAndEllipse_CreatesElementsAndRendersVisuals() => E2EAppFixture.Run(actor =>
    {
        // 1. 사각형 그리기 (도구 선택 후 색상/굵기 지정)
        actor
            .SelectTool(ToolKind.Rectangle)
            .SetColor(Colors.Blue)
            .SetThickness(ThicknessStep.Small)
            .DrawShape(ShapeKind.Rectangle, new Point(100, 100), new Point(300, 250), monitorIndex: 1);

        // 2. 타원 그리기
        actor
            .SelectTool(ToolKind.Ellipse)
            .SetColor(Colors.Green)
            .DrawShape(ShapeKind.Ellipse, new Point(400, 100), new Point(600, 300), monitorIndex: 1);

        var doc = actor.Document(monitorIndex: 1);
        Assert.Equal(2, doc.Elements.Count);

        var rect = Assert.IsType<ShapeElement>(doc.Elements[0]);
        Assert.Equal(ShapeKind.Rectangle, rect.Kind);
        Assert.Equal(Colors.Blue, rect.Color);

        var ellipse = Assert.IsType<ShapeElement>(doc.Elements[1]);
        Assert.Equal(ShapeKind.Ellipse, ellipse.Kind);
        Assert.Equal(Colors.Green, ellipse.Color);

        // Undo 1회 시 타원 제거
        actor.Undo();
        Assert.Single(doc.Elements);
        Assert.Equal(ShapeKind.Rectangle, ((ShapeElement)doc.Elements[0]).Kind);
    });

    [Fact]
    public void TextTool_ClickAndType_CommitsTextElement() => E2EAppFixture.Run(actor =>
    {
        actor
            .SelectTool(ToolKind.Text)
            .SetColor(Colors.Purple)
            .AddText(new Point(200, 200), "Hello SSPen E2E Test", monitorIndex: 1);

        var doc = actor.Document(monitorIndex: 1);
        Assert.Single(doc.Elements);

        var textElement = Assert.IsType<TextElement>(doc.Elements[0]);
        Assert.Equal("Hello SSPen E2E Test", textElement.Text);
        Assert.Equal(Colors.Purple, textElement.Color);
        Assert.Equal(new Point(200, 200), textElement.Origin);
    });
}
