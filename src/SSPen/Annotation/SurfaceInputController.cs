using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
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
/// 획 시작 시점에 스냅샷하는 스타일 값 (드래그 중 퀵컬러 핫키/휠 굵기 조정이
/// 진행 중인 획/도형/텍스트의 미리보기·커밋 스타일을 어긋나게 하는 버그 수정용).
/// </summary>
public readonly record struct StrokeStyle(Color Color, double Thickness, bool IsHighlighter, bool IsFading);

/// <summary>
/// 마우스/키보드 입력 상태 머신 (획·도형·텍스트·지우개). 창 참조 없이 <see cref="ISurfaceHost"/>로만
/// 창과 통신한다 (ARCH-2/ARCH-6 핸드셰이크만 위임).
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
    Action<Rect?> setGroupFrame,
    Action<IReadOnlyList<TransformDelta>, Point?> commitTransform,
    Action requestClickThrough)
{
    /// <summary>선택 히트 허용 오차 (지우개와 같은 감각으로 통일).</summary>
    private const double SelectTolerance = 6;

    // 선택 도구 드래그 상태 (SEL-7)
    private SelectionDragKind _dragKind;
    private Point _dragStart;
    private HandleKind _dragHandle;
    private GroupHandleKind _dragGroupHandle;
    private AnnotationElement? _dragHandleTarget;
    private Dictionary<long, ElementTransformState>? _dragBaseStates;

    /// <summary>
    /// 제스처 시작 시점에 <b>동결</b>된 그룹 프레임 (R1). 살아있는 경계로 매 프레임 재계산하면
    /// 회전 중 피벗이 표류하고 잡은 핸들이 커서 밑에서 빠져나간다 — "매 프레임 드래그 시작 상태에서
    /// 재계산"(<see cref="UpdateSelectGesture"/>) 규약의 프레임판이다.
    /// </summary>
    private Rect _groupFrame;

    /// <summary>빈 곳 제스처를 시작할 때 선택이 있었는가 (R5: 제자리 클릭이면 업에서 클릭 통과로 전환).</summary>
    private bool _hadSelectionOnPress;

    // 휠 확대/축소 (R7): 연속 노치를 하나의 원장 항목으로 묶는다.
    private readonly WheelScaleSession _wheel = new();
    private Dictionary<long, ElementTransformState>? _wheelBaseStates;

    /// <summary>
    /// 휠 세션이 잡은 요소들. <b>선택집합을 다시 조회하지 않는 이유</b>: 휠 확정은 유휴 타이머로
    /// 비동기 발생하므로, 그 사이 ESC나 클릭 통과 전환으로 선택이 비면 요소를 되찾지 못해
    /// <b>화면에는 커진 채 원장에는 없는</b> 변형이 남아 실행취소로 지울 수 없게 된다.
    /// </summary>
    private List<AnnotationElement>? _wheelElements;
    private DispatcherTimer? _wheelTimer;

    // 진행 중 획/도형/텍스트 상태
    private List<Point>? _activePoints;
    private Polyline? _activePolyline;
    private StrokeStyle _activeStrokeStyle;
    private bool _eraserDragging;          // 지우개 드래그 삭제 중 (사용자 조타 12차)
    private Point _shapeStart;
    private Shape? _previewShape;
    private ShapeKind _previewKind;
    private Color _activeShapeColor;
    private double _activeShapeThickness;
    private bool _activeShapeFading;       // 도형 시작 시점 페이딩 판정 (사용자 요청 17차)
    private TextBox? _activeTextBox;
    private Point _textOrigin;
    private Color _activeTextColor;
    private double _activeTextFontSize;
    private bool _activeTextFading;        // 텍스트 시작 시점 페이딩 판정 (사용자 요청 17차)

    public void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!state.IsInteractive)
        {
            return;
        }
        var pos = e.GetPosition(inkCanvas);

        // 텍스트 편집 중 바깥 클릭 → 확정 (Round 13).
        if (_activeTextBox is not null && !IsOverActiveTextBox(e))
        {
            CommitText();
            return;
        }

        switch (state.ActiveTool)
        {
            case ToolKind.Pen:
            case ToolKind.Highlighter:
                StartStroke(pos);
                break;
            case ToolKind.Line:
                StartShape(ShapeKind.Line, pos);
                break;
            case ToolKind.Arrow:
                StartShape(ShapeKind.Arrow, pos);
                break;
            case ToolKind.Rectangle:
                StartShape(ShapeKind.Rectangle, pos);
                break;
            case ToolKind.Ellipse:
                StartShape(ShapeKind.Ellipse, pos);
                break;
            case ToolKind.Text:
                BeginTextEdit(pos);
                break;
            case ToolKind.Eraser:
                // 사용자 조타: 클릭 + 드래그 삭제. 캡처로 창 밖까지 추적.
                EraseAt(pos);
                _eraserDragging = true;
                host.CaptureMouse();
                break;
            case ToolKind.Select:
                BeginSelectGesture(pos);
                break;
        }
        e.Handled = true;
    }

    public void OnMouseMove(MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }
        var pos = e.GetPosition(inkCanvas);

        if (_activePoints is not null && _activePolyline is not null)
        {
            var last = _activePoints[^1];
            if ((pos - last).Length >= 1.5)
            {
                _activePoints.Add(pos);
                _activePolyline.Points.Add(pos);
            }
        }
        else if (_previewShape is not null)
        {
            // D3: 선택 경로와 같은 이유로 KeyboardState를 쓴다 — Keyboard.Modifiers는 스레드 로컬이라
            // 이 창(영구 NOACTIVATE)에서는 항상 None이고, 전역 핫키로 도형 도구를 켠 정상 흐름에서
            // Shift 스냅(수평/수직/정비율)이 조용히 죽는다. 미리보기와 커밋이 **같은 판정**을 써야 한다.
            var end = KeyboardState.Shift
                ? ShiftConstraints.Apply(_previewKind, _shapeStart, pos)
                : pos;
            AnnotationVisualFactory.UpdateShapeVisual(_previewShape, _previewKind, _shapeStart, end);
        }
        else if (_eraserDragging && state.ActiveTool == ToolKind.Eraser && state.IsInteractive)
        {
            // 사용자 조타 12차: 드래그 지우기 (기존 Non-Goal 4 펜스를 명시 조타로 해제).
            EraseAt(pos);
        }
        else if (_dragKind != SelectionDragKind.None && state.IsInteractive)
        {
            UpdateSelectGesture(pos);
        }
    }

    public void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        // D4: 이동 경로(:OnMouseMove)에만 있던 인터랙티브 가드를 업에도 건다. 비인터랙티브로 전환된 뒤
        // 도착한 버튼 업이 제스처를 확정하면 보이지 않는 조작이 원장에 실린다.
        if (!state.IsInteractive)
        {
            CancelActiveInput();
            return;
        }

        if (_activePoints is not null)
        {
            CommitStroke();
        }
        else if (_previewShape is not null)
        {
            CommitShape(e.GetPosition(inkCanvas));
        }
        else if (_dragKind != SelectionDragKind.None)
        {
            EndSelectGesture(e.GetPosition(inkCanvas));
        }
        _eraserDragging = false;
        host.ReleaseMouseCapture();
    }

    public void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (!state.IsInteractive)
        {
            return;
        }

        // R7: 선택 도구에서 선택집합이 있으면 휠은 **선택 크기 조절**이다.
        // 원래 이 자리에서 굵기가 조정될 수 없었다 — SEL-5가 선택 도구의 스타일 쓰기를 차단하므로
        // StepThickness가 조용히 무동작이었다. 즉 죽어 있던 입력을 되살리는 것이지 뺏는 것이 아니다.
        if (state.ActiveTool == ToolKind.Select)
        {
            // 드래그 중 휠은 **삼키기만** 한다. 두 세션이 같은 요소를 동시에 잡으면 시작 상태
            // 스냅샷이 둘로 갈라져, 마우스 업이 항목 1을 싣고 450ms 뒤 유휴 타이머가 항목 2를
            // 더 실어 한 번의 드래그가 실행취소 2번이 된다 (그중 하나는 아무 일도 하지 않는 유령 스텝).
            if (_dragKind != SelectionDragKind.None)
            {
                e.Handled = true;
                return;
            }
            var owned = OwnedSelection();
            // SEL-LIM-5: 모니터에 걸친 선택은 확대/축소하지 않는다. 고정점이 이 서피스의 논리 좌표라
            // 다른 원점·DPI를 쓰는 문서의 요소에 그대로 먹이면 엉뚱한 곳으로 흩어진다.
            if (SelectionGroup.HandlesGrabbable(owned.Count, selection.Count))
            {
                StepWheelScale(owned, e.GetPosition(inkCanvas), e.Delta > 0 ? +1 : -1);
                e.Handled = true;
            }
            return;
        }

        // 마우스 휠로 펜 크기 조정 (WI-16 설정 연동).
        if (state.WheelAdjustsPenSize)
        {
            state.StepThickness(e.Delta > 0 ? +1 : -1);
            e.Handled = true;
        }
    }

    public void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _activeTextBox is not null)
        {
            CommitText();
            e.Handled = true;
        }
    }

    // ---- 선택 도구 입력 상태 머신 (SEL-7) ----

    private enum SelectionDragKind
    {
        None,
        Marquee,
        Move,

        /// <summary>단일 선택 전용: 요소 로컬 축 기준 <b>비등방</b> 크기 조절 (8핸들).</summary>
        Scale,

        /// <summary>단일 선택 전용: 요소 자기 중심 회전.</summary>
        Rotate,

        /// <summary>다중 선택: 그룹 프레임 대각 앵커 기준 <b>등방</b> 확대/축소 (R1).</summary>
        GroupScale,

        /// <summary>다중 선택: 그룹 프레임 중심 기준 회전 (R1).</summary>
        GroupRotate,
    }

    /// <summary>이 문서가 소유한 선택 요소. 장식·핸들·그룹 프레임은 모두 소유 서피스가 그린다.</summary>
    private List<AnnotationElement> OwnedSelection()
    {
        var owned = new List<AnnotationElement>();
        foreach (var element in selection.Elements)
        {
            if (document.Elements.Contains(element))
            {
                owned.Add(element);
            }
        }
        return owned;
    }

    /// <summary>회전 핸들 클램프용 서피스 논리 경계. 렌더와 **같은 값**을 써야 힌트와 그림이 어긋나지 않는다 (R5).</summary>
    private Rect SurfaceBounds => new(0, 0, inkCanvas.ActualWidth, inkCanvas.ActualHeight);

    /// <summary>
    /// 히트 우선순위 (SEL-7): 핸들 → 요소 → 빈 곳. 핸들이 먼저인 이유는 핸들이 요소 경계
    /// **바깥**에도 놓이기 때문이다 — 순서를 뒤집으면 빈 곳 분기가 회전 핸들을 가로채 잡힌 적이 없게 된다.
    /// </summary>
    private void BeginSelectGesture(Point pos)
    {
        CancelWheelScale(commit: true); // 휠 확대 중 클릭은 그 확대를 먼저 확정한다 (원장 순서 보존).
        _dragStart = pos;
        // D3: WPF Keyboard.Modifiers는 스레드 로컬이라 이 창(영구 NOACTIVATE)에서 항상 None이다.
        bool shift = KeyboardState.Shift;

        var owned = OwnedSelection();
        // 모니터에 걸친 선택은 이동만 허용한다 (SEL-LIM-5): 서피스마다 원점과 DPI가 달라
        // 두 문서의 논리 경계를 합친 그룹 프레임은 서로소인 좌표계의 합이라 의미가 없다.
        // 술어는 렌더(ContentSurfaceWindow.RedrawDecorations)와 **같은 함수**를 쓴다 — 이름을 따로
        // 두면 "그리는 조건"과 "잡히는 조건"이 다시 갈라져 보이지만 잡히지 않는 핸들이 생긴다.
        bool grabbable = SelectionGroup.HandlesGrabbable(owned.Count, selection.Count);

        // 1) 핸들 히트 — 핸들이 프레임 **바깥**에도 놓이므로 반드시 가장 먼저다 (SEL-7).
        if (grabbable && owned.Count >= SelectionGroup.MinGroupCount)
        {
            if (SelectionGroup.Frame(owned) is { } frame
                && SelectionGroup.HitHandle(frame, pos, SurfaceBounds) is { } groupHandle)
            {
                _groupFrame = frame;
                _dragGroupHandle = groupHandle;
                _dragKind = groupHandle == GroupHandleKind.Rotate
                    ? SelectionDragKind.GroupRotate
                    : SelectionDragKind.GroupScale;
                SnapshotDragStates();
                // 그려지는 프레임을 동결하는 것은 **회전뿐**이다. 회전은 축 정렬 합집합을 부풀려
                // 잡은 핸들이 커서 밑에서 빠져나가지만, 등방 스케일은 축 정렬 사상이라 살아있는
                // 합집합이 그대로 정답이다. 오히려 동결하면 구성원 배율이 바닥에 클램프될 때
                // 이상적 사상과 실제 합집합이 어긋나 마우스 업에서 프레임이 튄다.
                // (_groupFrame 자체는 배율·피벗의 기준이므로 두 경우 모두 동결한다.)
                if (_dragKind == SelectionDragKind.GroupRotate)
                {
                    setGroupFrame(frame);
                }
                host.CaptureMouse();
                return;
            }
        }
        else if (grabbable && owned.Count == 1)
        {
            var candidate = owned[0];
            if (TransformMath.HitHandle(candidate.TransformState, candidate.LocalBounds, pos, SurfaceBounds)
                is { } handle)
            {
                _dragHandle = handle;
                _dragHandleTarget = candidate;
                _dragKind = handle == HandleKind.Rotate ? SelectionDragKind.Rotate : SelectionDragKind.Scale;
                SnapshotDragStates();
                host.CaptureMouse();
                return;
            }
        }

        // 커서 밑의 요소를 **먼저** 구한다 — 아래 두 분기가 같은 값을 봐야 우선순위가 일관된다.
        var hit = SelectionGeometry.HitForSelect(document.Elements, pos, SelectTolerance);

        // 2) 선택 프레임 **내부** 클릭 → 이동 (R6). 이미 선택한 것을 옮기려고 잉크 실선을 다시
        //    정확히 겨냥할 필요가 없어진다. Shift는 토글 의도이므로 이 분기를 건너뛴다.
        //    단, 프레임 안이라도 **선택되지 않은 다른 요소** 위라면 3번에 양보한다 — 안 그러면
        //    큰 선택 하나가 그 프레임 안의 모든 요소를 영구히 가려 다시는 고를 수 없다.
        if (!shift
            && owned.Count > 0
            && SelectionGestureRules.ShouldMoveFromFrameInterior(
                IsInsideSelectionFrame(owned, pos), hit is not null, hit is not null && selection.Contains(hit)))
        {
            _dragKind = SelectionDragKind.Move;
            SnapshotDragStates();
            host.CaptureMouse();
            return;
        }

        // 3) 요소 히트 — Shift는 토글(SEL-AC-3), 그 외는 단일 선택 후 이동 준비.
        //    R6: 잉크 정확 히트 우선 → 없으면 경계 상자 내부 중 면적 최소.
        if (hit is not null)
        {
            if (shift)
            {
                selection.Toggle(hit);
                return; // 토글은 이동을 시작하지 않는다.
            }
            if (!selection.Contains(hit))
            {
                selection.Set([hit]);
            }
            _dragKind = SelectionDragKind.Move;
            SnapshotDragStates();
            host.CaptureMouse();
            return;
        }

        // 4) 빈 곳 — Shift가 아니면 해제하고 마퀴를 시작한다.
        //    R5의 클릭 통과 전환은 **여기서 하지 않는다**: 지금 켜면 IsInteractive가 false로 떨어져
        //    막 시작한 마퀴가 그대로 얼어붙는다. 판정은 마우스 업(EndSelectGesture)이 맡는다.
        // Shift+빈 곳은 **누적 의도**다 — 기존 선택을 유지한 채 마퀴로 더하려는 것이므로
        // 제자리에서 뗐다고 해제·클릭 통과로 넘어가면 안 된다.
        _hadSelectionOnPress = !shift && selection.Count > 0;
        if (!shift)
        {
            selection.Clear();
        }
        _dragKind = SelectionDragKind.Marquee;
        setMarquee(new Rect(pos, pos));
        host.CaptureMouse();
    }

    /// <summary>
    /// 커서가 선택 표시 안쪽인가 (R6). 다중 선택은 축 정렬 그룹 프레임,
    /// 단일 선택은 회전을 반영한 로컬 프레임(OBB) — <b>화면에 그려진 점선 경계와 같은 영역</b>이어야 한다.
    /// </summary>
    private bool IsInsideSelectionFrame(List<AnnotationElement> owned, Point pos)
    {
        if (owned.Count >= SelectionGroup.MinGroupCount)
        {
            return SelectionGroup.Frame(owned) is { } frame && frame.Contains(pos);
        }
        return SelectionGeometry.ContainsInFrame(owned[0], pos);
    }

    /// <summary>
    /// 매 프레임 **드래그 시작 상태에서 재계산**한다 (직전 프레임 결과 누적 금지).
    /// 누적하면 부동소수 오차가 프레임마다 쌓여 요소가 서서히 어긋나고, 취소 복원 기준도 사라진다.
    /// </summary>
    private void UpdateSelectGesture(Point pos)
    {
        if (_dragKind == SelectionDragKind.Marquee)
        {
            setMarquee(new Rect(_dragStart, pos));
            return;
        }
        if (_dragBaseStates is not { } baseStates)
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
                    ApplyTransformState(
                        scaleTarget,
                        TransformMath.ScaleLocal(scaleStart, scaleTarget.LocalBounds, _dragHandle, pos));
                }
                break;

            case SelectionDragKind.Rotate:
                if (_dragHandleTarget is { } rotateTarget
                    && baseStates.TryGetValue(rotateTarget.Id, out var rotateStart))
                {
                    ApplyTransformState(
                        rotateTarget,
                        TransformMath.Rotate(
                            rotateStart,
                            rotateTarget.LocalBounds,
                            _dragStart,
                            pos,
                            KeyboardState.Shift));
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
                        ApplyTransformState(
                            element, TransformMath.ScaleAbout(start, element.LocalBounds, pivot, factor));
                    }
                }
                break;
            }

            case SelectionDragKind.GroupRotate:
            {
                var pivot = SelectionGroup.Center(_groupFrame);
                double delta = GroupRotationDelta(pivot, pos);
                foreach (var element in selection.Elements)
                {
                    if (baseStates.TryGetValue(element.Id, out var start))
                    {
                        ApplyTransformState(
                            element, TransformMath.RotateAbout(start, element.LocalBounds, pivot, delta));
                    }
                }
                break;
            }
        }
    }

    /// <summary>
    /// 선택 전체 이동 (SEL-AC-9).
    ///
    /// D1: 다른 모니터 소속 요소에는 <b>DPI 환산</b>이 필요하다. 요소 기하는 자기 소유 서피스의 논리
    /// 단위로 굳어 있는데 변위는 게스처가 일어난 서피스의 논리 단위이므로, 두 모니터의 배율이 다르면
    /// 같은 손동작이 서로 다른 물리 거리로 번역된다. 물리 거리를 보존하는 환산은
    /// <c>d_target = d_source · (srcDpi / tgtDpi)</c>이며, 이관(<c>SelectionOperations.RebaseState</c>)이
    /// 쓰는 비율과 같은 형태다. 이 리그는 3대 모두 100%(r=1)라 통합 테스트가 절대 잡지 못한다.
    /// </summary>
    private void MoveSelection(Dictionary<long, ElementTransformState> baseStates, Vector delta)
    {
        double sourceDpi = host.GetDpi().DpiScaleX;
        foreach (var element in selection.Elements)
        {
            if (!baseStates.TryGetValue(element.Id, out var start))
            {
                continue;
            }
            double targetDpi = dpiOf(ownerLookup(element) ?? document);
            var scaled = targetDpi > 0 && Math.Abs(targetDpi - sourceDpi) > 1e-9
                ? new Vector(delta.X * sourceDpi / targetDpi, delta.Y * sourceDpi / targetDpi)
                : delta;
            ApplyTransformState(element, TransformMath.Translate(start, scaled));
        }
    }

    /// <summary>
    /// 그룹 회전각 증분. Shift는 <b>증분</b>을 15도 배수로 스냅한다 — 요소마다 시작 각이 달라
    /// 결과 각을 스냅하는 단일 선택 규칙(<see cref="TransformMath.Rotate"/>)을 그대로 쓸 수 없다.
    /// </summary>
    private double GroupRotationDelta(Point pivot, Point pos)
    {
        var before = _dragStart - pivot;
        var after = pos - pivot;
        if (before.Length < TransformMath.MinScale || after.Length < TransformMath.MinScale)
        {
            return 0;
        }
        double delta =
            (Math.Atan2(after.Y, after.X) - Math.Atan2(before.Y, before.X)) * 180.0 / Math.PI;
        return KeyboardState.Shift ? ShiftConstraints.SnapDegrees(delta) : delta;
    }

    private void EndSelectGesture(Point pos)
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
            if (KeyboardState.Shift)
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

        if (_dragBaseStates is { } baseStates)
        {
            // 실제로 바뀐 요소만 원장에 싣는다 — 제자리 클릭이 빈 undo 항목을 만들면 안 된다 (f3).
            var deltas = new List<TransformDelta>();
            foreach (var (id, before) in baseStates)
            {
                var element = FindDragged(id);
                if (element is null || before == element.TransformState)
                {
                    continue;
                }
                var owner = ownerLookup(element) ?? document;
                deltas.Add(new TransformDelta(element, before, element.TransformState, owner, owner));
            }
            if (deltas.Count > 0)
            {
                // 이관 판정(f7)과 원장 기록은 컴포지션 루트가 소유한다 (SEL-14/P7).
                // 놓은 지점을 넘기는 것은 **이동일 때뿐**이다: 크기/회전은 커서가 요소를 끌고 다닌 것이
                // 아니라 핸들을 돌린 것이므로, 회전 핸들이 옆 모니터에 닿았다고 요소를 이관하면 안 된다.
                commitTransform(deltas, _dragKind == SelectionDragKind.Move ? pos : null);
            }
        }
        ResetSelectGesture();
    }

    // ---- 휠 확대/축소 (R7) ----

    /// <summary>
    /// 휠 노치 1회. 첫 노치에서 시작 상태와 고정점을 <b>동결</b>하고, 이후 노치는 그 시작 상태에
    /// 누적 배율을 곱한다 (드래그와 같은 "직전 프레임 누적 금지" 규약 — 누적하면 부동소수 오차가 쌓인다).
    /// </summary>
    private void StepWheelScale(List<AnnotationElement> owned, Point cursor, int notches)
    {
        if (!_wheel.Active)
        {
            if (SelectionGroup.Frame(owned) is not { } frame)
            {
                return;
            }
            _wheelBaseStates = [];
            _wheelElements = [.. owned];
            foreach (var element in owned)
            {
                _wheelBaseStates[element.Id] = element.TransformState;
            }
            _wheel.Begin(SelectionGestureRules.WheelPivot(frame, cursor), DateTime.UtcNow);
        }
        if (_wheelBaseStates is not { } baseStates || _wheelElements is not { } elements)
        {
            return;
        }

        double raw = _wheel.Step(notches, DateTime.UtcNow);
        double factor = TransformMath.ClampGroupFactor(raw, baseStates.Values);
        _wheel.SetFactor(factor); // 한계 밖 누적을 지워 천장에서 첫 역방향 노치부터 반응하게 한다 (R7).
        foreach (var element in elements)
        {
            if (baseStates.TryGetValue(element.Id, out var start))
            {
                ApplyTransformState(
                    element, TransformMath.ScaleAbout(start, element.LocalBounds, _wheel.Pivot, factor));
            }
        }

        _wheelTimer ??= new DispatcherTimer(DispatcherPriority.Background, inkCanvas.Dispatcher)
        {
            Interval = WheelScaleSession.IdleTimeout,
        };
        _wheelTimer.Tick -= OnWheelIdleTick;
        _wheelTimer.Tick += OnWheelIdleTick;
        _wheelTimer.Stop();
        _wheelTimer.Start();
    }

    private void OnWheelIdleTick(object? sender, EventArgs e)
    {
        if (_wheel.DueToCommit(DateTime.UtcNow))
        {
            CancelWheelScale(commit: true);
        }
    }

    /// <summary>
    /// 휠 세션 마감. <paramref name="commit"/>이면 <b>원장 1항목</b>으로 싣는다 (f3/SEL-12).
    /// 이관 판정은 건너뛴다 — 휠은 요소를 어디에도 "놓지" 않았고, 커서가 옆 모니터 위에 있다는
    /// 이유로 선택 전체가 이관되면 사용자 의도와 정반대다.
    /// </summary>
    private void CancelWheelScale(bool commit)
    {
        _wheelTimer?.Stop();
        if (!_wheel.Active)
        {
            _wheelBaseStates = null;
            _wheelElements = null;
            return;
        }
        if (commit && _wheelBaseStates is { } baseStates && _wheelElements is { } elements)
        {
            var deltas = new List<TransformDelta>();
            foreach (var element in elements)
            {
                if (!baseStates.TryGetValue(element.Id, out var before) || before == element.TransformState)
                {
                    continue;
                }
                var owner = ownerLookup(element) ?? document;
                deltas.Add(new TransformDelta(element, before, element.TransformState, owner, owner));
            }
            if (deltas.Count > 0)
            {
                commitTransform(deltas, null);
            }
        }
        _wheel.End();
        _wheelBaseStates = null;
        _wheelElements = null;
    }

    /// <summary>드래그 시작 스냅샷에 담긴 id를 현재 요소로 되돌린다 (선택집합 또는 핸들 대상).</summary>
    private AnnotationElement? FindDragged(long id)
    {
        if (_dragHandleTarget is { } target && target.Id == id)
        {
            return target;
        }
        foreach (var element in selection.Elements)
        {
            if (element.Id == id)
            {
                return element;
            }
        }
        return null;
    }

    /// <summary>드래그 시작 상태 스냅샷. 핸들 대상은 선택집합에 없을 수 없지만 방어적으로 함께 넣는다.</summary>
    private void SnapshotDragStates()
    {
        _dragBaseStates = [];
        foreach (var element in selection.Elements)
        {
            _dragBaseStates[element.Id] = element.TransformState;
        }
        if (_dragHandleTarget is { } target)
        {
            _dragBaseStates[target.Id] = target.TransformState;
        }
    }

    /// <summary>
    /// 상태 대입 뒤에는 **반드시** 소유 문서의 알림이 따라와야 한다 (R15) — 그래야 시각물과 장식이 함께 움직인다.
    /// 다른 모니터 소속 요소도 그 요소의 소유 문서를 통해 알린다 (다중 선택 이동).
    /// </summary>
    private void ApplyTransformState(AnnotationElement element, ElementTransformState next)
    {
        element.TransformState = next;
        (ownerLookup(element) ?? document).RaiseElementTransformChanged(element);
    }

    private void ResetSelectGesture()
    {
        _dragKind = SelectionDragKind.None;
        _dragHandleTarget = null;
        _dragBaseStates = null;
        _groupFrame = Rect.Empty;
        setGroupFrame(null); // 동결 해제 — 이후 장식은 다시 살아있는 경계로 그린다.
    }

    private void StartStroke(Point pos)
    {
        // 시작 시점 판정 캡처 (아키텍트 자문): 드래그 중 핫키로 도구가 바뀌거나 퀵컬러/휠로
        // 색·굵기가 바뀌어도, 이 획의 스타일(색·굵기·형광펜·페이딩 여부)은 시작 당시 스냅샷을 따른다.
        bool highlighter = state.ActiveTool == ToolKind.Highlighter;
        bool fading = state.FadingApplies;
        var color = state.CurrentColor;
        double thickness = highlighter ? state.HighlighterThickness : state.PenThickness;
        _activeStrokeStyle = new StrokeStyle(color, thickness, highlighter, fading);

        _activePoints = [pos];
        _activePolyline = new Polyline
        {
            Stroke = AnnotationVisualFactory.StrokeBrush(color, highlighter),
            StrokeThickness = thickness,
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
        if (_activePoints is null || _activePolyline is null)
        {
            return;
        }
        var style = _activeStrokeStyle;
        var element = new StrokeElement(_activePoints, style.Color, style.Thickness, style.IsHighlighter);
        inkCanvas.Children.Remove(_activePolyline);
        _activePoints = null;
        _activePolyline = null;
        CommitElement(element, fade: style.IsFading);
    }

    private void StartShape(ShapeKind kind, Point pos)
    {
        _shapeStart = pos;
        _previewKind = kind;
        // 시작 시점 스냅샷: 드래그 중 색/굵기/페이딩 토글 변경이 미리보기·커밋 스타일을 어긋내지 않도록 고정.
        _activeShapeColor = state.CurrentColor;
        _activeShapeThickness = state.ShapeThickness;
        _activeShapeFading = state.FadingApplies;
        _previewShape = AnnotationVisualFactory.CreateShapeVisual(kind, _activeShapeColor, _activeShapeThickness);
        AnnotationVisualFactory.UpdateShapeVisual(_previewShape, kind, pos, pos);
        inkCanvas.Children.Add(_previewShape);
        host.CaptureMouse();
    }

    private void CommitShape(Point rawEnd)
    {
        if (_previewShape is null)
        {
            return;
        }
        var end = KeyboardState.Shift
            ? ShiftConstraints.Apply(_previewKind, _shapeStart, rawEnd)
            : rawEnd;
        inkCanvas.Children.Remove(_previewShape);
        _previewShape = null;

        if ((end - _shapeStart).Length < 3)
        {
            return; // 클릭만으로는 도형을 만들지 않는다.
        }
        var element = new ShapeElement(_previewKind, _shapeStart, end, _activeShapeColor, _activeShapeThickness);
        CommitElement(element, fade: _activeShapeFading);
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
        _activeTextColor = state.CurrentColor;
        _activeTextFontSize = state.TextFontSize;
        _activeTextFading = state.FadingApplies;
        _activeTextBox = new TextBox
        {
            FontFamily = new FontFamily("맑은 고딕"),
            FontSize = _activeTextFontSize,
            Foreground = new SolidColorBrush(_activeTextColor),
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

        if (!string.IsNullOrWhiteSpace(text))
        {
            var dpi = host.GetDpi();
            var formatted = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("맑은 고딕"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                _activeTextFontSize,
                Brushes.Black,
                dpi.PixelsPerDip);
            var element = new TextElement(
                _textOrigin,
                text,
                _activeTextColor,
                _activeTextFontSize,
                new Size(Math.Max(formatted.WidthIncludingTrailingWhitespace, 8), Math.Max(formatted.Height, 8)));
            CommitElement(element, fade: _activeTextFading);
        }
    }

    private bool IsOverActiveTextBox(MouseButtonEventArgs e) =>
        _activeTextBox is not null && _activeTextBox.IsMouseOver;

    // ---- 지우개 (클릭 + 드래그 삭제 — 사용자 조타 12차로 Round 13 클릭 전용에서 확장) ----

    private void EraseAt(Point pos)
    {
        // 요소를 없애기 전에 진행 중인 휠 확대를 확정한다 — 안 그러면 유휴 타이머가 뒤늦게 깨어나
        // 이미 지워진 요소의 변형을 지우기 항목 뒤에 실어 실행취소 1회가 무동작이 된다 (R7).
        CancelWheelScale(commit: true);
        var element = document.HitTestNearest(pos, tolerance: 6);
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
        fading.OnElementCommitted(element, DateTime.UtcNow, fade);
    }

    /// <summary>
    /// 진행 중인 휠 확대를 <b>지금</b> 원장에 확정한다 (R7).
    ///
    /// 선택 삭제처럼 요소를 없애는 조작이 먼저 일어나면, 유휴 타이머가 뒤늦게 깨어나 <b>이미 삭제된</b>
    /// 요소의 변형을 삭제 항목 뒤에 실어 실행취소 1회가 아무 일도 하지 않는 유령 스텝이 된다.
    /// 그런 조작 직전에 이걸 부르면 확대가 삭제보다 앞 항목이 되어 실행취소 순서가 맞는다.
    /// </summary>
    public void FlushPendingTransforms() => CancelWheelScale(commit: true);

    /// <summary>gen-7 자문(MED): 비인터랙티브 전환으로 버튼 업이 유실돼도 유령 드래그 삭제가 없도록 리셋.</summary>
    public void CancelActiveInput()
    {
        _eraserDragging = false;
        _hadSelectionOnPress = false;
        if (_activePolyline is not null)
        {
            inkCanvas.Children.Remove(_activePolyline);
            _activePoints = null;
            _activePolyline = null;
        }
        if (_previewShape is not null)
        {
            inkCanvas.Children.Remove(_previewShape);
            _previewShape = null;
        }
        if (_activeTextBox is not null)
        {
            CommitText();
        }
        // 진행 중이던 변형은 시작 상태로 롤백한다 — 원장에 없는 중간 변형이 화면에 남으면 실행취소로 지울 수 없다.
        if (_dragBaseStates is { } baseStates)
        {
            foreach (var (id, start) in baseStates)
            {
                if (FindDragged(id) is { } element)
                {
                    ApplyTransformState(element, start);
                }
            }
        }
        if (_dragKind != SelectionDragKind.None)
        {
            setMarquee(null);
        }
        // 휠 확정은 드래그 롤백 **뒤**다. 앞에 두면 원장의 after가 곧 롤백될 화면과 어긋난 채 실린다.
        // 확정하는 이유: 롤백하면 화면에서 이미 커진 결과가 소리 없이 되돌아가고, 방치하면
        // 원장에 없는 변형이 남아 실행취소로 지울 수 없게 된다.
        CancelWheelScale(commit: true);
        ResetSelectGesture();
        host.ReleaseMouseCapture();
    }
}
