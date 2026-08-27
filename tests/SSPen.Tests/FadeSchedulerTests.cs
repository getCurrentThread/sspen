using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>WI-8: 페이딩 잉크 코어 (프리모템 1 — 타이머 추상화, 순수 로직).</summary>
public class FadeSchedulerTests
{
    private static readonly DateTime T0 = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    private static StrokeElement NewStroke() =>
        new([new Point(0, 0), new Point(10, 10)], Colors.Black, 3, isHighlighter: false);

    [Fact]
    public void Due_ReturnsExpiredInDeadlineOrder()
    {
        var core = new FadeSchedulerCore();
        var late = NewStroke();
        var early = NewStroke();
        core.Schedule(late, T0 + TimeSpan.FromSeconds(12));
        core.Schedule(early, T0 + TimeSpan.FromSeconds(3));

        var due = core.Due(T0 + TimeSpan.FromSeconds(13));
        Assert.Equal(new AnnotationElement[] { early, late }, due);
        Assert.Equal(0, core.PendingCount);
    }

    [Fact]
    public void Due_LeavesUnexpiredInQueue()
    {
        var core = new FadeSchedulerCore();
        var a = NewStroke();
        var b = NewStroke();
        core.Schedule(a, T0 + TimeSpan.FromSeconds(3));
        core.Schedule(b, T0 + TimeSpan.FromSeconds(6));

        var due = core.Due(T0 + TimeSpan.FromSeconds(4));
        Assert.Equal(new AnnotationElement[] { a }, due);
        Assert.True(core.IsScheduled(b));
        Assert.Equal(1, core.PendingCount);
    }

    [Fact]
    public void Cancel_RemovesPendingEntry()
    {
        var core = new FadeSchedulerCore();
        var stroke = NewStroke();
        core.Schedule(stroke, T0 + TimeSpan.FromSeconds(3));
        Assert.True(core.Cancel(stroke));
        Assert.Empty(core.Due(T0 + TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Cancel_UnknownElement_ReturnsFalse()
    {
        var core = new FadeSchedulerCore();
        Assert.False(core.Cancel(NewStroke()));
    }

    [Fact]
    public void Controller_InactiveDoesNotSchedule()
    {
        var controller = new FadingInkController(new FadeSchedulerCore()) { Active = false };
        var stroke = NewStroke();
        Assert.False(controller.OnElementCommitted(stroke, T0));
        Assert.False(stroke.IsFading);
        Assert.Equal(0, controller.Core.PendingCount);
    }

    [Fact]
    public void Controller_OnlyStrokesAfterActivationFade()
    {
        var controller = new FadingInkController(new FadeSchedulerCore());
        var before = NewStroke();
        controller.OnElementCommitted(before, T0); // 활성화 이전

        controller.Active = true;
        controller.Duration = TimeSpan.FromSeconds(3);
        var after = NewStroke();
        controller.OnElementCommitted(after, T0);  // 활성화 이후

        Assert.False(before.IsFading);
        Assert.True(after.IsFading);
        var due = controller.Core.Due(T0 + TimeSpan.FromSeconds(3));
        Assert.Equal(new AnnotationElement[] { after }, due);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(12)]
    public void Controller_UsesConfiguredDuration(int seconds)
    {
        var controller = new FadingInkController(new FadeSchedulerCore())
        {
            Active = true,
            Duration = TimeSpan.FromSeconds(seconds),
        };
        var stroke = NewStroke();
        controller.OnElementCommitted(stroke, T0);

        Assert.Empty(controller.Core.Due(T0 + TimeSpan.FromSeconds(seconds) - TimeSpan.FromMilliseconds(1)));
        Assert.Equal(new AnnotationElement[] { stroke }, controller.Core.Due(T0 + TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Controller_OnElementRemoved_CancelsPendingFade()
    {
        var controller = new FadingInkController(new FadeSchedulerCore())
        {
            Active = true,
            Duration = TimeSpan.FromSeconds(3),
        };
        var stroke = NewStroke();
        controller.OnElementCommitted(stroke, T0);
        controller.OnElementRemoved(stroke); // 지우개/undo/전체 지우기 경로
        Assert.Empty(controller.Core.Due(T0 + TimeSpan.FromSeconds(10)));
    }
}
