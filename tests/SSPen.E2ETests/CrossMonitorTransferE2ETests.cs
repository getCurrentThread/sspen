using System.Windows;
using SSPen.Annotation;
using Xunit;

namespace SSPen.E2ETests;

public class CrossMonitorTransferE2ETests
{
    [Fact]
    public void DragElementAcrossMonitors_TransfersOwnershipAndRebasesCoordinates() => E2EAppFixture.Run(actor =>
    {
        // 1. 주 모니터(인덱스 1, 0..1920) 우측 끝에 획 생성
        actor
            .SelectTool(ToolKind.Pen)
            .DrawStroke(new Point(1800, 500), new Point(1900, 500), monitorIndex: 1);

        var doc1 = actor.Document(monitorIndex: 1);
        var doc2 = actor.Document(monitorIndex: 2); // 우측 모니터 (1920..3840)

        Assert.Single(doc1.Elements);
        Assert.Empty(doc2.Elements);
        var element = doc1.Elements[0];

        // 2. 선택 도구로 획 선택 (핸들이 없는 (1820, 500) 지점)
        actor
            .SelectTool(ToolKind.Select)
            .Click(new Point(1820, 500), monitorIndex: 1);

        Assert.Single(actor.Selection.Elements);

        // 3. 우측 모니터로 드래그 (물리 1820 -> 물리 2020으로 이동)
        actor.Drag(new Point(1820, 500), new Point(2020, 500), monitorIndex: 1);

        // 4. 모니터 1에서 모니터 2로 도큐먼트 이관 검증
        Assert.Empty(doc1.Elements);
        Assert.Single(doc2.Elements);
        Assert.Same(element, doc2.Elements[0]);
        Assert.Single(actor.Selection.Elements); // 선택 유지

        // 5. Undo 실행 시 원래 모니터로 원복
        actor.Undo();
        Assert.Single(doc1.Elements);
        Assert.Empty(doc2.Elements);
    });
}
