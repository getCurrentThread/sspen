using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SSPen.Interop;

namespace SSPen.Annotation;

/// <summary>
/// <see cref="ContentSurfaceWindow"/>가 자신에게 위임해야 하는 최소 창 조작 집합.
/// ARCH-2 텍스트 도구 NOACTIVATE 핸드셰이크와 ARCH-6 마우스 캡처만 창에 위임하고,
/// 그 외 입력 상태 머신은 <see cref="SurfaceInputController"/>가 창 참조 없이 소유한다.
/// </summary>
public interface ISurfaceHost
{
    void SetNoActivate(bool on);
    void ActivateWindow();
    void CaptureMouse();
    void ReleaseMouseCapture();
    DpiScale GetDpi();
}

/// <summary>
/// 컨트롤러가 WPF 비주얼 트리 없이는 스스로 알 수 없는 값들의 주입 지점.
/// 시계는 <see cref="WheelScaleSession"/>와 <see cref="FadeSchedulerCore"/>가 이미 주입받는데
/// 컨트롤러만 그 경계에서 <c>DateTime.UtcNow</c>로 다시 하드코딩하고 있었고,
/// 서피스 논리 경계는 창이 유일 소유자인데 컨트롤러가 <b>따로 한 벌 더</b> 계산하고 있었다 (R5).
/// </summary>
public sealed record SurfaceInputSeams
{
    /// <summary>
    /// 주입 시계. 휠 노치 코얼레싱의 450ms 유휴 판정과 페이드 예약 마감이 전부 이 값에서 나온다 (R7).
    /// 프로덕션 값을 <b>충실히 감싸므로</b> 기본값이 정당하다 — 배선을 빠뜨려도 동작이 달라지지 않는다.
    /// </summary>
    public Func<DateTime> Now { get; init; } = () => DateTime.UtcNow;

    /// <summary>
    /// 서피스 논리 경계 (R5). 렌더(<c>ContentSurfaceWindow.RedrawDecorations</c>)와 힌트(히트 테스트)가
    /// <b>같은 값</b>을 써야 "그려지는 위치 == 잡히는 위치"가 성립한다. 창이 유일 소유자이므로
    /// 충실한 프로덕션 기본값이 없다 — 그래서 <c>required</c>다.
    ///
    /// 기본값을 두면 안 되는 이유: <see cref="Rect.Empty"/>는 "경계 없음"이 아니라
    /// <see cref="TransformMath.ClampRotateHandle"/>의 <b>다른 코드 경로</b>("클램프하지 않음")다.
    /// 배선을 빠뜨린 테스트가 조용히 프로덕션과 다른 경로를 타는 대신 컴파일 에러가 나야 한다.
    ///
    /// <b><see cref="Rect"/> 값이 아니라 <see cref="Func{TResult}"/>인 이유</b>: 창 생성 시점에는
    /// 아직 measure/arrange가 돌지 않아 <c>ActualWidth/ActualHeight</c>가 0이다. 그때 값을 얼려 두면
    /// <c>new Rect(0,0,0,0)</c>이 되는데 이것은 <c>IsEmpty == false</c>라 클램프 경로로 들어가고,
    /// <c>left &gt; right</c> 분기가 중심점을 돌려주어 <b>모든 회전 핸들이 (0,0)으로 붕괴</b>한다.
    /// 매 히트 테스트마다 호출한다 — 필드에 캐시하지 말 것.
    /// </summary>
    public required Func<Rect> SurfaceBounds { get; init; }

    /// <summary>
    /// 휠 유휴 디바운스 (R7). 창이 유일 소유자이므로 충실한 프로덕션 기본값이 없다 — 그래서 <c>required</c>다.
    /// 기본값을 두면 배선 누락이 컴파일 에러가 아니라 첫 노치의 NRE가 된다.
    /// </summary>
    public required IIdleScheduler IdleScheduler { get; init; }
}

