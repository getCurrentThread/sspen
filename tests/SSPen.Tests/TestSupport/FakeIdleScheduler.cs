using SSPen.Annotation;

namespace SSPen.Tests;

/// <summary>
/// 유휴 디바운스 이음매의 가짜 구현 (R7). 실제 <c>DispatcherTimer</c>는 이 스위트의 STA 쓰레드에서
/// 디스패처를 펌프하지 않으므로 <b>영영 틱하지 않는다</b> — 그래서 만료를 테스트가 직접 일으킨다.
/// </summary>
internal sealed class FakeIdleScheduler : IIdleScheduler
{
    public event Action? Tick;

    /// <summary>디바운스 재시작 횟수 (노치 1회당 1이어야 한다).</summary>
    public int RestartCount { get; private set; }

    /// <summary>취소 횟수 (마감 1회당 1이어야 한다).</summary>
    public int CancelCount { get; private set; }

    public TimeSpan LastInterval { get; private set; }

    /// <summary>현재 구독자 수 — 멱등 재구독(<c>-=</c> 뒤 <c>+=</c>)이 지켜지면 항상 0 또는 1이다.</summary>
    public int SubscriberCount => Tick?.GetInvocationList().Length ?? 0;

    public void Restart(TimeSpan interval)
    {
        RestartCount++;
        LastInterval = interval;
    }

    public void Cancel() => CancelCount++;

    /// <summary>유휴 만료를 일으킨다.</summary>
    public void Fire() => Tick?.Invoke();
}
