namespace SSPen.Annotation;

/// <summary>
/// 페이딩 잉크 코어 스케줄러 (플랜 프리모템 1 설계, 순수 로직).
/// 획별 타이머 대신 (요소, 마감) 큐 하나를 공유 틱이 소비한다.
/// WPF 어댑터는 마감된 요소에 Opacity DoubleAnimation을 시작한다.
/// </summary>
public sealed class FadeSchedulerCore
{
    private readonly List<(AnnotationElement Element, DateTime Deadline)> _queue = [];

    public int PendingCount => _queue.Count;

    public void Schedule(AnnotationElement element, DateTime deadline)
    {
        _queue.Add((element, deadline));
    }

    /// <summary>마감이 지난 요소를 마감 순서대로 반환하고 큐에서 제거.</summary>
    public IReadOnlyList<AnnotationElement> Due(DateTime now)
    {
        List<(AnnotationElement Element, DateTime Deadline)>? due = null;
        for (int i = _queue.Count - 1; i >= 0; i--)
        {
            if (_queue[i].Deadline <= now)
            {
                (due ??= []).Add(_queue[i]);
                _queue.RemoveAt(i);
            }
        }
        if (due is null)
        {
            return [];
        }
        due.Sort((a, b) => a.Deadline.CompareTo(b.Deadline));
        return due.Select(entry => entry.Element).ToArray();
    }

    /// <summary>undo/지우기/전체 지우기 시 보류 중 페이드 취소.</summary>
    public bool Cancel(AnnotationElement element) =>
        _queue.RemoveAll(entry => ReferenceEquals(entry.Element, element)) > 0;

    public bool IsScheduled(AnnotationElement element) =>
        _queue.Any(entry => ReferenceEquals(entry.Element, element));
}

/// <summary>
/// 페이딩 잉크 지속 시간 규칙 (순수 로직, 단일 소유 지점).
///
/// 범위는 <see cref="Min"/>~<see cref="Max"/>초다 (사용자 요청 16차: 이전 3/6/12초 체계에서 재조정).
/// 보관 값은 범위 안 <b>임의의 실수</b>를 허용하고(손으로 편집한 settings.json 존중),
/// <see cref="Steps"/>는 UI 프리셋과 버튼 로테이션에만 쓴다.
/// </summary>
public static class FadingDurations
{
    /// <summary>최소 지속 시간(초). 이보다 짧으면 획을 놓기도 전에 사라져 그렸는지도 모른다.</summary>
    public const double Min = 0.1;

    /// <summary>최대 지속 시간(초).</summary>
    public const double Max = 5.0;

    /// <summary>기본값. 새 범위의 중간치 — 이전 기본값 6초는 범위 밖이라 그대로 쓸 수 없다.</summary>
    public const double Default = 2.0;

    /// <summary>UI 프리셋 사다리. 양 끝은 반드시 <see cref="Min"/>/<see cref="Max"/>와 같다.</summary>
    public static readonly double[] Steps = [0.1, 0.5, 1.0, 2.0, 3.0, Max];

    /// <summary>범위 밖 값을 재단한다. NaN은 기본값으로 돌린다 (손상된 설정 방어).</summary>
    public static double Clamp(double seconds) =>
        double.IsNaN(seconds) ? Default : Math.Clamp(seconds, Min, Max);

    /// <summary>가장 가까운 사다리 칸의 인덱스 (콤보·플라이아웃 강조 표시용).</summary>
    public static int NearestIndex(double seconds)
    {
        double target = Clamp(seconds);
        int best = 0;
        for (int i = 1; i < Steps.Length; i++)
        {
            if (Math.Abs(Steps[i] - target) < Math.Abs(Steps[best] - target))
            {
                best = i;
            }
        }
        return best;
    }

    /// <summary>페이딩 버튼 재클릭 로테이션: 다음 사다리 칸으로, 끝에서는 처음으로 돌아간다.</summary>
    public static double Next(double current) =>
        Steps[(NearestIndex(current) + 1) % Steps.Length];

    /// <summary>휠 스크롤에 따른 지속 시간 사다리 이동 (delta > 0 길게/위쪽, delta < 0 짧게/아래쪽).</summary>
    public static double StepByWheel(double current, int delta)
    {
        if (delta == 0)
        {
            return current;
        }
        int idx = NearestIndex(current);
        int step = delta > 0 ? 1 : -1;
        int next = Math.Clamp(idx + step, 0, Steps.Length - 1);
        return Steps[next];
    }

    /// <summary>두 지속 시간이 같은 칸인가 (부동소수점 오차 허용).</summary>
    public static bool Same(double a, double b) => Math.Abs(a - b) < 0.001;
}

/// <summary>
/// 페이딩 잉크 활성화 규칙 (Round 13): 활성화 이후 그린 획만 대상.
/// 지속 시간은 <see cref="FadingDurations"/> 범위(0.1~5초)에서 고르며, 획을 놓은 시점부터 카운트한다.
/// </summary>
public sealed class FadingInkController
{
    private readonly FadeSchedulerCore _core;

    public FadingInkController(FadeSchedulerCore core)
    {
        _core = core;
        Duration = TimeSpan.FromSeconds(FadingDurations.Default);
    }

    public bool Active { get; set; }

    public TimeSpan Duration { get; set; }

    public FadeSchedulerCore Core => _core;

    /// <summary>획 커밋 시 호출 (구 경로): 현재 Active 상태로 판정.</summary>
    public bool OnElementCommitted(AnnotationElement element, DateTime now) =>
        OnElementCommitted(element, now, Active);

    /// <summary>획 커밋: fade는 획 시작 시점 판정 (아키텍트 자문 — 드래그 중 전환 오분류 방지).</summary>
    public bool OnElementCommitted(AnnotationElement element, DateTime now, bool fade)
    {
        if (!fade)
        {
            return false;
        }
        element.IsFading = true;
        _core.Schedule(element, now + Duration);
        return true;
    }

    /// <summary>요소가 문서에서 사라질 때(지우개/undo/전체 지우기) 보류 페이드 취소.</summary>
    public void OnElementRemoved(AnnotationElement element) => _core.Cancel(element);
}