/// <summary>
/// 마우스/키보드 입력 상태 머신 (획·도형·텍스트·지우개). 창 참조 없이 <see cref="ISurfaceHost"/>로만
/// 창과 통신한다 (ARCH-2/ARCH-6 핸드셰이크만 위임).
///
/// 마우스 다운이 무엇이 되는가와 휠 한 노치의 중재는 순수 표 <see cref="SurfaceInputRouter"/>가
/// 소유한다 (D4/R7) — 여기 남는 것은 그 판정의 적용부다.
///
/// 선택 도구의 판정은 순수 협력자들이 소유한다: 히트 우선순위 사다리는
/// <see cref="SelectionGesturePlanner"/>, 시작 상태 스냅샷과 R15 집행은 <see cref="DragBaseStates"/>,
/// 제스처 판정 상수·술어는 <see cref="SelectionGestureRules"/>, 그룹 기하는 <see cref="SelectionGroup"/>,
/// 휠 확대/축소 정책 전체(세션·시작 상태·잡은 요소·유휴 확정)는 <see cref="WheelScaleController"/>가,
/// 선택 전체 이동 계산은 <see cref="SelectionOperations"/>가, 원장에 실을 델타 목록은
/// <see cref="TransformCommitPlan"/>이 소유한다.
///
/// 획·도형·텍스트 쪽도 마찬가지다: 시작 시점 스타일 동결과 획 누적은
/// <see cref="GestureStyleSnapshot"/>/<see cref="StrokeAccumulator"/>, 미리보기 끝점과 커밋 판정은
/// <see cref="ShapeGestureRules"/>/<see cref="TextCommitRules"/>가 가진다.
///
/// 이 클래스에 남는 것은 진행 중 필드와 창·문서·원장으로 흘려보내는 배선뿐이다 (ARCH-2).
/// 그 배선 중 <b>순서 자체가 계약</b>인 것은 <see cref="CancelActiveInput"/> 하나이며,
/// 다섯 가지 취소 의미와 그 순서 의무는 그 메서드의 문서가 소유한다.
/// </summary>
public sealed class SurfaceInputController(
    Canvas inkCanvas,
    AppState state,
    AnnotationDocument document,
    UndoLedger ledger,
    FadingInkController fading,
    ISurfaceHost host,
    SelectionModel selection,
    Func<AnnotationElement, AnnotationDocument?> ownerLookup,
    Func<AnnotationDocument, double> dpiOf,
    Action<Rect?> setMarquee,
    // 마퀴(setMarquee)와 타입을 묶지 않는다 — 마퀴는 설계상 영원히 축 정렬이다 (SEL-B-1).
    Action<GroupFrame?> setGestureGroupFrame,
    Action<IReadOnlyList<TransformDelta>, Point?> commitTransform,
    Action requestClickThrough,
    SurfaceInputSeams seams)
{
    // 선택 도구 드래그 상태 (SEL-7)
    private SelectionDragKind _dragKind;
    private Point _dragStart;
    private HandleKind _dragHandle;
    private GroupHandleKind _dragGroupHandle;
    private AnnotationElement? _dragHandleTarget;

    /// <summary>드래그 시작 상태 + R15 집행 (요소 참조를 붙잡는 이유는 DragBaseStates 문서 참조).</summary>
    private readonly DragBaseStates _base = new(ownerLookup, document);

    /// <summary>
    /// 제스처 시작 시점에 <b>동결</b>된 그룹 프레임 (R1). 살아있는 경계로 매 프레임 재계산하면
    /// 회전 중 피벗이 표류하고 잡은 핸들이 커서 밑에서 빠져나간다 — "매 프레임 드래그 시작 상태에서
    /// 재계산"(<see cref="UpdateSelectGesture"/>) 규약의 프레임판이다.
    ///
    /// 각도는 여기에 <b>싣지 않는다</b> — 실으면 <see cref="SelectionGroup.ScaleFactor"/>/
    /// <c>AnchorCorner</c>/휠 경로로 새어 나간다. 그려지는 프레임의 각도는 창으로만 흐른다 (SEL-LIM-6).
    /// </summary>
    private Rect _groupFrame;

    /// <summary>빈 곳 제스처를 시작할 때 선택이 있었는가 (R5: 제자리 클릭이면 업에서 클릭 통과로 전환).</summary>
    private bool _hadSelectionOnPress;

    private WheelScaleController? _wheel;

    /// <summary>
    /// 휠 확대/축소 (R7): 연속 노치를 하나의 원장 항목으로 묶는다.
    ///
    /// 필드 이니셜라이저로 만들 수 없어 최초 사용 시점에 한 번만 만든다 — R15의 <b>유일</b> 집행 지점인
    /// <c>_base.Apply</c>를 넘겨야 하는데 필드 이니셜라이저는 다른 인스턴스 필드를 참조할 수 없고(CS0236),
    /// 주 생성자에는 본문이 없다. <see cref="DragBaseStates"/>를 한 벌 더 만들어 피하면
    /// <c>TransformState</c> 집행자가 둘이 되므로 그쪽이 더 나쁘다.
    /// </summary>
    private WheelScaleController WheelScale => _wheel ??= new(
        ownerLookup, document, _base.Apply, commitTransform, seams.Now, seams.IdleScheduler);

    // 진행 중 획/도형/텍스트 상태
    private StrokeAccumulator? _stroke;
    private Polyline? _activePolyline;
    private bool _eraserDragging;          // 지우개 드래그 삭제 중 (사용자 조타 12차)
    private Point _shapeStart;
    private Shape? _previewShape;
    private ShapeKind _previewKind;
    private ShapeStyle _activeShapeStyle;  // 도형 시작 시점 페이딩 판정 (사용자 요청 17차)
    private TextBox? _activeTextBox;
    private Point _textOrigin;
    private TextStyle _activeTextStyle;    // 텍스트 시작 시점 페이딩 판정 (사용자 요청 17차)

    // ---- WPF 이벤트 어댑터 ----
    //
    // 좌표·버튼 상태·휠 부호·Shift·IsMouseOver만 벗겨 아래 Point 진입점에 넘긴다.
    //
    // D3: WPF Keyboard.Modifiers는 스레드 로컬이라 이 창(영구 NOACTIVATE)에서 항상 None이고,
    // 전역 핫키로 도구를 켠 정상 흐름에서 Shift 스냅(수평/수직/정비율)·다중 선택·마퀴 누적이
    // 조용히 죽는다. 그래서 KeyboardState(GetAsyncKeyState)로 읽는다.
    // 이벤트당 **한 번만** 읽는 것은 동작 보존이다 — 옮기기 전의 Shift 읽기 지점들은 한 이벤트가
    // 그중 최대 하나에만 도달하도록 if/else-if 사다리와 switch로 서로 배타적이었다.
    //
    // Handled는 **반환값이 참일 때만** 세운다. `e.Handled = 반환값`으로 대입하면 상위에서
    // 이미 세워 둔 Handled를 false로 되돌려, 오늘 서피스가 통과시키는 입력의 소비 여부가 바뀐다.

    public void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (PointerDown(e.GetPosition(inkCanvas), KeyboardState.Shift))
        {
            e.Handled = true;
        }
    }

    public void OnMouseMove(MouseEventArgs e)
    {
        // 눌리지 않은 이동을 여기서 끊는다 — 호버 이동마다 GetPosition/GetAsyncKeyState를
        // 부르지 않기 위해서다. 판정의 주인은 아래 PointerMove의 leftPressed 가드다.
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }
        PointerMove(e.GetPosition(inkCanvas), KeyboardState.Shift, leftPressed: true);
    }

    public void OnMouseLeftButtonUp(MouseButtonEventArgs e) =>
        PointerUp(e.GetPosition(inkCanvas), KeyboardState.Shift);

    public void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Wheel(e.GetPosition(inkCanvas), e.Delta > 0 ? +1 : -1))
        {
            e.Handled = true;
        }
    }

    public void OnKeyDown(KeyEventArgs e)
    {
        // 키 판정은 어댑터가 소유한다 — Escape()는 "열린 텍스트 상자를 확정한다"이므로
        // 다른 키에 대해 부르면 편집 중 텍스트가 조용히 커밋된다.
        if (e.Key == Key.Escape && Escape())
        {
            e.Handled = true;
        }
    }

    // ---- Point 진입점 (WPF 이벤트 인자 없이 헤드리스로 구동 가능) ----

    /// <summary>
    /// 마우스 다운 진입점. 반환값이 곧 <c>e.Handled</c> 판정이다 — 비인터랙티브와
    /// 텍스트 바깥 클릭은 Handled를 <b>세우지 않는다</b>(그 클릭은 소비되지 않는다).
    /// void로 두고 어댑터가 무조건 대입하면 서피스가 오늘 통과시키는 클릭을 삼킨다.
    /// </summary>
    public bool PointerDown(Point pos, bool shift) => PointerDown(pos, shift, IsOverActiveTextBox());

    /// <summary>
    /// <paramref name="overActiveEditor"/>는 <c>Point</c>에서 유도할 수 없는 WPF 히트테스트 입력이다 (ARCH-2).
    /// <c>TextBox.IsMouseOver</c>는 입력 매니저가 <b>살아있는 비주얼 트리</b>에 대해 유지하는 플래그이고,
    /// 그 상자는 BorderThickness(1)·MinWidth=24라 measure/arrange가 돌지 않는 헤드리스에서는
    /// ActualWidth가 0이다 — Canvas.GetLeft/GetTop 기하로 대체하는 것은 헤드리스 편의를 위해
    /// 프로덕션 동작을 바꾸는 것이므로 금지한다.
    /// </summary>
    public bool PointerDown(Point pos, bool shift, bool overActiveEditor)
    {
        // 판정은 순수 표가 소유한다 (D4/ARCH-2). 여기 남는 것은 적용부와 Handled 배선뿐이다.
        var gesture = SurfaceInputRouter.RouteDown(
            state.ActiveTool,
            state.IsInteractive,
            textEditing: _activeTextBox is not null,
            overActiveEditor: overActiveEditor);

        if (gesture == SurfaceGesture.Ignore)
        {
            return false;
        }

        if (gesture == SurfaceGesture.CommitTextOnly)
        {
            CommitText();
            return false; // Handled 미대입 — 오늘 이 클릭은 소비되지 않는다 (ARCH-2).
        }

        switch (gesture)
        {
            case SurfaceGesture.StartStroke:
                StartStroke(pos);
                break;
            case SurfaceGesture.StartLine:
                StartShape(ShapeKind.Line, pos);
                break;
            case SurfaceGesture.StartArrow:
                StartShape(ShapeKind.Arrow, pos);
                break;
            case SurfaceGesture.StartRectangle:
                StartShape(ShapeKind.Rectangle, pos);
                break;
            case SurfaceGesture.StartEllipse:
                StartShape(ShapeKind.Ellipse, pos);
                break;
            case SurfaceGesture.BeginTextEdit:
                BeginTextEdit(pos);
                break;
            case SurfaceGesture.EraseAndDrag:
                // 사용자 조타: 클릭 + 드래그 삭제. 캡처로 창 밖까지 추적.
                EraseAt(pos);
                _eraserDragging = true;
                host.CaptureMouse();
                break;
            case SurfaceGesture.BeginSelect:
                BeginSelectGesture(pos, shift);
                break;
        }
        return SurfaceInputRouter.MarksHandled(gesture);
    }

    public void PointerMove(Point pos, bool shift, bool leftPressed)
    {
        if (!leftPressed)
        {
            return;
        }

        if (_stroke is not null && _activePolyline is not null)
        {
            if (_stroke.TryAppend(pos))
            {
                _activePolyline.Points.Add(pos);
            }
        }
        else if (_previewShape is not null)
        {
            var end = ShapeGestureRules.ResolveEnd(_previewKind, _shapeStart, pos, shift);
            AnnotationVisualFactory.UpdateShapeVisual(_previewShape, _previewKind, _shapeStart, end);
        }
        else if (_eraserDragging && state.ActiveTool == ToolKind.Eraser && state.IsInteractive)
        {
            // 사용자 조타 12차: 드래그 지우기 (기존 Non-Goal 4 펜스를 명시 조타로 해제).
            EraseAt(pos);
        }
        else if (_dragKind != SelectionDragKind.None && state.IsInteractive)
        {
            UpdateSelectGesture(pos, shift);
        }
    }

    public void PointerUp(Point pos, bool shift)
    {
        // D4: 이동 경로(:PointerMove)에만 있던 인터랙티브 가드를 업에도 건다. 비인터랙티브로 전환된 뒤
        // 도착한 버튼 업이 제스처를 확정하면 보이지 않는 조작이 원장에 실린다.
        if (!state.IsInteractive)
        {
            CancelActiveInput();
            return;
        }

        if (_stroke is not null)
        {
            CommitStroke();
        }
        else if (_previewShape is not null)
        {
            CommitShape(pos, shift);
        }
        else if (_dragKind != SelectionDragKind.None)
        {
            EndSelectGesture(pos, shift);
        }
        _eraserDragging = false;
        host.ReleaseMouseCapture();
    }

    /// <summary>휠 진입점. 반환값이 <c>e.Handled</c> 판정이다 — 모니터에 걸친 선택(SEL-LIM-5)과
    /// 설정이 꺼진 비선택 도구(WI-16)는 오늘 Handled를 세우지 않고 휠을 통과시킨다.</summary>
    public bool Wheel(Point pos, int notches)
    {
        // 중재는 순수 표가 소유한다 (R7/SEL-5/WI-16). SEL-LIM-5 게이트만 여기 남는다.
        switch (SurfaceInputRouter.RouteWheel(
            state.ActiveTool,
            state.IsInteractive,
            dragActive: _dragKind != SelectionDragKind.None,
            state.WheelAdjustsPenSize))
        {
            case WheelVerdict.SwallowOnly:
                return true;

            case WheelVerdict.ScaleSelection:
                var owned = SelectionGroup.OwnedBy(document, selection);
                // SEL-LIM-5: 모니터에 걸친 선택은 확대/축소하지 않는다. 고정점이 이 서피스의 논리 좌표라
                // 다른 원점·DPI를 쓰는 문서의 요소에 그대로 먹이면 엉뚱한 곳으로 흩어진다.
                if (SelectionGroup.HandlesGrabbable(owned.Count, selection.Count))
                {
                    // dragActive를 여기서 다시 유도해 넘긴다 — 위 SwallowOnly 판정 덕분에 오늘은 항상
                    // false지만, 그 행이 언젠가 사라져도 드래그 중 휠이 조용히 되살아나지 않게 하는
                    // 이중 방어다 (R7).
                    WheelScale.Step(owned, pos, notches, dragActive: _dragKind != SelectionDragKind.None);
                    return true;
                }
                return false;

            case WheelVerdict.StepThickness:
                // 마우스 휠로 펜 크기 조정 (WI-16 설정 연동).
                state.StepThickness(notches);
                return true;

            default:
                return false;
        }
    }

    /// <summary>ESC 진입점 — 열린 텍스트 상자를 확정한다. 반환값이 <c>e.Handled</c> 판정이다 (ARCH-2).</summary>
    public bool Escape()
    {
        if (_activeTextBox is null)
        {
            return false;
        }
        CommitText();
        return true;
    }

    // ---- 선택 도구 입력 상태 머신 (SEL-7) ----

    /// <summary>
    /// 마우스 다운 적용부. 판정은 전부 <see cref="SelectionGesturePlanner.Plan"/>이 하고(히트 우선순위
    /// SEL-7), 여기서는 그 계획을 <b>고정된 순서로</b> 옮겨 적기만 한다.
    ///
    /// 순서가 계약이다 — 필드마다 도는 루프로 바꾸면 <c>SelectHit</c>과 스냅샷 순서가 뒤집혀
    /// "고르자마자 끌기"가 조용히 무동작이 된다 (SEL-AC-9: <see cref="MoveSelection"/>은 시작 상태가
    /// 없는 요소를 예외도 로그도 없이 건너뛴다).
    /// </summary>
    private void BeginSelectGesture(Point pos, bool shift)
    {
        WheelScale.Flush(commit: true); // 휠 확대 중 클릭은 그 확대를 먼저 확정한다 (원장 순서 보존).

        // 캡처를 잃어 버튼 업이 유실된 제스처가 그려지는 프레임에 각도를 남길 수 있다.
        // 마우스 다운 시점에는 각도가 반드시 0이어야 한다 — 그래야 아래 히트 테스트,
        // R6 내부 판정(IsInsideSelectionFrame), 휠 고정점(WheelPivot)이 전부
        // "화면에 그려진 것과 같은 축 정렬 프레임"을 본다.
        // ResetSelectGesture()를 부르면 안 된다: 진행 중이던 변형을 커밋도 롤백도 하지 않아
        // 원장에 없는 변형이 화면에 남고 실행취소로 지울 수 없게 된다 (CancelActiveInput의 규칙).
        setGestureGroupFrame(null);

        _dragStart = pos;

        var owned = SelectionGroup.OwnedBy(document, selection);
        var plan = SelectionGesturePlanner.Plan(
            document.Elements, owned, selection.Count, selection.Contains, pos, shift,
            seams.SurfaceBounds());

        if (plan.ToggleHit is { } toggle)
        {
            selection.Toggle(toggle);
            return; // 토글은 이동을 시작하지 않는다 (SEL-AC-3).
        }

        if (plan.ClearSelection)
        {
            selection.Clear();
        }
        // ★ 스냅샷보다 반드시 **앞**이다 (GesturePlan.SelectHit 문서 — SEL-AC-9).
        if (plan.SelectHit is { } pick)
        {
            selection.Set([pick]);
        }

        _dragKind = plan.Kind;
        // 핸들 두 필드는 non-null일 때만 쓴다: 오늘도 ResetSelectGesture가 이 둘을 지우지 않고
        // 각자 자기 _dragKind 아래에서만 읽히므로, 무조건 기본값으로 덮으면 근거 없는 동작 변화가 된다.
        if (plan.Handle is { } handle)
        {
            _dragHandle = handle;
        }
        if (plan.GroupHandle is { } groupHandle)
        {
            _dragGroupHandle = groupHandle;
        }
        _dragHandleTarget = plan.Target;
        if (plan.FrozenBasis is { } frozen)
        {
            _groupFrame = frozen;
        }

        // 마퀴는 시작 상태를 잡지 않는다 (오늘도 4)번 분기에 스냅샷이 없다) — 잡으면
        // CancelActiveInput의 롤백이 선택 전원에 무의미한 상태 대입과 알림을 뿌린다 (R15).
        if (plan.Kind != SelectionDragKind.None && !plan.StartsMarquee)
        {
            _base.Snapshot(selection, plan.Target);
        }

        // 그려지는 프레임은 회전일 때만 밀린다 (판정은 플래너 안 GestureFrame 한 번의 호출이 소유).
        // null이면 밀지 않는다 — 머리에서 이미 null을 밀었고 창이 같은 값을 덧걸러 낸다.
        if (plan.DrawnFrame is { } guide)
        {
            setGestureGroupFrame(guide);
        }

        if (plan.StartsMarquee)
        {
            _hadSelectionOnPress = plan.HadSelectionOnPress;
            setMarquee(new Rect(pos, pos));
        }
        if (plan.Captures)
        {
            host.CaptureMouse();
        }
    }

    /// <summary>
    /// 매 프레임 **드래그 시작 상태에서 재계산**한다 (직전 프레임 결과 누적 금지).
    /// 누적하면 부동소수 오차가 프레임마다 쌓여 요소가 서서히 어긋나고, 취소 복원 기준도 사라진다.
    /// </summary>
    private void UpdateSelectGesture(Point pos, bool shift)
    {
        if (_dragKind == SelectionDragKind.Marquee)
        {
            setMarquee(new Rect(_dragStart, pos));
            return;
        }
        if (_base.BaseStates is not { } baseStates)
        {
            return;
        }

        switch (_dragKind)
        {
            case SelectionDragKind.Move:
                // 이동은 선택 전체에 적용된다 (SEL-AC-9). 모니터에 걸친 선택에서도 이것만은 허용된다 (SEL-LIM-5).
                MoveSelection(baseStates, pos - _dragStart);
                break;

            // 단일 선택(Count==1) 전용 경로. 8핸들 **비등방** 크기 조절과 요소 자기 중심 회전은
            // 대상이 하나로 확정될 때만 모호하지 않다. 다중 선택은 위 GroupScale/GroupRotate가 맡는다 (R1).
            case SelectionDragKind.Scale:
                if (_dragHandleTarget is { } scaleTarget
                    && baseStates.TryGetValue(scaleTarget.Id, out var scaleStart))
                {
                    _base.Apply(
                        scaleTarget,
                        TransformMath.ScaleLocal(scaleStart, scaleTarget.LocalBounds, _dragHandle, pos));
                }
                break;

            case SelectionDragKind.Rotate:
                if (_dragHandleTarget is { } rotateTarget
                    && baseStates.TryGetValue(rotateTarget.Id, out var rotateStart))
                {
                    _base.Apply(
                        rotateTarget,
                        TransformMath.Rotate(
                            rotateStart,
                            rotateTarget.LocalBounds,
                            _dragStart,
                            pos,
                            shift));
                }
                break;

            // R1: 그룹 변형은 **동결된 프레임**을 기준으로 선택 전원에 같은 값을 먹인다.
            // 등방 스케일과 회전만 있는 이유는 SelectionGroup의 XML 문서 참고 (전단 표현 불가).
            case SelectionDragKind.GroupScale:
            {
                double factor = TransformMath.ClampGroupFactor(
                    SelectionGroup.ScaleFactor(_groupFrame, _dragGroupHandle, pos), baseStates.Values);
                var pivot = SelectionGroup.AnchorCorner(_groupFrame, _dragGroupHandle);
                foreach (var element in selection.Elements)
                {
                    if (baseStates.TryGetValue(element.Id, out var start))
                    {
                        _base.Apply(
                            element, TransformMath.ScaleAbout(start, element.LocalBounds, pivot, factor));
                    }
                }
                break;
            }

            case SelectionDragKind.GroupRotate:
            {
                // 피벗·각도·가이드 프레임을 **한 번에** 받는다. 따로 구하면 가이드와 잉크가 서로 다른
                // 값을 쓰는 상태가 표현 가능해지는데, 그 어긋남이 바로 "그룹을 회전해도 테두리 가이드가
                // 같이 안 도는" 증상이었다 (SelectionGroup.GroupRotateStep 참고).
                // D3: Shift는 이벤트 진입점(어댑터)이 KeyboardState로 한 번 읽어 여기까지 흘린다 —
                // 스레드 로컬 Keyboard.Modifiers는 영구 NOACTIVATE 서피스에서 항상 None이다.
                var step = SelectionGroup.RotateStep(_groupFrame, _dragStart, pos, shift);

                // 루프보다 **먼저** 미는 이유: 아래 상태 대입이 유발하는 재그리기가
                // 이미 새 각도를 보게 한다. 각도는 _dragStart 기준 누적 증분이라 프레임 각에
                // 더해 나가지 않는다 ("직전 프레임 결과 누적 금지" 규약).
                setGestureGroupFrame(step.Guide);

                foreach (var element in selection.Elements)
                {
                    if (baseStates.TryGetValue(element.Id, out var start))
                    {
                        _base.Apply(
                            element,
                            TransformMath.RotateAbout(
                                start, element.LocalBounds, step.Pivot, step.DeltaDegrees));
                    }
                }
                break;
            }
        }
    }

    /// <summary>
    /// 선택 전체 이동 (SEL-AC-9). 계산은 <see cref="SelectionOperations.PlanMove"/>가 하고,
    /// 이 어댑터는 계획을 R15의 유일한 집행 지점(<see cref="DragBaseStates.Apply"/>)으로 흘리기만 한다.
    ///
    /// 순회 대상은 <c>owned</c>가 아니라 <c>selection.Elements</c>다 — 이 서피스가 소유하지 않은
    /// 요소도 함께 움직여야 선택이 통째로 따라온다 (SEL-LIM-5).
    ///
    /// D1: 다른 모니터 소속 요소에는 <b>DPI 환산</b>이 필요하다 —
    /// <see cref="SelectionOperations.ScaleDisplacementForDpi"/> 참고. 소유 문서를 못 찾을 때의
    /// <c>?? document</c> 폴백은 여기 남는다: 순수 코어는 문서도 소유자 조회도 모른다.
    /// <c>host.GetDpi()</c>는 이동 프레임당 <b>정확히 한 번</b>이다 (요소마다 다시 묻지 않는다).
    /// </summary>
    private void MoveSelection(IReadOnlyDictionary<long, ElementTransformState> baseStates, Vector delta)
    {
        var plan = SelectionOperations.PlanMove(
            selection.Elements,
            baseStates,
            delta,
            host.GetDpi().DpiScaleX,
            element => dpiOf(ownerLookup(element) ?? document));

        foreach (var (element, next) in plan)
        {
            _base.Apply(element, next);
        }
    }

    private void EndSelectGesture(Point pos, bool shift)
    {
        if (_dragKind == SelectionDragKind.Marquee)
        {
            setMarquee(null);

            // R2/R5: 끌지 않고 제자리에서 뗐다면 마퀴가 아니라 **해제 클릭**이다.
            // 선택이 있었을 때만 클릭 통과로 넘어간다 — 아무것도 안 고른 상태의 빈 클릭까지
            // 흡수하면 선택 도구를 켜자마자 도구가 해제되어 아무것도 고를 수 없다.
            if (SelectionGestureRules.IsStationaryClick(_dragStart, pos))
            {
                bool engage = SelectionGestureRules.ShouldEngageClickThrough(_hadSelectionOnPress, _dragStart, pos);
                _hadSelectionOnPress = false;
                ResetSelectGesture();
                if (engage)
                {
                    requestClickThrough();
                }
                return;
            }

            _hadSelectionOnPress = false;
            var hits = SelectionGeometry.HitMarquee(document.Elements, new Rect(_dragStart, pos));
            if (shift)
            {
                foreach (var element in hits)
                {
                    selection.Add(element);
                }
            }
            else
            {
                selection.Set(hits);
            }
            ResetSelectGesture();
            return;
        }

        if (_base.Active)
        {
            var deltas = TransformCommitPlan.Build(_base.Pairs, ownerLookup, document);
            if (deltas.Count > 0)
            {
                // 이관 판정(f7)과 원장 기록은 컴포지션 루트가 소유한다 (SEL-14/P7).
                commitTransform(deltas, TransformCommitPlan.CarriesDropPoint(_dragKind) ? pos : null);
            }
        }
        ResetSelectGesture();
    }

    private void ResetSelectGesture()
    {
        _dragKind = SelectionDragKind.None;
        _dragHandleTarget = null;
        _base.Reset();
        _groupFrame = Rect.Empty;
        // 각도의 유일한 소멸 지점 — 이후 장식은 다시 살아있는 축 정렬 경계로 그린다 (SEL-LIM-6).
        setGestureGroupFrame(null);
    }

    private void StartStroke(Point pos)
    {
        // 시작 시점 판정 캡처 (아키텍트 자문): 드래그 중 핫키로 도구가 바뀌거나 퀵컬러/휠로
        // 색·굵기가 바뀌어도, 이 획의 스타일(색·굵기·형광펜·페이딩 여부)은 시작 당시 스냅샷을 따른다.
        var style = GestureStyleSnapshot.ForStroke(state);
        _stroke = new StrokeAccumulator(pos, style);
        _activePolyline = new Polyline
        {
            Stroke = AnnotationVisualFactory.StrokeBrush(style.Color, style.IsHighlighter),
            StrokeThickness = style.Thickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        _activePolyline.Points.Add(pos);
        inkCanvas.Children.Add(_activePolyline);
        host.CaptureMouse();
    }

    private void CommitStroke()
    {
        if (_stroke is null || _activePolyline is null)
        {
            return;
        }
        var style = _stroke.Style;
        var element = new StrokeElement(_stroke.Points, style.Color, style.Thickness, style.IsHighlighter);
        inkCanvas.Children.Remove(_activePolyline);
        _stroke = null;
        _activePolyline = null;
        CommitElement(element, fade: style.IsFading);
    }

    /// <summary>
    /// 진행 중 획 <b>폐기</b> (커밋이 아니다 — 원장 항목이 없으므로 미리보기 시각물만 지운다).
    /// <c>Children.Remove</c>가 WPF라 헤드리스 코어로 내리지 않고 얇은 UI 어댑터로 남긴다.
    /// </summary>
    private void DiscardStroke()
    {
        if (_activePolyline is null)
        {
            return;
        }
        inkCanvas.Children.Remove(_activePolyline);
        _stroke = null;
        _activePolyline = null;
    }

    private void StartShape(ShapeKind kind, Point pos)
    {
        _shapeStart = pos;
        _previewKind = kind;
        // 시작 시점 스냅샷: 드래그 중 색/굵기/페이딩 토글 변경이 미리보기·커밋 스타일을 어긋내지 않도록 고정.
        _activeShapeStyle = GestureStyleSnapshot.ForShape(state);
        _previewShape = AnnotationVisualFactory.CreateShapeVisual(kind, _activeShapeStyle.Color, _activeShapeStyle.Thickness);
        AnnotationVisualFactory.UpdateShapeVisual(_previewShape, kind, pos, pos);
        inkCanvas.Children.Add(_previewShape);
        host.CaptureMouse();
    }

    private void CommitShape(Point rawEnd, bool shift)
    {
        if (_previewShape is null)
        {
            return;
        }
        var end = ShapeGestureRules.ResolveEnd(_previewKind, _shapeStart, rawEnd, shift);
        inkCanvas.Children.Remove(_previewShape);
        _previewShape = null;

        if (!ShapeGestureRules.ShouldCommit(_shapeStart, end))
        {
            return;
        }
        var element = new ShapeElement(_previewKind, _shapeStart, end, _activeShapeStyle.Color, _activeShapeStyle.Thickness);
        CommitElement(element, fade: _activeShapeStyle.IsFading);
    }

    /// <summary>진행 중 도형 <b>폐기</b> (커밋이 아니다 — <see cref="DiscardStroke"/>와 같은 이유).</summary>
    private void DiscardShape()
    {
        if (_previewShape is null)
        {
            return;
        }
        inkCanvas.Children.Remove(_previewShape);
        _previewShape = null;
    }

    // ---- 텍스트 도구 (ARCH-2: NOACTIVATE 일시 해제 핸드셰이크로 한국어 IME 지원) ----

    private void BeginTextEdit(Point pos)
    {
        if (_activeTextBox is not null)
        {
            CommitText();
        }
        _textOrigin = pos;
        // 시작 시점 스냅샷: 편집 중 색/폰트 크기/페이딩 토글 변경이 결과 텍스트 스타일을 어긋내지 않도록 고정.
        _activeTextStyle = GestureStyleSnapshot.ForText(state);
        _activeTextBox = new TextBox
        {
            FontFamily = new FontFamily(TextCommitRules.FontFamilyName),
            FontSize = _activeTextStyle.FontSize,
            Foreground = new SolidColorBrush(_activeTextStyle.Color),
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, 0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(1),
            MinWidth = 24,
            AcceptsReturn = true,
        };
        Canvas.SetLeft(_activeTextBox, pos.X);
        Canvas.SetTop(_activeTextBox, pos.Y);
        inkCanvas.Children.Add(_activeTextBox);

        // NOACTIVATE 해제 → 활성화 → 포커스 (커밋 시 복원).
        host.SetNoActivate(false);
        host.ActivateWindow();
        _activeTextBox.Focus();
        _activeTextBox.LostKeyboardFocus += (_, _) => CommitText();
    }

    private void CommitText()
    {
        var box = _activeTextBox;
        if (box is null)
        {
            return;
        }
        _activeTextBox = null;
        string text = box.Text;
        inkCanvas.Children.Remove(box);
        host.SetNoActivate(true);

        if (TextCommitRules.ProducesElement(text))
        {
            var dpi = host.GetDpi();
            var formatted = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily(TextCommitRules.FontFamilyName), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                _activeTextStyle.FontSize,
                Brushes.Black,
                dpi.PixelsPerDip);
            var element = new TextElement(
                _textOrigin,
                text,
                _activeTextStyle.Color,
                _activeTextStyle.FontSize,
                TextCommitRules.FloorMeasured(new Size(formatted.WidthIncludingTrailingWhitespace, formatted.Height)));
            CommitElement(element, fade: _activeTextStyle.IsFading);
        }
    }

    /// <summary>
    /// 커서가 편집 중 텍스트 상자 위인가 (ARCH-2). <c>IsMouseOver</c>는 WPF 입력 매니저가
    /// <b>살아있는 비주얼 트리</b>에 대해 유지하는 히트테스트 플래그이지 <c>Point</c>에서
    /// 유도할 수 있는 값이 아니다 — 그래서 3인자 <see cref="PointerDown(Point, bool, bool)"/>이
    /// 이 판정을 인자로 받는다.
    /// </summary>
    private bool IsOverActiveTextBox() =>
        _activeTextBox is not null && _activeTextBox.IsMouseOver;

    // ---- 지우개 (클릭 + 드래그 삭제 — 사용자 조타 12차로 Round 13 클릭 전용에서 확장) ----

    private void EraseAt(Point pos)
    {
        // 요소를 없애기 전에 진행 중인 휠 확대를 확정한다 — 안 그러면 유휴 타이머가 뒤늦게 깨어나
        // 이미 지워진 요소의 변형을 지우기 항목 뒤에 실어 실행취소 1회가 무동작이 된다 (R7).
        WheelScale.Flush(commit: true);
        var element = document.HitTestNearest(pos, tolerance: SelectionGestureRules.EraseHitTolerancePixels);
        if (element is null)
        {
            return;
        }
        int index = document.IndexOf(element);
        if (document.Remove(element))
        {
            ledger.RecordErase(document, element, index);
        }
    }

    // ---- 커밋 공통 ----

    /// <summary>공통 커밋. fade는 획 시작 시점 판정 (도형/텍스트는 항상 false).</summary>
    private void CommitElement(AnnotationElement element, bool fade = false)
    {
        document.Add(element);
        ledger.RecordAdd(element);
        fading.OnElementCommitted(element, seams.Now(), fade);
    }

    /// <summary>
    /// 진행 중인 휠 확대를 <b>지금</b> 원장에 확정한다 (R7).
    ///
    /// 선택 삭제처럼 요소를 없애는 조작이 먼저 일어나면, 유휴 타이머가 뒤늦게 깨어나 <b>이미 삭제된</b>
    /// 요소의 변형을 삭제 항목 뒤에 실어 실행취소 1회가 아무 일도 하지 않는 유령 스텝이 된다.
    /// 그런 조작 직전에 이걸 부르면 확대가 삭제보다 앞 항목이 되어 실행취소 순서가 맞는다.
    /// </summary>
    public void FlushPendingTransforms() => WheelScale.Flush(commit: true);

    /// <summary>
    /// 진행 중인 모든 입력을 **정해진 순서로** 마감한다.
    /// gen-7 자문(MED): 비인터랙티브 전환으로 버튼 업이 유실돼도 유령 드래그 삭제가 없도록 리셋한다.
    ///
    /// 취소의 의미가 다섯 가지로 <b>서로 다르다</b> — 균일한 <c>Cancel()</c> 인터페이스로 묶으면 전부 틀어진다.
    /// <list type="bullet">
    ///   <item>획·도형 = <b>폐기</b>. 미리보기 시각물만 있고 원장 항목이 없으므로 그냥 지운다.</item>
    ///   <item>텍스트 = <b>커밋</b>. ARCH-2 NOACTIVATE 핸드셰이크로 이미 활성화된 편집이고,
    ///         입력한 글자를 폐기하면 사용자 데이터가 사라진다.</item>
    ///   <item>변형(드래그) = <b>롤백</b> (R15). 원장에 없는 중간 변형이 화면에 남으면 실행취소로 지울 수 없다.</item>
    ///   <item>휠 = <b>확정</b> (R7/f3). 방치하면 원장에 없는 변형이 남고, 롤백하면 화면에서 이미
    ///         커진 결과가 소리 없이 되돌아간다.</item>
    ///   <item>제스처 각도 = <b>소멸</b>. <see cref="ResetSelectGesture"/> 한 곳에서만 사라진다 (SEL-LIM-6).</item>
    /// </list>
    ///
    /// 순서가 곧 계약이다. 롤백은 휠 확정보다 <b>앞</b>이고(뒤에 두면 원장의 after가 곧 롤백될 화면과
    /// 어긋난 채 실린다), <see cref="ResetSelectGesture"/>는 휠 확정 <b>뒤</b>이며(먼저 부르면 시작 상태
    /// 스냅샷이 사라져 롤백이 조용히 무동작이 된다), 캡처 해제(ARCH-6)는 언제나 <b>마지막</b>이다.
    ///
    /// <b>이 메서드는 <see cref="BeginSelectGesture"/> 머리의 <c>setGestureGroupFrame(null)</c>과
    /// 한 곳으로 합칠 수 없다.</b> 그쪽은 각도만 지우는 것이고 여기는 커밋/롤백 의미를 가진다 —
    /// 합치는 순간 진행 중 변형이 커밋도 롤백도 되지 않고 화면에 남는다 (그 함수의 주석 참고).
    /// </summary>
    public void CancelActiveInput()
    {
        _eraserDragging = false;
        _hadSelectionOnPress = false;
        DiscardStroke();
        DiscardShape();
        if (_activeTextBox is not null)
        {
            CommitText(); // 텍스트만 폐기가 아니라 커밋이다 (ARCH-2).
        }
        // 진행 중이던 변형은 시작 상태로 롤백한다 (DragBaseStates 참조).
        _base.RollbackAll();
        if (_dragKind != SelectionDragKind.None)
        {
            setMarquee(null);
        }
        // 휠 확정은 드래그 롤백 **뒤**다. 앞에 두면 원장의 after가 곧 롤백될 화면과 어긋난 채 실린다.
        // 확정하는 이유: 롤백하면 화면에서 이미 커진 결과가 소리 없이 되돌아가고, 방치하면
        // 원장에 없는 변형이 남아 실행취소로 지울 수 없게 된다.
        WheelScale.Flush(commit: true);
        ResetSelectGesture();
        host.ReleaseMouseCapture();
    }
}
