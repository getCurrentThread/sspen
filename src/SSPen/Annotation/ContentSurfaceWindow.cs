using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SSPen.Diagnostics;
using SSPen.Interop;

namespace SSPen.Annotation;

/// <summary>
/// 모니터별 독립 투명 콘텐츠 서피스 (스펙 고정 F8: 단일 가상스크린 오버레이 금지).
/// exstyle: WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE (+ 클릭 통과 시 WS_EX_TRANSPARENT).
/// ARCH-1: 완전 투명 WPF 창은 exstyle과 무관하게 입력이 통과하므로, 상호작용 상태에서는
/// 근사투명 히트테스트 배경(#01000000)을 깐다.
/// ARCH-6: 마우스 캡처로 획은 원점 서피스에 남고 모니터 이음새에서 클리핑된다.
/// 입력 상태 머신은 <see cref="SurfaceInputController"/>에 위임하고, 이 창은 <see cref="ISurfaceHost"/>로
/// ARCH-2 NOACTIVATE 핸드셰이크와 ARCH-6 마우스 캡처만 제공한다.
/// </summary>
public sealed class ContentSurfaceWindow : Window, ISurfaceHost
{
    private static readonly SolidColorBrush HitTestBrush = AnnotationVisualFactory.CreateFrozen(Color.FromArgb(0x01, 0, 0, 0));

    private readonly MonitorSurfaceInfo _monitor;
    private readonly AppState _state;
    private readonly FadingInkController _fading;
    private readonly Func<nint> _zAnchor;
    private System.Windows.Interop.HwndSourceHook? _zHook; // GC 고정
    private readonly Grid _root;
    private readonly System.Windows.Shapes.Rectangle _boardRect;
    private readonly Canvas _inkCanvas;
    private readonly Canvas _haloLayer;
    private readonly Canvas _decorationLayer;
    private readonly Ellipse _halo;
    private readonly Dictionary<long, FrameworkElement> _visuals = [];
    private readonly SurfaceInputController _input;
    private readonly SelectionModel _selection;
    private Rect? _marquee;

    /// <summary>
    /// 제스처 도중 입력 컨트롤러가 밀어 넣은 그룹 프레임 (R1). null이면 매 그리기마다 살아있는
    /// 축 정렬 합집합으로 재계산한다. GroupRotate 동안에는 <b>크기는 동결, 각도만 매 프레임 갱신</b>된다 —
    /// 그래서 이름이 '동결(override)'이 아니라 '제스처 프레임'이다.
    /// </summary>
    private GroupFrame? _gestureGroupFrame;
    // 보드 전이 판정용 직전 적용 상태 (BoardTransition.Resolve 입력).
    private bool _boardShown;
    private BoardMode _boardApplied = BoardMode.None;


