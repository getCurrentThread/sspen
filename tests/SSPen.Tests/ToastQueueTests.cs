using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="ToastQueue"/>의 증인. 창·타이머는 어댑터(<c>ToastHost</c>)에 남으므로 여기는 판정만 본다 —
/// 지속 시간·선점·병합·타이머 자기 해제 네 가지가 이 표의 관심사다.
/// </summary>
public class ToastQueueTests
{
    private static readonly DateTime Origin = new(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);

    private static (ToastQueue Queue, Func<DateTime> Clock) Make(out Action<double> advance)
    {
        var current = Origin;
        advance = seconds => current = current.AddSeconds(seconds);
        Func<DateTime> clock = () => current;
        return (new ToastQueue(clock), clock);
    }

    [Fact]
    public void Push_FirstRequest_IsVisibleImmediately()
    {
        var (queue, clock) = Make(out _);

        queue.Push(new ToastRequest(ToastKind.Info, "저장했습니다"));
        var step = queue.Tick(clock());

        Assert.True(step.Visible);
        Assert.Equal("저장했습니다", step.Text);
        Assert.False(step.StopTimer);
    }

    /// <summary>표시 시간이 지나면 숨기고, 뒤에 아무것도 없으면 타이머까지 내린다 (폴링 루프를 남기지 않는다).</summary>
    [Fact]
    public void Tick_AfterDwellElapses_HidesAndStopsTimer()
    {
        var (queue, clock) = Make(out var advance);
        queue.Push(new ToastRequest(ToastKind.Info, "복사했습니다"));

        advance(2.7);
        var step = queue.Tick(clock());

        Assert.False(step.Visible);
        Assert.True(step.StopTimer);
    }

    /// <summary>더 심각한 알림은 기다리지 않는다 — 오류 뒤에 성공 문구가 남으면 실패를 성공으로 읽는다.</summary>
    [Fact]
    public void Push_WarningWhileInfoVisible_PreemptsImmediately()
    {
        var (queue, clock) = Make(out var advance);
        queue.Push(new ToastRequest(ToastKind.Info, "복사했습니다"));

        advance(0.5);
        queue.Push(new ToastRequest(ToastKind.Warning, "클립보드 복사 실패"));
        var step = queue.Tick(clock());

        Assert.Equal("클립보드 복사 실패", step.Text);
        Assert.Equal(ToastKind.Warning, step.Kind);
    }

    /// <summary>선점은 시간도 새로 준다: 낮은 등급이 이미 소진한 시간을 물려받지 않는다.</summary>
    [Fact]
    public void Push_Preemption_RestartsTheDwell()
    {
        var (queue, clock) = Make(out var advance);
        queue.Push(new ToastRequest(ToastKind.Info, "복사했습니다"));

        advance(2.5);
        queue.Push(new ToastRequest(ToastKind.Error, "저장 실패"));
        advance(2.0);
        var step = queue.Tick(clock());

        Assert.True(step.Visible);
        Assert.Equal("저장 실패", step.Text);
    }

    /// <summary>같은 등급은 선점하지 않고 줄을 선다.</summary>
    [Fact]
    public void Push_SameSeverity_QueuesBehindTheVisibleOne()
    {
        var (queue, clock) = Make(out var advance);
        queue.Push(new ToastRequest(ToastKind.Info, "첫 번째"));
        queue.Push(new ToastRequest(ToastKind.Info, "두 번째"));

        var first = queue.Tick(clock());
        advance(2.7);
        var second = queue.Tick(clock());

        Assert.Equal("첫 번째", first.Text);
        Assert.Equal("두 번째", second.Text);
        Assert.False(second.StopTimer);
    }

    /// <summary>연타로 같은 문구가 쌓이면 화면이 몇 초씩 밀린다 — 병합해서 시간만 늘린다.</summary>
    [Fact]
    public void Push_IdenticalMessageTwice_CoalescesIntoOneAndExtendsDwell()
    {
        var (queue, clock) = Make(out var advance);
        queue.Push(new ToastRequest(ToastKind.Warning, "클립보드 복사 실패"));

        advance(4.0);
        queue.Push(new ToastRequest(ToastKind.Warning, "클립보드 복사 실패"));
        advance(1.0); // 최초 요청 기준으로는 5초 — 병합이 없었다면 이미 지났다.
        var step = queue.Tick(clock());

        Assert.True(step.Visible);
        advance(4.0);
        Assert.True(queue.Tick(clock()).StopTimer); // 연장된 시간이 지나면 하나만 사라진다 (중복이 뒤에 없다).
    }

    [Fact]
    public void Tick_EmptyQueue_ReportsStopTimer()
    {
        var (queue, clock) = Make(out _);

        var step = queue.Tick(clock());

        Assert.False(step.Visible);
        Assert.True(step.StopTimer);
        Assert.False(queue.HasWork);
    }

    /// <summary>액션 라벨 유무가 클릭 통과 해제의 유일한 근거다 — 창이 아니라 이 판정이 정한다.</summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("폴더 열기", true)]
    public void Step_InteractiveFollowsTheActionLabel(string? actionLabel, bool expected)
    {
        var (queue, clock) = Make(out _);

        queue.Push(new ToastRequest(ToastKind.Info, "저장했습니다", actionLabel));
        var step = queue.Tick(clock());

        Assert.Equal(expected, step.Interactive);
    }

    /// <summary>빈 문구는 표시할 것이 없다 — 빈 창을 깜빡이느니 아무 일도 하지 않는다.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Push_BlankText_IsIgnored(string text)
    {
        var (queue, _) = Make(out _);

        queue.Push(new ToastRequest(ToastKind.Info, text));

        Assert.False(queue.HasWork);
    }

    /// <summary>캡처 세션처럼 화면을 비워야 하는 자리에서는 표시 중인 것과 대기열을 함께 버린다.</summary>
    [Fact]
    public void Clear_DropsVisibleAndPending()
    {
        var (queue, clock) = Make(out _);
        queue.Push(new ToastRequest(ToastKind.Info, "첫 번째"));
        queue.Push(new ToastRequest(ToastKind.Info, "두 번째"));

        queue.Clear();

        Assert.False(queue.HasWork);
        Assert.True(queue.Tick(clock()).StopTimer);
    }

    /// <summary>오늘 값 고정: 정보 2.6초 / 경고 4.5초 / 오류 6초. 바꾸려면 이 줄과 코어 주석을 함께 바꾼다.</summary>
    [Fact]
    public void DwellFor_IsLongerForMoreSevereKinds()
    {
        Assert.Equal(TimeSpan.FromSeconds(2.6), ToastQueue.DwellFor(ToastKind.Info));
        Assert.Equal(TimeSpan.FromSeconds(4.5), ToastQueue.DwellFor(ToastKind.Warning));
        Assert.Equal(TimeSpan.FromSeconds(6.0), ToastQueue.DwellFor(ToastKind.Error));
    }
}
