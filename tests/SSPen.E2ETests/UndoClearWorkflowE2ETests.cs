using System.Windows;
using SSPen.Annotation;
using Xunit;

namespace SSPen.E2ETests;

public class UndoClearWorkflowE2ETests
{
    [Fact]
    public void ClearAll_RemovesInkOnAllSurfaces_AndUndoRestoresAll() => E2EAppFixture.Run(actor =>
    {
        // 1. 모니터 0, 1, 2 에 각각 획 생성
        actor
            .SelectTool(ToolKind.Pen)
            .DrawStroke(new Point(100, 100), new Point(200, 100), monitorIndex: 0)
            .DrawStroke(new Point(100, 100), new Point(200, 100), monitorIndex: 1)
            .DrawStroke(new Point(100, 100), new Point(200, 100), monitorIndex: 2);

        Assert.Single(actor.Document(0).Elements);
        Assert.Single(actor.Document(1).Elements);
        Assert.Single(actor.Document(2).Elements);

        // 2. 전체 지우기 (Clear All)
        actor.ClearAll();

        Assert.Empty(actor.Document(0).Elements);
        Assert.Empty(actor.Document(1).Elements);
        Assert.Empty(actor.Document(2).Elements);

        // 3. Undo 시 모든 서피스의 요소가 일괄 복원되어야 함
        actor.Undo();

        Assert.Single(actor.Document(0).Elements);
        Assert.Single(actor.Document(1).Elements);
        Assert.Single(actor.Document(2).Elements);
    });
}
