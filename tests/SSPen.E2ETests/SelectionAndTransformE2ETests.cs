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

    /// <summary>
    /// R7(c) 특성화 (47단계 선행): 휠 확대 <b>직후</b> 실행취소는 확대를 되돌린다 — 유휴 타이머(450ms)가 확정하기 전이라도
    /// Undo 선두의 플러시가 확대를 먼저 원장에 싣는다. 플러시가 빠지면 실행취소가 획 생성을 되돌리고(문서가 빈다)
    /// 확대는 화면에 남은 채 뒤늦게 원장에 실린다 — "확대해 보고 마음에 안 들어 되돌린다"가 가장 자연스러운 조작이다.
    /// </summary>
    [Fact]
    public void WheelScale_ThenImmediateUndo_RevertsTheScale_NotTheStroke() => E2EAppFixture.Run(actor =>
    {
        actor
            .SelectTool(ToolKind.Pen)
            .DrawStroke(new Point(100, 100), new Point(200, 100), monitorIndex: 1);
        var doc = actor.Document(monitorIndex: 1);
        var element = Assert.Single(doc.Elements);

        actor
            .SelectTool(ToolKind.Select)
            .Click(new Point(120, 100), monitorIndex: 1);
        Assert.Single(actor.Selection.Elements);

        actor.Wheel(new Point(120, 100), +1, monitorIndex: 1);
        Assert.NotEqual(1.0, element.TransformState.ScaleX); // 확대가 화면에 적용된 상태, 원장에는 아직 없다

        actor.Undo();

        Assert.Single(doc.Elements);                                        // 획 생성이 아니라
        Assert.Equal(ElementTransformState.Identity, element.TransformState); // 확대가 되돌아간다
    });
}
