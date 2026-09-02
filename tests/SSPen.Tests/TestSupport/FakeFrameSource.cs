using SSPen.Annotation;

namespace SSPen.Tests;

/// <summary>
/// <see cref="IFrameSource"/>의 가짜 (45단계). Start/Stop 횟수를 세고, <see cref="Fire"/>로 테스트가 프레임을 직접 일으킨다.
/// 발화 중 Stop이 불려도 안전하도록 핸들러 목록을 복사해 순회한다 (컨트롤러가 틱 첫 줄에서 스스로 떼는 경로).
/// </summary>
internal sealed class FakeFrameSource : IFrameSource
{
    public event Action? Frame;

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public int SubscriberCount => Frame?.GetInvocationList().Length ?? 0;

    public void Start() => StartCount++;

    public void Stop() => StopCount++;

    public void Fire()
    {
        var handlers = Frame?.GetInvocationList();
        if (handlers is null)
        {
            return;
        }
        foreach (var handler in handlers)
        {
            ((Action)handler)();
        }
    }
}
