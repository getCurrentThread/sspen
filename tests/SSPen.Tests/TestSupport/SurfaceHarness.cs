using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SSPen.Annotation;

namespace SSPen.Tests;

/// <summary>
/// <see cref="SurfaceHarness"/>의 변종을 표현하는 옵션. 스위트마다 달랐던 네 가지를 값으로 드러낸다.
/// </summary>
internal sealed record SurfaceHarnessOptions
{
    /// <summary>주입 시계 (R7 휠 유휴·페이드 마감). null이면 프로덕션 기본값(UtcNow)이 그대로 쓰인다.</summary>
    public Func<DateTime>? Now { get; init; }

    /// <summary>
    /// 서피스 경계 이음매. null이면 프로덕션(창)과 <b>같은 식</b>으로 캔버스에서 유도한다 —
    /// <c>Layout</c>이 없으면 (0,0,0,0)이라 어떤 핸들도 잡히지 않는다 (SurfaceEntryPointTests가 그 전제를 쓴다).
    /// 값을 주면 캔버스를 보지 않고 그 델리게이트가 그대로 흘러 들어간다 (SurfaceBoundsSeamTests의 전제).
    /// </summary>
    public Func<Rect>? SurfaceBounds { get; init; }

    /// <summary>캔버스를 이 크기로 measure/arrange 해 ActualWidth/Height를 채운다. null이면 미측정(0×0).</summary>
    public Size? Layout { get; init; }

    /// <summary>
    /// 커밋 델리게이트가 <see cref="SurfaceHarness.Commits"/>에 기록하는 것에 더해 프로덕션
    /// (<c>AppController.OnCommitTransform</c>의 <c>RecordTransform</c> 자리)처럼 원장에도 싣는다 —
    /// 항목 순서를 관측하는 스위트용 (R7).
    /// </summary>
    public bool CommitToLedger { get; init; }
}

/// <summary>
/// 컨트롤러 1대 + 그 협력자들. 창 대신 <see cref="ISurfaceHost"/>를 기록하는 가짜로 채운다.
/// 네 스위트의 private Harness와 WheelScaleControllerTests의 ControllerRig가 쓰던 관측 프로퍼티의
/// <b>상위집합</b>을 노출한다 — 각 스위트는 자기가 쓰던 것만 읽는다.
///
/// 스위트별 차이(측정 여부·경계 출처·커밋 싱크·시계)는 <see cref="SurfaceHarnessOptions"/>로 표현하고,
/// 각 파일은 <c>private sealed class Harness : SurfaceHarness</c> 한 줄짜리 파생으로 옵션만 고정해
/// 테스트 본문의 <c>new Harness(…)</c>·<c>h.Marquee</c> 등을 바꾸지 않는다.
///
/// requestClickThrough는 <b>세기만</b> 한다: 프로덕션은 이 콜백을 Dispatcher.BeginInvoke로 지연시키므로
/// (ApplyState → CancelActiveInput 재진입 방지) 가짜가 동기로 되돌아가면 프로덕션보다 관대해져 그 버그를 숨긴다.
/// </summary>
internal class SurfaceHarness : ISurfaceHost
{
    protected SurfaceHarness(SurfaceHarnessOptions? options = null)
    {
        options ??= new SurfaceHarnessOptions();
        Canvas = new Canvas();
        if (options.Layout is { } layout)
        {
            Canvas.Width = layout.Width;
            Canvas.Height = layout.Height;
            // measure/arrange를 실제로 돌려 ActualWidth/Height를 채운다 — SurfaceBounds의 출처다.
            Canvas.Measure(layout);
            Canvas.Arrange(new Rect(0, 0, layout.Width, layout.Height));
        }

        State = new AppState();
        Document = new AnnotationDocument("test");
        Selection = new SelectionModel();
        Ledger = new UndoLedger(OwnerLookup, Selection);
        Fading = new FadingInkController(new FadeSchedulerCore());
        Document.ElementTransformChanged += _ => TransformNotifications++;

        bool toLedger = options.CommitToLedger;
        var seams = new SurfaceInputSeams
        {
            // 프로덕션(창)과 **같은 식**으로 캔버스에서 유도한다 (R5) — 옵션이 주어지면 그 델리게이트 그대로.
            SurfaceBounds = options.SurfaceBounds ?? (() => new Rect(0, 0, Canvas.ActualWidth, Canvas.ActualHeight)),
            // R7: 실제 DispatcherTimer는 펌프 없는 STA 쓰레드에서 영영 틱하지 않는다.
            IdleScheduler = Idle,
        };
        if (options.Now is { } now)
        {
            seams = seams with { Now = now };
        }

        Controller = new SurfaceInputController(
            Canvas, State, Document, Ledger, Fading, this,
            Selection, OwnerLookup, _ => 1.0,
            rect => MarqueePushes.Add(rect),
            frame => FramePushes.Add(frame),
            hint => BadgeHints.Add(hint),
            (deltas, drop) =>
            {
                Commits.Add((deltas, drop));
                if (toLedger)
                {
                    Ledger.RecordTransform(deltas);
                }
            },
            () => ClickThroughRequests++,
            seams);
    }

    /// <summary>휠 유휴 디바운스 가짜 (R7) — 만료는 테스트가 직접 일으킨다.</summary>
    public FakeIdleScheduler Idle { get; } = new();

    public Canvas Canvas { get; }

    public AppState State { get; }

    public AnnotationDocument Document { get; }

    public SelectionModel Selection { get; }

    public UndoLedger Ledger { get; }

    public FadingInkController Fading { get; }

    public SurfaceInputController Controller { get; }

    /// <summary>마퀴 푸시 전부 (해제 null 포함) — 횟수가 검증 대상인 스위트용.</summary>
    public List<Rect?> MarqueePushes { get; } = [];

    /// <summary>마지막으로 밀린 마퀴 (없으면 null).</summary>
    public Rect? Marquee => MarqueePushes.Count == 0 ? null : MarqueePushes[^1];

    /// <summary>제스처 프레임 푸시 전부 (해제 null 포함) — 횟수와 순서가 검증 대상인 스위트용.</summary>
    public List<GroupFrame?> FramePushes { get; } = [];

    /// <summary>마지막으로 밀린 제스처 프레임 (없으면 null).</summary>
    public GroupFrame? GestureGroupFrame => FramePushes.Count == 0 ? null : FramePushes[^1];

    /// <summary>표 배지 힌트 푸시 전부 (해제 null 포함) — 26단계 이음매의 관측 창구.</summary>
    public List<TableBadgeHint?> BadgeHints { get; } = [];

    public List<(IReadOnlyList<TransformDelta> Deltas, Point? Drop)> Commits { get; } = [];

    public int ClickThroughRequests { get; private set; }

    /// <summary>ARCH-6 캡처 해제 호출 수.</summary>
    public int ReleaseCaptureCalls { get; private set; }

    /// <summary>R15 알림 횟수 — 스냅샷을 잡았는지 여부의 관측 가능한 그림자다.</summary>
    public int TransformNotifications { get; private set; }

    private AnnotationDocument? OwnerLookup(AnnotationElement element) =>
        Document.Elements.Contains(element) ? Document : null;

    public void SetNoActivate(bool on) { }

    public void ActivateWindow() { }

    public void CaptureMouse() { }

    public void ReleaseMouseCapture() => ReleaseCaptureCalls++;

    public DpiScale GetDpi() => new(1.0, 1.0);
}