    public ContentSurfaceWindow(
        MonitorSurfaceInfo monitor,
        AppState state,
        AnnotationDocument document,
        UndoLedger ledger,
        FadingInkController fading,
        SelectionModel selection,
        Func<AnnotationElement, AnnotationDocument?> ownerLookup,
        Func<AnnotationDocument, double> dpiOf,
        Action<IReadOnlyList<TransformDelta>, (int X, int Y)?> commitTransform,
        Action requestClickThrough,
        Func<nint> zAnchor)
    {
        _monitor = monitor;
        _state = state;
        Document = document;
        _fading = fading;
        _selection = selection;
        _zAnchor = zAnchor;

        Title = $"SS Pen Content Surface: {monitor.DeviceName}";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // 논리 좌표 배치(100% DPI 항등). 물리 확정 배치는 OnSourceInitialized의 SetWindowPos가 담당 (R2).
        Left = monitor.WorkArea.X;
        Top = monitor.WorkArea.Y;
        Width = monitor.WorkArea.Width;
        Height = monitor.WorkArea.Height;

        _boardRect = new System.Windows.Shapes.Rectangle { Visibility = Visibility.Collapsed };
        _inkCanvas = new Canvas { ClipToBounds = true };
        _halo = new Ellipse
        {
            Width = 40,
            Height = 40,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        _haloLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        _haloLayer.Children.Add(_halo);

        // SEL-10: 장식은 판서 요소가 아니라 UI다. _inkCanvas가 아닌 별도 최상단 레이어에 두면
        // (a) Document.Clear()에 고아 시각물이 생기지 않고 (b) 캡처에서 레이어 단위로 제외할 수 있다.
        // 핸들 힌트는 WPF 이벤트가 아니라 SurfaceInputController의 좌표 계산이므로 IsHitTestVisible=false다.
        _decorationLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = false };

        // 보드가 화면 위로 걷힐 때 창 밖까지 그려지지 않도록 보드 전용 클립 컨테이너를 둔다.
        // 잉크/후광/장식은 이 클립과 무관하게 기존 순서를 유지한다.
        var boardClip = new Canvas { ClipToBounds = true, IsHitTestVisible = false };
        boardClip.Children.Add(_boardRect);
        // Canvas 자식은 자동 신장되지 않으므로 창 크기에 맞음 묶어준다.
        boardClip.SizeChanged += (_, e) =>
        {
            _boardRect.Width = e.NewSize.Width;
            _boardRect.Height = e.NewSize.Height;
        };

        // 판서 서피스에서는 OS의 펜 제스처를 끈다 (R8 실측: 둘 다 WPF 기본값 True였다).
        // 길게 누르기=우클릭은 인식 판정 동안 **첫 점의 마우스 승격을 지연**시켜 획 시작이 늦게 찍히고
        // 원형 리플 피드백이 화면에 그려진다. 플릭도 빠른 획을 제스처로 가로챈다.
        Stylus.SetIsPressAndHoldEnabled(this, false);
        Stylus.SetIsFlicksEnabled(this, false);

        _root = new Grid();
        _root.Children.Add(boardClip);
        _root.Children.Add(_inkCanvas);
        _root.Children.Add(_haloLayer);
        _root.Children.Add(_decorationLayer);
        Content = _root;

        _input = new SurfaceInputController(
            _inkCanvas, _state, Document, ledger, _fading, this,
            selection, ownerLookup, dpiOf, SetMarquee, SetGestureGroupFrame,
            (deltas, drop) => commitTransform(deltas, drop is { } p ? ToPhysical(p) : null),
            // R5: 해제 제스처가 끝난 **뒤에** 상태를 바꾼다. 마우스 업 핸들러 안에서 곧바로 켜면
            // ApplyState → CancelActiveInput이 같은 콜 스택에서 재진입해 캡처 해제 순서가 뒤엉킨다.
            () => Dispatcher.BeginInvoke(requestClickThrough),
            // R5: 서피스 경계의 유일 소유자는 이 창이다. 값이 아니라 델리게이트로 넘기는 이유는
            // 여기서는 아직 레이아웃이 돌지 않아 ActualWidth가 0이기 때문이다 (SurfaceInputSeams 문서 참고).
            new SurfaceInputSeams
            {
                SurfaceBounds = () => this.SurfaceBounds,
                // R7: 휠 유휴 디바운스도 창이 소유한다 — DispatcherTimer를 아는 곳은 여기 하나뿐이다.
                IdleScheduler = new DispatcherIdleScheduler(Dispatcher),
            });

        Document.ElementAdded += OnElementAdded;
        Document.ElementRemoved += OnElementRemoved;
        Document.ElementTransformChanged += OnElementTransformChanged;
        _selection.SelectionChanged += OnSelectionChanged;
        _state.Changed += OnStateChanged;
    }

    public AnnotationDocument Document { get; }

    public MonitorSurfaceInfo Monitor => _monitor;

    /// <summary>이 서피스의 DPI 배율. 이관 시 원본/대상의 논리 단위를 맞추는 데 쓴다 (ARCH-20).</summary>
    public double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    public nint Hwnd { get; private set; }

