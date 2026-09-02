namespace SSPen.Annotation;

/// <summary>
/// 프레임 틱 이음매 (45단계). 유일한 구현은 <c>AppController.CompositionTargetFrameSource</c>(private nested) — WPF
/// <c>CompositionTarget.Rendering</c>을 아는 곳이 그 하나뿐이게 한다 (<c>ContentSurfaceWindow.DispatcherIdleScheduler</c> 선례, 13단계).
/// <see cref="Start"/>/<see cref="Stop"/>은 멱등이어야 하며 <see cref="Frame"/> 발화 중 <see cref="Stop"/>이 불려도 안전해야 한다.
/// </summary>
public interface IFrameSource
{
    event Action? Frame;

    void Start();

    void Stop();
}

/// <summary>
/// 페이드 마감을 실행할 수 있는 서피스의 최소 계약 (45단계) — 코어는 <c>ContentSurfaceWindow</c> 타입을 모른다.
/// </summary>
public interface IFadeSurface
{
    AnnotationDocument Document { get; }

    /// <summary>시각물을 서서히 소멸시키고 완료 시 <paramref name="onCompleted"/>를 부른다 (시각물이 없으면 즉시).</summary>
    void AnimateFadeOut(AnnotationElement element, TimeSpan fadeLength, Action onCompleted);
}

/// <summary>
/// 공유 렌더 틱 정책 (45단계, ARCH-3, 프리모템 1, CRIT-1). AppController.UpdateRenderTickSubscription/OnRenderTick을 옮겼다.
///
/// 두 규칙이 계약이다: (1) <b>상시 구독 금지</b> — 후광/페이딩이 필요할 때만 붙이고(<see cref="Refresh"/>는 attach-only),
/// 틱 안에서 스스로 뗀다(<see cref="Needed"/>가 거짓이면 첫 줄에서 <see cref="Stop"/>). 붙임/뗌 술어가 한 함수라 "붙일 조건의
/// 정확한 부정에서 뗀다"가 타입으로 성립한다. (2) 페이드 완료 순서는 <b>Document.Remove → UndoLedger.PurgeElement</b> — 반대로
/// 하면 원장 정리 뒤에 요소가 잠깐 문서에 남아 undo-of-add가 유령 항목을 남긴다.
/// 시계·커서·서피스 조회는 주입이라 헤드리스로 구동된다. 렌더 틱에서 z-밴드를 재적용하고 싶은 유혹은 AGENTS L14 위반이다.
/// </summary>
public sealed class RenderTickController(
    AppState state,
    FadeSchedulerCore fadeCore,
    UndoLedger ledger,
    IFrameSource frames,
    Func<DateTime> now,
    Func<(int X, int Y)?> cursor,
    Action<int, int> updateHalos,
    Func<AnnotationElement, IFadeSurface?> ownerOf)
{
    /// <summary>페이드아웃 애니메이션 길이 — 마감(FadeSchedulerCore 큐)이 지난 뒤 시각물이 사라지는 데 걸리는 시간.</summary>
    public static readonly TimeSpan FadeOutLength = TimeSpan.FromMilliseconds(700);

    private bool _attached;

    public bool IsAttached => _attached;

    /// <summary>틱이 필요한가 — 후광 추적 중이거나, 페이딩 토글이 켜져 있거나, 마감 대기 요소가 있을 때.</summary>
    public static bool Needed(bool haloActive, bool fadingInk, int pendingCount) => haloActive || fadingInk || pendingCount > 0;

    /// <summary>AppState.Changed마다 호출. <b>붙이기만</b> 한다 — 떼는 것은 틱 안에서만 (자기 해제 계약).</summary>
    public void Refresh()
    {
        if (Needed(state.HaloActive, state.FadingInk, fadeCore.PendingCount) && !_attached)
        {
            frames.Frame += OnFrame;
            frames.Start();
            _attached = true;
        }
    }

    /// <summary>종료 시 강제 해제 (멱등). 평상시 해제는 <see cref="OnFrame"/> 첫 줄이 한다.</summary>
    public void Stop()
    {
        if (!_attached)
        {
            return;
        }
        frames.Frame -= OnFrame;
        frames.Stop();
        _attached = false;
    }

    private void OnFrame()
    {
        if (!Needed(state.HaloActive, state.FadingInk, fadeCore.PendingCount))
        {
            Stop();
            return;
        }

        if (state.HaloActive && cursor() is { } c)
        {
            updateHalos(c.X, c.Y);
        }

        if (fadeCore.PendingCount > 0)
        {
            foreach (var element in fadeCore.Due(now()))
            {
                var owner = ownerOf(element);
                if (owner is null)
                {
                    continue;
                }
                owner.AnimateFadeOut(element, FadeOutLength, () =>
                {
                    // CRIT-1: 문서에서 먼저 지우고 원장을 정리한다 — 순서를 뒤집으면 undo-of-add가 유령 항목을 남긴다.
                    owner.Document.Remove(element);
                    ledger.PurgeElement(element);
                });
            }
        }
    }
}
