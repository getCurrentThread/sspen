using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace SSPen.Annotation;

/// <summary>
/// 획·도형·표 제스처의 <b>진행 중 상태와 미리보기 시각물</b> (46단계). <see cref="SurfaceInputController"/>의 그리기 클러스터
/// (필드 11개 + 시작/갱신/확정/폐기)를 그대로 옮긴 것이다 — 판정은 여전히 <see cref="GestureStyleSnapshot"/>(시작 시점 스타일
/// 동결), <see cref="StrokeAccumulator"/>(획 누적), <see cref="ShapeGestureRules"/>(끝점·커밋 임계), <see cref="TableGestureRules"/>
/// (행·열 조절)가 갖고, 여기 남는 것은 그 판정을 캔버스와 커밋 델리게이트로 흘려보내는 배선뿐이다.
///
/// 이 클래스가 <b>모르는</b> 것:
/// <list type="bullet">
///   <item>창(<c>ISurfaceHost</c>) — 마우스 캡처·해제 쌍(ARCH-6)은 컨트롤러가 한 클래스 안에서 소유한다.</item>
///   <item>문서·원장·페이딩 — 확정은 주입된 <c>commitElement</c>(컨트롤러의 <c>CommitElement</c>, 텍스트 도구와 공유)로 흘려보낸다.</item>
///   <item>선택·텍스트·지우개 상태 — 세 진입점(<see cref="Move"/>/<see cref="Up"/>/<see cref="Wheel"/>)은 "이 분기를 소비했는가"만
///         돌려주고, 나머지 사다리(지우개 → 선택)는 컨트롤러가 옮기기 전 if/else-if 순서 그대로 잇는다.</item>
/// </list>
///
/// 폐기(<see cref="DiscardAll"/>)는 커밋이 아니다 — 원장 항목이 없으므로 미리보기 시각물만 지운다. <c>CancelActiveInput</c>의
/// 다섯 가지 취소 의미 중 "획·도형·표 = 폐기" 한 항이 이 메서드이며, 나머지 넷과 그 순서는 컨트롤러가 소유한다.
/// </summary>
public sealed class DrawingGestureController(
    Canvas inkCanvas,
    AppState state,
    Action<AnnotationElement, bool> commitElement,
    Action<TableBadgeHint?> setTableBadge)
{
    private StrokeAccumulator? _stroke;
    private Path? _activeStrokePath;
    private Point _shapeStart;
    private Shape? _previewShape;
    private ShapeKind _previewKind;
    private ShapeStyle _activeShapeStyle;  // 도형 시작 시점 페이딩 판정 (사용자 요청 17차)
    private Point _tableStart;
    private Shape? _previewTable;
    private TableStyle _activeTableStyle;
    private TableSize _tableSize;          // 진행 중 행·열 — 확정 시점에만 AppState로 (fix 57b043d)
    private Point _lastPointerPos;

    /// <summary>진행 중인 획의 점 목록 (테스트/진단용).</summary>
    public IReadOnlyList<Point>? ActiveStrokePoints => _stroke?.Points;

    /// <summary>표 드래그 중인가 — 휠·방향키가 라우터보다 <b>앞</b>에서 선점하는 판정의 근거.</summary>
    public bool TableActive => _previewTable is not null;

    /// <summary>획·도형·표 중 하나라도 진행 중인가.</summary>
    public bool Active => _stroke is not null || _previewShape is not null || _previewTable is not null;

    // ---- 시작 (캡처는 호출자 몫) ----

    public void StartStroke(Point pos, ToolKind effectiveTool, float pressure)
    {
        // 시작 시점 판정 캡처 (아키텍트 자문): 드래그 중 핫키로 도구가 바뀌거나 퀵컬러/휠로
        // 색·굵기가 바뀌어도, 이 획의 스타일(색·굵기·형광펜·페이딩 여부)은 시작 당시 스냅샷을 따른다.
        var style = GestureStyleSnapshot.ForStroke(state, effectiveTool);
        _stroke = new StrokeAccumulator(pos, style, pressure);
        _activeStrokePath = new Path
        {
            Fill = AnnotationVisualFactory.StrokeBrush(style.Color, style.IsHighlighter),
        };
        UpdateActiveStrokeVisual();
        inkCanvas.Children.Add(_activeStrokePath);
    }

    public void StartShape(ShapeKind kind, Point pos, ToolKind effectiveTool)
    {
        _shapeStart = pos;
        _previewKind = kind;
        // 시작 시점 스냅샷: 드래그 중 색/굵기/페이딩 토글 변경이 미리보기·커밋 스타일을 어긋내지 않도록 고정.
        _activeShapeStyle = GestureStyleSnapshot.ForShape(state, effectiveTool);
        _previewShape = AnnotationVisualFactory.CreateShapeVisual(kind, _activeShapeStyle.Color, _activeShapeStyle.Thickness);
        AnnotationVisualFactory.UpdateShapeVisual(_previewShape, kind, pos, pos);
        inkCanvas.Children.Add(_previewShape);
    }

    public void StartTable(Point pos, ToolKind effectiveTool)
    {
        _tableStart = pos;
        _lastPointerPos = pos;
        _activeTableStyle = GestureStyleSnapshot.ForTable(state, effectiveTool);
        _tableSize = new TableSize(_activeTableStyle.Rows, _activeTableStyle.Columns);
        _previewTable = AnnotationVisualFactory.CreateTableVisual(_activeTableStyle.Color, _activeTableStyle.Thickness);
        AnnotationVisualFactory.UpdateTableVisual(_previewTable, pos, pos, _tableSize.Rows, _tableSize.Columns);
        inkCanvas.Children.Add(_previewTable);

        // 드래그 중 실시간 행/열 HUD 배지 (방안 2) — 그리는 것은 창이다 (setTableBadge 이음매, 26단계).
        PushTableBadge(pos);
    }

    // ---- 이동·업·휠·방향키: 반환값은 "그리기 분기가 소비했는가" ----

    /// <summary>
    /// 눌린 채 이동. false면 호출자가 지우개 → 선택 사다리를 이어 탄다.
    /// 첫 줄의 <c>_lastPointerPos</c> 대입은 분기와 무관하게 일어난다 — 방향키 <see cref="AdjustTable"/>의 기준점이며,
    /// 옮기기 전에도 사다리 앞에서 무조건 대입했다.
    /// </summary>
    public bool Move(Point pos, bool shift, float pressure)
    {
        _lastPointerPos = pos;
        if (_stroke is not null && _activeStrokePath is not null)
        {
            if (_stroke.TryAppend(pos, pressure))
            {
                UpdateActiveStrokeVisual();
            }
            return true;
        }
        if (_previewShape is not null)
        {
            var end = ShapeGestureRules.ResolveEnd(_previewKind, _shapeStart, pos, shift);
            AnnotationVisualFactory.UpdateShapeVisual(_previewShape, _previewKind, _shapeStart, end);
            return true;
        }
        if (_previewTable is not null)
        {
            AnnotationVisualFactory.UpdateTableVisual(_previewTable, _tableStart, pos, _tableSize.Rows, _tableSize.Columns);
            PushTableBadge(pos);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 버튼 업. 임계 미만으로 <b>폐기</b>된 도형·표도 소비다 — 그 업이 선택 제스처 종료로 흐르면 안 된다
    /// (옮기기 전 if/else-if 사다리에서도 도달하지 않았다).
    /// </summary>
    public bool Up(Point pos, bool shift)
    {
        if (_stroke is not null)
        {
            CommitStroke();
            return true;
        }
        if (_previewShape is not null)
        {
            CommitShape(pos, shift);
            return true;
        }
        if (_previewTable is not null)
        {
            CommitTable(pos);
            return true;
        }
        return false;
    }

    /// <summary>표 드래그 중 휠 = 행·열 조절 (Shift는 열, <see cref="TableGestureRules.AxisForWheel"/>). 표 드래그 중이 아니면 false.</summary>
    public bool Wheel(Point pos, int notches, bool shift)
    {
        if (_previewTable is null)
        {
            return false;
        }
        _lastPointerPos = pos;
        return AdjustTable(TableGestureRules.AxisForWheel(shift), notches);
    }

    /// <summary>
    /// 표 드래그 중 행·열 조절 (25단계). 표 드래그 중이 아니면 false(입력을 소비하지 않는다).
    /// 드래그 중 행·열은 진행 중 값(<c>_tableSize</c>)이다 — AppState에는 <see cref="CommitTable"/>이 1회만 쓴다 (fix 57b043d):
    /// 노치마다 쓰면 AppState.Changed가 z-밴드 재적용·전 서피스 ApplyState·설정 저장 예약을 매번 돌린다.
    /// 시각물의 기준점은 마지막 포인터 위치다 — 휠은 호출 전에 그 값을 갱신하고, 방향키는 마지막 이동 위치를 쓴다.
    /// </summary>
    public bool AdjustTable(TableAxis axis, int delta)
    {
        if (_previewTable is null)
        {
            return false;
        }
        _tableSize = TableGestureRules.Adjust(_tableSize, axis, delta);
        AnnotationVisualFactory.UpdateTableVisual(_previewTable, _tableStart, _lastPointerPos, _tableSize.Rows, _tableSize.Columns);
        PushTableBadge(_lastPointerPos);
        return true;
    }

    /// <summary>
    /// 진행 중 획·도형·표를 <b>폐기</b>한다 — 커밋이 아니다 (원장 항목이 없으므로 미리보기 시각물만 지운다).
    /// 세 슬롯의 순서(획 → 도형 → 표)는 옮기기 전 <c>CancelActiveInput</c> 그대로이며, 표 슬롯은 표가 없어도
    /// 배지 null 힌트를 민다 — 배지를 지우는 것은 창이고, 커밋·폐기 어느 쪽이든 소멸 신호는 이 한 곳에서 나간다.
    /// </summary>
    public void DiscardAll()
    {
        DiscardStroke();
        DiscardShape();
        DiscardTable();
    }

    // ---- 획 ----

    private void UpdateActiveStrokeVisual()
    {
        if (_stroke is null || _activeStrokePath is null)
        {
            return;
        }
        _activeStrokePath.Data = StrokeGeometry.Create(
            _stroke.Points, _stroke.Pressures, _stroke.Style.Thickness, _stroke.Style.IsHighlighter);
    }

    private void CommitStroke()
    {
        if (_stroke is null || _activeStrokePath is null)
        {
            return;
        }
        var style = _stroke.Style;
        var element = new StrokeElement(_stroke.Points, style.Color, style.Thickness, style.IsHighlighter, _stroke.Pressures);
        inkCanvas.Children.Remove(_activeStrokePath);
        _stroke = null;
        _activeStrokePath = null;
        commitElement(element, style.IsFading);
    }

    /// <summary><c>Children.Remove</c>가 WPF라 헤드리스 코어로 내리지 않고 얇은 어댑터로 남긴다.</summary>
    private void DiscardStroke()
    {
        if (_activeStrokePath is null)
        {
            return;
        }
        inkCanvas.Children.Remove(_activeStrokePath);
        _stroke = null;
        _activeStrokePath = null;
    }

    // ---- 도형 ----

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
        commitElement(element, _activeShapeStyle.IsFading);
    }

    private void DiscardShape()
    {
        if (_previewShape is null)
        {
            return;
        }
        inkCanvas.Children.Remove(_previewShape);
        _previewShape = null;
    }

    // ---- 표 ----

    /// <summary>배지 힌트를 창에 민다 — 앵커는 포인터 위치, 크기는 진행 중 값. 창은 텍스트·위치만 갱신한다 (재구축 없음).</summary>
    private void PushTableBadge(Point pos) => setTableBadge(new TableBadgeHint(pos, _tableSize));

    private void CommitTable(Point rawEnd)
    {
        if (_previewTable is null)
        {
            return;
        }
        inkCanvas.Children.Remove(_previewTable);
        _previewTable = null;
        setTableBadge(null); // 배지 소멸 — 창이 지운다 (커밋·폐기 모두).

        // 임계는 도형과 같은 단일 상수(R2)를 **읽는다** — 리터럴 3을 다시 적으면 같은 양에 이름이 둘 생긴다 (1단계 교훈).
        if (!ShapeGestureRules.ShouldCommit(_tableStart, rawEnd))
        {
            return;
        }
        var element = new TableElement(_tableStart, rawEnd, _tableSize.Rows, _tableSize.Columns, _activeTableStyle.Color, _activeTableStyle.Thickness);
        commitElement(element, _activeTableStyle.IsFading);
        // 확정된 표의 행·열을 다음 표의 기본값으로 기억한다 — 여기 **한 번**만 AppState에 쓴다.
        // 취소되거나 임계 미달로 폐기된 드래그의 행·열은 기억하지 않는다 (fix: 노치마다 쓰던 것을 확정 시점으로).
        state.TableRows = _tableSize.Rows;
        state.TableColumns = _tableSize.Columns;
    }

    private void DiscardTable()
    {
        if (_previewTable is not null)
        {
            inkCanvas.Children.Remove(_previewTable);
            _previewTable = null;
        }
        setTableBadge(null); // 배지 소멸 — 창이 지운다 (커밋·폐기 모두).
    }
}
