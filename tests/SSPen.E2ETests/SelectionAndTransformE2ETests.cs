using System.Windows;
using SSPen.Annotation;
using Xunit;

namespace SSPen.E2ETests;

public class SelectionAndTransformE2ETests
{
    [Fact]
    public void SelectAndMove_UpdatesTransformStateAndRenderMatrix() => E2EAppFixture.Run(actor =>
    {
        // 1. 획 생성
        actor
            .SelectTool(ToolKind.Pen)
            .DrawStroke(new Point(100, 100), new Point(200, 100), monitorIndex: 1);

        var doc = actor.Document(monitorIndex: 1);
        Assert.Single(doc.Elements);
        var element = doc.Elements[0];

        // 2. 선택 도구로 획 클릭하여 선택 (핸들이 없는 (120, 100) 지점)
        actor
            .SelectTool(ToolKind.Select)
            .Click(new Point(120, 100), monitorIndex: 1);

        Assert.Single(actor.Selection.Elements);
        Assert.Same(element, actor.Selection.Elements[0]);

        // 장식 레이어 확인 (단일 선택 핸들)
        var decLayer = actor.Surface(monitorIndex: 1).DecorationLayer;
        Assert.NotEmpty(decLayer.Children);

        // 3. 선택 획을 (120, 100) -> (220, 200) 으로 드래그 이동 (변위: +100, +100)
        actor.Drag(new Point(120, 100), new Point(220, 200), monitorIndex: 1);

        Assert.Equal(new Vector(100, 100), element.TransformState.Translation);

        // 4. Undo 실행 시 원래 위치로 복원
        actor.Undo();
        Assert.Equal(new Vector(0, 0), element.TransformState.Translation);
    });

    [Fact]
    public void MarqueeSelect_MultipleElements_AndDeleteSelection() => E2EAppFixture.Run(actor =>
    {
        // 1. 획 2개 생성
        actor
            .SelectTool(ToolKind.Pen)
            .DrawStroke(new Point(100, 100), new Point(200, 100), monitorIndex: 1)
            .DrawStroke(new Point(100, 200), new Point(200, 200), monitorIndex: 1);

        var doc = actor.Document(monitorIndex: 1);
        Assert.Equal(2, doc.Elements.Count);

        // 2. 마키 영역 선택 (50, 50) -> (250, 250)
        actor
            .SelectTool(ToolKind.Select)
            .Drag(new Point(50, 50), new Point(250, 250), monitorIndex: 1);

        Assert.Equal(2, actor.Selection.Elements.Count);

        // 3. 선택 삭제 실행
        actor.DeleteSelection();
        Assert.Empty(doc.Elements);
        Assert.Empty(actor.Selection.Elements);
        Assert.True(actor.State.ClickThrough); // R5: 삭제 후 클릭 통과 자동 전이

        // 4. Undo 실행으로 일괄 복원
        actor.Undo();
        Assert.Equal(2, doc.Elements.Count);
    });
}