    // ---- E2E 및 테스트 전용 접근자 ----
    internal SurfaceInputController Input => _input;
    internal Canvas InkCanvas => _inkCanvas;
    internal Canvas DecorationLayer => _decorationLayer;
    internal System.Windows.Shapes.Rectangle BoardRect => _boardRect;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Hwnd = WindowStyling.GetHwnd(this);
        WindowStyling.SetToolWindow(Hwnd, true);
        WindowStyling.SetNoActivate(Hwnd, true);
        WindowStyling.PlacePhysical(Hwnd, _monitor.WorkArea);
        // 사용자 조타: 서피스는 어떤 경우에도 툴바 위로 올라가지 않는다 (도구 선택 후 툴바 상호작용 보장).
        _zHook = WindowStyling.AnchorBelow(Hwnd, _zAnchor);
        ApplyState();
        // R2 배치 검증 (프리모템 2 탐지 신호): 시동 시점에 기대/실제 물리 사각형 일치를 기록한다.
        NativeMethods.GetWindowRect(Hwnd, out var actual);
        Log.Info(
            $"서피스 {_monitor.DeviceName}: 기대 {_monitor.WorkArea} / 실제 ({actual.Left},{actual.Top},{actual.Right - actual.Left},{actual.Bottom - actual.Top})");
    }

    private bool _closed;

    protected override void OnClosed(EventArgs e)
    {
        Detach();
        if (_zHook is not null && Hwnd != 0)
        {
            System.Windows.Interop.HwndSource.FromHwnd(Hwnd)?.RemoveHook(_zHook);
            _zHook = null;
        }
        Hwnd = 0;
        base.OnClosed(e);
    }

    /// <summary>
    /// 창 닫힘/비활성화 시 이벤트 구독 해제 및 활성 입력 정리.
    /// </summary>
    public void Detach()
    {
        if (_closed)
        {
            return;
        }
        _closed = true;
        _state.Changed -= OnStateChanged;
        _selection.SelectionChanged -= OnSelectionChanged;
        Document.ElementAdded -= OnElementAdded;
        Document.ElementRemoved -= OnElementRemoved;
        Document.ElementTransformChanged -= OnElementTransformChanged;
        _input.CancelActiveInput();
    }

    private bool _suspended;

    /// <summary>캡처 세션 등에서 서피스 입력을 일시 중단/복원한다.</summary>
    public void SetSuspended(bool suspended)
    {
        if (_closed || Hwnd == 0)
        {
            return;
        }
        _suspended = suspended;
        if (suspended)
        {
            _input.CancelActiveInput();
            WindowStyling.SetClickThrough(Hwnd, true);
            _root.Background = null;
            _root.IsHitTestVisible = false;
            Cursor = Cursors.Arrow;
        }
        else
        {
            _root.IsHitTestVisible = true;
            ApplyState();
        }
    }

    /// <summary>상태 변화 재적용: 표시, 클릭 통과, 히트테스트 배경, 보드, 커서.</summary>
    public void ApplyState()
    {
        if (_closed || Hwnd == 0)
        {
            return;
        }

        if (_suspended)
        {
            Visibility = _state.SurfacesVisible ? Visibility.Visible : Visibility.Hidden;
            WindowStyling.SetClickThrough(Hwnd, true);
            _root.Background = null;
            _root.IsHitTestVisible = false;
            Cursor = Cursors.Arrow;
            return;
        }

        Visibility = _state.SurfacesVisible ? Visibility.Visible : Visibility.Hidden;

        bool interactive = _state.IsInteractive;
        WindowStyling.SetClickThrough(Hwnd, !interactive);
        _root.Background = interactive ? HitTestBrush : null;
        _root.IsHitTestVisible = interactive;
        Cursor = interactive ? CursorFor(_state.ActiveTool) : Cursors.Arrow;

        if (!_state.HaloActive)
        {
            _halo.Visibility = Visibility.Collapsed;
        }

        ApplyBoardState();

        if (!interactive)
        {
            // gen-7 자문(MED): 비인터랙티브 전환으로 버튼 업이 유실돼도 유령 드래그 삭제가 없도록 리셋.
            _input.CancelActiveInput();
        }
    }

    private void OnStateChanged() => Dispatcher.Invoke(ApplyState);

    // ---- 보드 전이 (사용자 요청 16차: 위→아래로 내려오고 다시 위로 걷힌다) ----

    /// <summary>보드 슬라이드 길이. 블라인드를 내리는 동작이 보이려면 페이드보다 조금 길어야 한다.</summary>
    private static readonly TimeSpan BoardSlideDuration = TimeSpan.FromMilliseconds(280);

    /// <summary>
    /// 보드 표시를 적용한다. 전이 판정은 <see cref="BoardTransition"/>(순수 로직)이 소유한다 —
    /// <c>ApplyState()</c>는 모든 상태 변경에 불리므로 전이가 아닐 때 애니메이션을 걸면
    /// 퀵컬러를 누를 때마다 보드가 깜빡인다.
    /// </summary>
    private void ApplyBoardState()
    {
        bool shouldShow = BoardTransition.ShouldShow(_state.Board, _state.BoardAllMonitors, _monitor.IsPrimary);
        var kind = BoardTransition.Resolve(_boardShown, _boardApplied, shouldShow, _state.Board);
        _boardShown = shouldShow;
        _boardApplied = _state.Board;

        switch (kind)
        {
            case BoardTransitionKind.None:
                return;

            case BoardTransitionKind.Recolor:
                // 화이트 ↔ 블랙은 둘 다 불투명하므로 교차 페이드할 것이 없다 — 즉시 교체.
                _boardRect.Fill = BoardBrush(_state.Board);
                return;

            case BoardTransitionKind.SlideDown:
                _boardRect.Fill = BoardBrush(_state.Board);
                _boardRect.Visibility = Visibility.Visible;
                // 화면 바로 위(−높이)에서 제자리(0)로 내려온다.
                BeginBoardSlide(from: -BoardTravel, to: 0, collapseWhenDone: false);
                return;

            case BoardTransitionKind.SlideUp:
                // 제자리에서 다시 화면 위로 걷힌다.
                BeginBoardSlide(from: Canvas.GetTop(_boardRect), to: -BoardTravel, collapseWhenDone: true);
                return;
        }
    }

    private static Brush BoardBrush(BoardMode mode) => mode == BoardMode.Black ? Brushes.Black : Brushes.White;

    /// <summary>
    /// 이동 거리: 모니터 높이만큼 올리면 화면 밖으로 완전히 빠진다.
    /// 논리 단위를 쓰는 이유: 보드 사각형은 WPF 레이아웃 안에 있으므로 물리 픽셀이 아니다.
    /// </summary>
    private double BoardTravel => Math.Max(ActualHeight, _monitor.WorkArea.Height);

    private void BeginBoardSlide(double from, double to, bool collapseWhenDone)
    {
        var animation = new System.Windows.Media.Animation.DoubleAnimation(from, to, BoardSlideDuration)
        {
            FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd,
            // 끝에서 부드럽게 멈춰 블라인드를 내리는 감각을 낸다 (등속 직선은 기계적으로 보인다).
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
            },
        };
        if (collapseWhenDone)
        {
            animation.Completed += (_, _) =>
            {
                // 올라가는 중간에 다시 켜졌으면 숨기면 안 된다 — 완료 시점의 최신 상태로 재판정한다.
                if (!_boardShown)
                {
                    _boardRect.Visibility = Visibility.Collapsed;
                }
            };
        }
        // Canvas.Top을 직접 애니메이션한다. RenderTransform을 쓰지 않는 이유:
        // 그줦 단일 소유 지점(AnnotationVisualFactory.ApplyRenderTransform)은 판서 요소 변형 전용이라,
        // 보드가 거기에 끼어들면 선택 도구의 변형 소유 규약이 흐려진다.
        _boardRect.BeginAnimation(Canvas.TopProperty, animation);
    }

    // ---- 강조 커서 후광 (ARCH-3: GetCursorPos 폴링 기반, 클릭 통과 중에도 추적) ----

    /// <summary>공유 렌더 틱이 호출. 물리 커서 좌표가 이 모니터 안일 때만 표시.</summary>
    public void UpdateHalo(int physicalX, int physicalY)
    {
        bool visible = _state.HaloActive
            && _state.SurfacesVisible
            && _monitor.WorkArea.Contains(physicalX, physicalY);
        if (!visible)
        {
            _halo.Visibility = Visibility.Collapsed;
            return;
        }
        var dpi = VisualTreeHelper.GetDpi(this);
        var local = CoordinateSpace.ToLogical(
            physicalX - _monitor.WorkArea.X, physicalY - _monitor.WorkArea.Y, dpi.DpiScaleX);
        Canvas.SetLeft(_halo, local.X - 20);
        Canvas.SetTop(_halo, local.Y - 20);
        var c = _state.CurrentColor;
        _halo.Fill = new SolidColorBrush(Color.FromArgb(0x59, c.R, c.G, c.B));
        _halo.Visibility = Visibility.Visible;
    }

    /// <summary>도구별 커서 (사용자 조타: UX 미려화): 펜/형광펜/페이딩=펜, 텍스트=IBeam,
    /// 지우개=커스텀 지우개 커서, 도형=십자. 도구 없음은 호출되지 않음(비인터랙티브=화살표).</summary>
    private static Cursor CursorFor(ToolKind tool) => tool switch
    {
        ToolKind.Pen or ToolKind.Highlighter => Cursors.Pen,
        ToolKind.Text => Cursors.IBeam,
        ToolKind.Eraser => CursorFactory.Eraser,
        ToolKind.Select => Cursors.Arrow,
        _ => Cursors.Cross,
    };

    // ---- 선택 장식 레이어 (SEL-10, SEL-11) ----

    /// <summary>캡처 시 장식만 숨긴다 (SEL-17). 서피스 창을 숨기면 잉크까지 사라져 as-seen 인텐트가 깨진다.</summary>
    public void SetDecorationsVisible(bool visible) =>
        _decorationLayer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>마퀴 사각형 갱신 (null이면 숨김). 입력 컨트롤러가 호출한다.</summary>
    public void SetMarquee(Rect? rect)
    {
        _marquee = rect;
        RedrawDecorations();
    }

    /// <summary>
    /// 제스처 프레임 푸시/해제 (R1). 입력 컨트롤러가 <c>setGestureGroupFrame</c> 델리게이트로 부른다.
    ///
    /// GroupRotate 동안에는 <b>매 마우스무브마다</b> 불린다 — 크기는 제스처 시작값으로 동결하고 각도만
    /// 갱신한다. 살아있는 합집합으로 되그리면 회전 중 프레임이 부풀어 잡은 핸들이 커서 밑에서 빠져나가고,
    /// 반대로 각도까지 동결하면 가이드가 잉크를 따라오지 않는다(이 수정 이전의 증상).
    /// 등방 스케일은 아무것도 밀지 않는다 — 살아있는 합집합이 정답이다
    /// (<see cref="SelectionGroup.GestureFrame"/> 참고).
    /// </summary>
    public void SetGestureGroupFrame(GroupFrame? frame)
    {
        if (Nullable.Equals(_gestureGroupFrame, frame))
        {
            return; // 값이 그대로면 재그리기를 건너뛴다 (마우스 다운마다 오는 null 초기화가 공짜가 된다).
        }
        _gestureGroupFrame = frame;
        RedrawDecorations();
    }

    /// <summary>회전 핸들 클램프용 서피스 논리 경계. 렌더와 **같은 값**을 써야 힌트와 그림이 어긋나지 않는다 (R5).</summary>
    /// <remarks>
    /// 이 창이 <b>유일 소유자</b>다. 입력 컨트롤러는 <c>SurfaceInputSeams.SurfaceBounds</c>로 이 값을 받아
    /// 히트 테스트에 쓰므로, 렌더와 힌트가 어긋나는 상태 자체가 표현 불가능하다.
    /// 대체값 금지: <c>Window.ActualWidth/Height</c>(장식 레이어가 아니라 창 전체),
    /// <c>MonitorSurfaceInfo.WorkArea</c>(물리 픽셀·작업 영역 원점), 물리 모니터 경계 — 셋 다 값이 다르다.
    /// </remarks>
    private Rect SurfaceBounds => new(0, 0, _inkCanvas.ActualWidth, _inkCanvas.ActualHeight);

    /// <summary>
    /// 장식 재그리기. 드래그 중에는 선택집합이 불변이라 <c>SelectionChanged</c>가 오지 않으므로
    /// 변형 채널과 입력 컨트롤러가 직접 부른다 (CRIT-08).
    /// </summary>
    public void RedrawDecorations()
    {
        if (_closed || Hwnd == 0)
        {
            return;
        }

        _decorationLayer.Children.Clear();

        if (_marquee is { } marquee)
        {
            _decorationLayer.Children.Add(AnnotationVisualFactory.BuildMarquee(marquee));
        }

        var surfaceBounds = SurfaceBounds;
        var owned = SelectionGroup.OwnedBy(Document, _selection);

        // 모니터에 걸친 선택은 경계만 그리고 핸들을 숨긴다 (SEL-LIM-5): 두 서피스의 논리 좌표계가
        // 서로소라 공통 프레임이 성립하지 않으므로, 잡을 수 없는 핸들을 그리면 거짓 어포던스가 된다.
        // 술어는 히트 테스트(SelectionGesturePlanner.Plan)와 **같은 함수**를 쓴다 —
        // 소유 요소가 1개인 서피스는 아래 요소별 경로를 타므로, 그룹 분기에만 걸어두면 그쪽에 구멍이 난다.
        bool handles = SelectionGroup.HandlesGrabbable(owned.Count, _selection.Count);

        // R1: 다중 선택은 **하나의 그룹**으로 보인다 — 요소별 프레임 대신 공통 축 정렬 프레임 1개.
        if (owned.Count >= SelectionGroup.MinGroupCount)
        {
            DrawGroupDecorations(owned, surfaceBounds, handles);
            return;
        }

        foreach (var element in owned)
        {
            var corners = element.TransformedCorners();
            _decorationLayer.Children.Add(AnnotationVisualFactory.BuildSelectionBorder(corners));
            if (!handles)
            {
                continue;
            }

            var bounds = element.LocalBounds;
            var matrix = element.TransformMatrix;
            foreach (var handle in TransformMath.SizeHandlesCornersFirst)
            {
                var center = matrix.Transform(TransformMath.HandleCenterLocal(bounds, handle));
                _decorationLayer.Children.Add(
                    AnnotationVisualFactory.BuildHandle(center, TransformMath.HandleScreenSize));
            }

            // 회전 핸들은 렌더와 힌트가 **같은 클램프된 위치**를 써야 한다 (R5).
            var stemStart = TransformMath.TopCenterWorld(element.TransformState, bounds);
            var rotate = TransformMath.ClampRotateHandle(
                TransformMath.RotateHandleWorld(element.TransformState, bounds),
                surfaceBounds,
                TransformMath.HandleScreenSize / 2);
            _decorationLayer.Children.Add(AnnotationVisualFactory.BuildRotateStem(stemStart, rotate));
            _decorationLayer.Children.Add(
                AnnotationVisualFactory.BuildHandle(rotate, TransformMath.HandleScreenSize));
        }
    }

    /// <summary>
    /// 그룹 장식 (R1): 공통 프레임 + 모서리 4핸들 + 회전 핸들 1개.
    /// 측면 4핸들을 그리지 않는 이유는 <see cref="SelectionGroup"/> 참고 — 비등방 그룹 스케일은
    /// 회전된 요소에 전단을 요구해 <see cref="ElementTransformState"/>로 표현할 수 없다.
    ///
    /// 회전 중에는 컨트롤러가 밀어 넣은 각도로 테두리·핸들·스템이 함께 돈다. 각도는 좌표 계산에만 쓰이며
    /// 히트 테스트도 같은 <see cref="GroupFrame"/> 계산을 쓰므로 "그려지는 위치 == 잡히는 위치"가 유지된다 (R5).
    /// </summary>
    private void DrawGroupDecorations(List<AnnotationElement> owned, Rect surfaceBounds, bool handles)
    {
        GroupFrame? current = _gestureGroupFrame;
        if (current is null && SelectionGroup.Frame(owned) is { } live)
        {
            current = new GroupFrame(live, 0);
        }
        if (current is not { } frame)
        {
            return;
        }

        _decorationLayer.Children.Add(
            AnnotationVisualFactory.BuildSelectionBorder(SelectionGroup.Corners(frame)));
        if (!handles)
        {
            return;
        }

        foreach (var handle in SelectionGroup.CornersClockwise)
        {
            _decorationLayer.Children.Add(AnnotationVisualFactory.BuildHandle(
                SelectionGroup.CornerCenter(frame, handle), TransformMath.HandleScreenSize));
        }

        // 회전 핸들은 렌더와 힌트가 **같은 클램프된 위치**를 써야 한다 (R5).
        var rotate = TransformMath.ClampRotateHandle(
            SelectionGroup.RotateHandle(frame), surfaceBounds, TransformMath.HandleScreenSize / 2);
        _decorationLayer.Children.Add(
            AnnotationVisualFactory.BuildRotateStem(SelectionGroup.TopCenter(frame), rotate));
        _decorationLayer.Children.Add(
            AnnotationVisualFactory.BuildHandle(rotate, TransformMath.HandleScreenSize));
    }

    /// <summary>진행 중인 휠 확대를 지금 원장에 확정한다 (R7) — 선택 삭제처럼 요소를 없애는 조작 직전용.</summary>
    public void FlushPendingTransforms() => _input.FlushPendingTransforms();

    private void OnSelectionChanged() => Dispatcher.Invoke(RedrawDecorations);

    private void OnElementTransformChanged(AnnotationElement element)
    {
        // R15/R23: 시각물 갱신은 RenderMatrixFor 단일 소유 지점을 경유한다.
        if (_visuals.TryGetValue(element.Id, out var visual))
        {
            AnnotationVisualFactory.ApplyRenderTransform(visual, element);
        }
        RedrawDecorations();
    }

    /// <summary>논리 서피스 좌표 → 가상 스크린 물리 좌표 (이관 대상 모니터 판별용).</summary>
    private (int X, int Y) ToPhysical(Point logical)
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var (x, y) = CoordinateSpace.ToPhysical(logical, scale);
        return (_monitor.WorkArea.X + x, _monitor.WorkArea.Y + y);
    }

    // ---- 입력 처리: SurfaceInputController에 전량 위임 ----

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_suspended)
        {
            return;
        }
        StylusProbe.Observe("마우스다운(승격)", e.StylusDevice);
        UpdateStylusCursor(e.StylusDevice);
        _input.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_suspended)
        {
            return;
        }
        StylusProbe.Observe("마우스이동(승격)", e.StylusDevice);
        UpdateStylusCursor(e.StylusDevice);
        _input.OnMouseMove(e);
    }

    // ---- R8: 스타일러스(와콤 펜 등) 입력 처리 및 커서 연동 ----

    protected override void OnStylusDown(StylusDownEventArgs e)
    {
        base.OnStylusDown(e);
        if (_suspended)
        {
            return;
        }
        StylusProbe.Observe("스타일러스다운", e.StylusDevice, e.Inverted);
        UpdateStylusCursor(e.StylusDevice);
    }

    protected override void OnStylusInAirMove(StylusEventArgs e)
    {
        base.OnStylusInAirMove(e);
        if (_suspended)
        {
            return;
        }
        StylusProbe.Observe("스타일러스공중이동", e.StylusDevice);
        UpdateStylusCursor(e.StylusDevice);
    }

    protected override void OnStylusOutOfRange(StylusEventArgs e)
    {
        base.OnStylusOutOfRange(e);
        ResetCursor();
    }

    protected override void OnStylusLeave(StylusEventArgs e)
    {
        base.OnStylusLeave(e);
        ResetCursor();
    }

    private void UpdateStylusCursor(StylusDevice? device)
    {
        if (_closed || Hwnd == 0 || !_state.IsInteractive || _suspended)
        {
            return;
        }

        Cursor = device?.Inverted == true ? CursorFactory.Eraser : CursorFor(_state.ActiveTool);
    }

    private void ResetCursor()
    {
        if (_closed || Hwnd == 0 || _suspended)
        {
            return;
        }

        Cursor = _state.IsInteractive ? CursorFor(_state.ActiveTool) : Cursors.Arrow;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_suspended)
        {
            return;
        }
        _input.OnMouseLeftButtonUp(e);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_suspended)
        {
            return;
        }
        _input.OnMouseWheel(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_suspended)
        {
            return;
        }
        _input.OnKeyDown(e);
    }

    // ---- ISurfaceHost: ARCH-2 NOACTIVATE 핸드셰이크 + ARCH-6 마우스 캡처만 위임 ----

    void ISurfaceHost.SetNoActivate(bool on) => WindowStyling.SetNoActivate(Hwnd, on);

    void ISurfaceHost.ActivateWindow() => Activate();

    void ISurfaceHost.CaptureMouse() => CaptureMouse();

    void ISurfaceHost.ReleaseMouseCapture() => ReleaseMouseCapture();

    DpiScale ISurfaceHost.GetDpi() => VisualTreeHelper.GetDpi(this);

    // ---- 커밋/렌더링 공통 ----

    private void OnElementAdded(AnnotationElement element)
    {
        var visual = AnnotationVisualFactory.BuildVisual(element);
        _visuals[element.Id] = visual;
        _inkCanvas.Children.Add(visual);
        RedrawDecorations();
    }

    private void OnElementRemoved(AnnotationElement element)
    {
        if (_visuals.Remove(element.Id, out var visual))
        {
            _inkCanvas.Children.Remove(visual);
        }
        _fading.OnElementRemoved(element);
        RedrawDecorations();
    }

    /// <summary>페이드 마감: 요소 시각물을 서서히 소멸시키고 완료 시 문서에서 제거.</summary>
    public void AnimateFadeOut(AnnotationElement element, TimeSpan fadeLength, Action onCompleted)
    {
        if (!_visuals.TryGetValue(element.Id, out var visual))
        {
            onCompleted();
            return;
        }
        var animation = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.0, fadeLength)
        {
            FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd,
        };
        animation.Completed += (_, _) => onCompleted();
        visual.BeginAnimation(OpacityProperty, animation);
    }

    /// <summary>
    /// <see cref="IIdleScheduler"/>의 WPF 어댑터 (R7). <c>DispatcherTimer</c> +
    /// <c>DispatcherPriority.Background</c> + Stop 후 Start 디바운스 — 원래
    /// <c>SurfaceInputController.StepWheelScale</c>에 있던 관용구 그대로다.
    ///
    /// <c>Task</c>/<c>await</c>로 바꾸지 않는다: 확정 경로가 <c>TransformState</c>를 쓰고
    /// 소유 문서에 알리고 <see cref="UndoLedger"/>에 append 한다 — 전부 UI 스레드 전용이다.
    /// </summary>
    private sealed class DispatcherIdleScheduler(Dispatcher dispatcher) : IIdleScheduler
    {
        private DispatcherTimer? _timer;

        public event Action? Tick;

        public void Restart(TimeSpan interval)
        {
            _timer ??= new DispatcherTimer(DispatcherPriority.Background, dispatcher);
            _timer.Interval = interval;
            _timer.Tick -= OnTimerTick;
            _timer.Tick += OnTimerTick;
            _timer.Stop();
            _timer.Start();
        }

        public void Cancel() => _timer?.Stop();

        private void OnTimerTick(object? sender, EventArgs e) => Tick?.Invoke();
    }
}
