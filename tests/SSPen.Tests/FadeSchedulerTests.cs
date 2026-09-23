using System.Reflection;
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
    public void Controller_FadeFalse_DoesNotSchedule()
    {
        var controller = new FadingInkController(new FadeSchedulerCore());
        var stroke = NewStroke();
        Assert.False(controller.OnElementCommitted(stroke, T0, fade: false));
        Assert.False(stroke.IsFading);
        Assert.Equal(0, controller.Core.PendingCount);
    }

    /// <summary>
    /// 페이드 여부는 커밋마다 넘겨받은 스냅샷(제스처 시작 시점, AGENTS L92)이 정한다 — 컨트롤러는 자기 상태로 판정하지 않는다.
    /// 예전 '활성화 이후 획만' 증인은 커밋 시점 Active를 읽는 구 경로(57단계에 삭제, C-1)를 통해 같은 것을 확인했다.
    /// </summary>
    [Fact]
    public void Controller_FadeFlag_DecidesPerCommit()
    {
        var controller = new FadingInkController(new FadeSchedulerCore()) { Duration = TimeSpan.FromSeconds(3) };
        var before = NewStroke();
        var after = NewStroke();

        Assert.False(controller.OnElementCommitted(before, T0, fade: false)); // 토글 켜기 전에 시작한 제스처
        Assert.True(controller.OnElementCommitted(after, T0, fade: true));    // 토글 켠 뒤 시작한 제스처

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
            Duration = TimeSpan.FromSeconds(seconds),
        };
        var stroke = NewStroke();
        controller.OnElementCommitted(stroke, T0, fade: true);

        Assert.Empty(controller.Core.Due(T0 + TimeSpan.FromSeconds(seconds) - TimeSpan.FromMilliseconds(1)));
        Assert.Equal(new AnnotationElement[] { stroke }, controller.Core.Due(T0 + TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Controller_OnElementRemoved_CancelsPendingFade()
    {
        var controller = new FadingInkController(new FadeSchedulerCore())
        {
            Duration = TimeSpan.FromSeconds(3),
        };
        var stroke = NewStroke();
        controller.OnElementCommitted(stroke, T0, fade: true);
        controller.OnElementRemoved(stroke); // 지우개/undo/전체 지우기 경로
        Assert.Empty(controller.Core.Due(T0 + TimeSpan.FromSeconds(10)));
    }

    /// <summary>
    /// 트립와이어 (57단계, C-1): 커밋 시점 상태로 페이드를 판정하는 공개 경로가 되살아나면 안 된다. 새 호출부가 그것을 부르면
    /// 드래그 도중 토글할 때 진행 중 요소가 재분류되는 결함(AGENTS L92가 막는 것)이 컴파일러 경고 없이 돌아온다.
    /// </summary>
    [Fact]
    public void FadingInkController_HasNoCommitTimeActiveFlag_ByReflection()
    {
        var type = typeof(FadingInkController);

        Assert.Null(type.GetProperty("Active", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
        var commits = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == nameof(FadingInkController.OnElementCommitted))
            .ToArray();
        var only = Assert.Single(commits);
        Assert.Equal(
            [typeof(AnnotationElement), typeof(DateTime), typeof(bool)],
            only.GetParameters().Select(p => p.ParameterType).ToArray());
    }

    [Fact]
    public void StepByWheel_ZeroDelta_ReturnsSameDuration()
    {
        Assert.Equal(2.0, FadingDurations.StepByWheel(2.0, 0));
    }

    [Fact]
    public void StepByWheel_ScrollUp_IncreasesDuration()
    {
        // 0.1 -> 0.5 -> 1.0 -> 2.0 -> 3.0 -> 5.0
        double val = 0.1;
        foreach (var expected in FadingDurations.Steps.Skip(1))
        {
            val = FadingDurations.StepByWheel(val, 120);
            Assert.Equal(expected, val);
        }

        // 최대치(5.0)에서 더 올려도 5.0에 클램프
        val = FadingDurations.StepByWheel(val, 120);
        Assert.Equal(FadingDurations.Max, val);
    }

    [Fact]
    public void StepByWheel_ScrollDown_DecreasesDuration()
    {
        // 5.0 -> 3.0 -> 2.0 -> 1.0 -> 0.5 -> 0.1
        double val = FadingDurations.Max;
        var reversed = FadingDurations.Steps.Reverse().Skip(1);
        foreach (var expected in reversed)
        {
            val = FadingDurations.StepByWheel(val, -120);
            Assert.Equal(expected, val);
        }

        // 최소치(0.1)에서 더 내려도 0.1에 클램프
        val = FadingDurations.StepByWheel(val, -120);
        Assert.Equal(FadingDurations.Min, val);
    }
}
