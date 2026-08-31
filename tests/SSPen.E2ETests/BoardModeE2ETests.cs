using System.Windows;
using SSPen.Annotation;
using Xunit;

namespace SSPen.E2ETests;

public class BoardModeE2ETests
{
    [Fact]
    public void BoardToggle_WhiteAndBlackBoard_PreservesInkAcrossTransitions() => E2EAppFixture.Run(actor =>
    {
        // 1. 펜 판서 생성
        actor
            .SelectTool(ToolKind.Pen)
            .DrawStroke(new Point(200, 200), new Point(400, 200), monitorIndex: 1);

        var doc = actor.Document(monitorIndex: 1);
        Assert.Single(doc.Elements);

        // 2. 화이트보드 모드 켜기
        actor.SetBoardMode(BoardMode.White);
        Assert.Equal(BoardMode.White, actor.State.Board);

        var boardRect = actor.Surface(monitorIndex: 1).BoardRect;
        Assert.Equal(Visibility.Visible, boardRect.Visibility);

        // 3. 블랙보드 모드로 전환
        actor.SetBoardMode(BoardMode.Black);
        Assert.Equal(BoardMode.Black, actor.State.Board);

        // 4. 보드 모드 끄기 (투명 데스크톱 복귀)
        actor.SetBoardMode(BoardMode.None);
        Assert.Equal(BoardMode.None, actor.State.Board);

        // 판서는 보드 전이와 무관하게 보존되어야 함
        Assert.Single(doc.Elements);
    });
}
