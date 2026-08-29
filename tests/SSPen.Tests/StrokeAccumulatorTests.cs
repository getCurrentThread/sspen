using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 5단계: 진행 중 획 누적기 (점 거리 필터 + 시작 시점 동결 스타일).
///
/// 기존 <c>OnMouseMove</c>의 무명 리터럴 1.5를 <see cref="StrokeAccumulator.MinPointDistance"/>로
/// 옮기면서 조용히 뒤집히기 쉬운 두 가지 — <b>경계 부호</b>(1.5는 채택)와
/// <b>거리 기준점</b>(마지막으로 <i>채택된</i> 점)를 잠근다.
/// </summary>
public class StrokeAccumulatorTests
{
    private static readonly StrokeStyle Pen = new(Colors.Black, 6, IsHighlighter: false, IsFading: false);

    [Fact]
    public void Points_AfterConstruction_ContainsOnlyStartPoint()
    {
        // 시드 생성자라 '빈 획'이 표현 불가능하다 — StrokeElement는 0점에 ArgumentException을 던진다.
        var acc = new StrokeAccumulator(new Point(10, 20), Pen);

        Point[] expected = [new(10, 20)];
        Assert.Equal(expected, acc.Points);
    }

    [Fact]
    public void TryAppend_BelowMinDistance_IsRejected()
    {
        var acc = new StrokeAccumulator(new Point(0, 0), Pen);

        Assert.False(acc.TryAppend(new Point(1.4, 0)));
        Assert.Single(acc.Points);
    }

    [Fact]
    public void TryAppend_AtMinDistance_IsAccepted()
    {
        // 이관 전 코드는 `>= 1.5`가 채택이었다. `<= MinPointDistance` 거절로 뒤집으면
        // 정확히 1.5인 이동이 조용히 버려진다.
        var acc = new StrokeAccumulator(new Point(0, 0), Pen);

        Assert.True(acc.TryAppend(new Point(StrokeAccumulator.MinPointDistance, 0)));
        Assert.Equal(2, acc.Points.Count);
    }

    [Fact]
    public void TryAppend_MeasuresFromLastAcceptedPoint_NotFromRejected()
    {
        // 0 → 1.0 거절 → 2.0. '마지막으로 제시된 점'(1.0)에서 재면 간격 1.0이라 거절되지만,
        // '마지막으로 채택된 점'(0)에서 재면 2.0이라 채택된다. 기준점이 표본 간격을 정한다.
        var acc = new StrokeAccumulator(new Point(0, 0), Pen);

        Assert.False(acc.TryAppend(new Point(1.0, 0)));
        Assert.True(acc.TryAppend(new Point(2.0, 0)));

        Point[] expected = [new(0, 0), new(2.0, 0)];
        Assert.Equal(expected, acc.Points);
    }

    [Fact]
    public void Points_MirrorAcceptedOnly()
    {
        var acc = new StrokeAccumulator(new Point(0, 0), Pen);
        Point[] offered = [new(0.5, 0), new(2, 0), new(2.5, 0), new(10, 0)];
        List<Point> accepted = [];

        foreach (var p in offered)
        {
            if (acc.TryAppend(p))
            {
                accepted.Add(p);
            }
        }

        Point[] expectedAccepted = [new(2, 0), new(10, 0)];
        Point[] expectedPoints = [new(0, 0), new(2, 0), new(10, 0)];
        Assert.Equal(expectedAccepted, accepted);
        Assert.Equal(expectedPoints, acc.Points);
    }

    [Fact]
    public void Style_IsFrozenAtConstruction()
    {
        // 누적기는 AppState를 참조하지 않는다 — 스타일은 값으로만 들어온다 (동결 규약).
        var state = new AppState { ActiveTool = ToolKind.Pen, FadingInk = true };
        var acc = new StrokeAccumulator(new Point(0, 0), GestureStyleSnapshot.ForStroke(state));
        var frozen = acc.Style;

        state.CurrentColor = Color.FromRgb(0x34, 0x98, 0xDB);
        state.SetThickness(ToolStyleGroup.Pen, ThicknessStep.XLarge);
        state.FadingInk = false;
        acc.TryAppend(new Point(100, 0));

        Assert.Equal(frozen, acc.Style);
        Assert.NotEqual(state.CurrentColor, acc.Style.Color);
        Assert.NotEqual(state.PenThickness, acc.Style.Thickness);
        Assert.True(acc.Style.IsFading);
    }
}
