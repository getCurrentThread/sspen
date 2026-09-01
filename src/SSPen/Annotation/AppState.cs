using System.Windows.Media;

namespace SSPen.Annotation;

public enum ToolKind
{
    None,
    Pen,
    Highlighter,
    Eraser,
    Line,
    Arrow,
    Rectangle,
    Ellipse,
    Table,
    Text,
    Select,    // 필기내용선택 (SEL-4): 어떤 ToolStyleGroup에도 속하지 않는다 (f12). 열거 말단에 추가.
}

/// <summary>굵기 5단계 (사용자 조타: Epic Pen 크기 선택기 5점 대응).</summary>
public enum ThicknessStep
{
    XSmall,
    Small,
    Medium,
    Large,
    XLarge,
}

public enum BoardMode
{
    None,
    White,
    Black,
}

/// <summary>색·굵기를 개별 보유하는 도구 그룹 (사용자 조타: 펜/형광펜/도형 개별 스타일).</summary>
public enum ToolStyleGroup
{
    Pen,
    Highlighter,
    Shape,
}

/// <summary>
/// 앱 전역 도구 상태 머신. 서피스/툴바/핫키가 모두 이 상태를 구독한다.
/// 상호작용 규칙: 서피스가 입력을 받는 조건 = SurfacesVisible && ActiveTool != None && !ClickThrough.
/// 색/굵기는 도구 그룹(펜·형광펜·도형)별로 보유하며, SyncToolStyles가 켜지면 세 그룹이 함께 움직인다
/// (기본값: 개별 보유 — 사용자 조타).
/// </summary>
public sealed class AppState
{
    /// <summary>바로가기 색상 칸 수 (툴바 2열 x 3행 모자이크 — 스펙 고정).</summary>
    public const int QuickColorCount = 6;

    // 바로가기 색상 (사용자 요청 17차): 설정에서 칸별로 바꿀 수 있다.
    // 상수 배열이었던 것을 인스턴스 상태로 내린 이유: 툴바 스와치·Ctrl+Shift+n 핫키·설정 창이
    // 전부 같은 6칸을 보게 하려면 색 목록이 상태여야 변경 통지(Changed)를 타고 퍼질 수 있다.
    private readonly Color[] _quickColors = [.. ColorPalette.DefaultQuickColors];

    // 그룹 인덱스 = (int)ToolStyleGroup. 기본값 (사용자 조타): 펜 팔레트 빨강 / 형광펜 노랑 / 도형 초록.
    private readonly Color[] _toolColors =
    [
        ColorPalette.DefaultQuickColors[5],
        ColorPalette.DefaultQuickColors[1],
        ColorPalette.DefaultQuickColors[4],
    ];

    private readonly ThicknessStep[] _toolThickness =
    [
        ThicknessStep.Medium,
        ThicknessStep.Medium,
        ThicknessStep.Medium,
    ];

    private ToolKind _activeTool = ToolKind.None;
    private bool _syncToolStyles;
    private BoardMode _board = BoardMode.None;
    private bool _boardAllMonitors = true;
    private bool _surfacesVisible = true;
    private bool _clickThrough;
    private bool _haloActive;
    private bool _wheelAdjustsPenSize = true;
    private bool _fadingInk;
    private BoardMode _defaultBoard = BoardMode.White;
    private int _tableRows = 3;
    private int _tableColumns = 3;

    /// <summary>어떤 하위 상태든 바뀌면 발생. 서피스가 상호작용/시각 상태를 재적용한다.</summary>
    public event Action? Changed;

    /// <summary>
    /// <see cref="ActiveTool"/> 값이 **실제로 전이**할 때만 발생 (이전, 현재). 선택집합 해제의 유일한 트리거다 (SEL-B-4).
    /// <see cref="Changed"/>와 달리 색·굵기·보드·가시성 변경에는 발화하지 않으므로,
    /// 퀵컬러를 눌러도 선택이 유지된다 (f12, SEL-AC-17).
    /// </summary>
    public event Action<ToolKind, ToolKind>? ActiveToolChanged;

    /// <summary>도구 선택. 도구를 잡으면 클릭 통과 선택이 해제된다 (상호 배타 선택 — 사용자 조타).</summary>
    public ToolKind ActiveTool
    {
        get => _activeTool;
        set => SetActiveTool(value);
    }

    /// <summary>
    /// <c>_activeTool</c>에 쓰는 **유일한** 경로 (ARCH-02/CRIT-01). 백킹 필드 직접 대입을 여기로 모아
    /// <see cref="ActiveToolChanged"/> 우회를 구조적으로 불가능하게 만든다.
    /// 발화 순서는 <see cref="Changed"/> → <see cref="ActiveToolChanged"/>로 고정한다.
    /// </summary>
    /// <returns>실제로 전이가 일어났으면 true. 호출부가 <see cref="Changed"/> 중복/누락을 판단하는 근거다 (ARCH-22).</returns>
    private bool SetActiveTool(ToolKind value)
    {
        if (_activeTool == value)
        {
            return false;
        }
        var previous = _activeTool;
        _activeTool = value;
        if (value != ToolKind.None)
        {
            _clickThrough = false;
        }
        Changed?.Invoke();
        ActiveToolChanged?.Invoke(previous, value);
        return true;
    }

    /// <summary>세 도구 그룹의 색·굵기를 동기화할지 (설정 항목, 기본 개별).</summary>
    public bool SyncToolStyles
    {
        get => _syncToolStyles;
        set
        {
            if (_syncToolStyles == value)
            {
                return;
            }
            _syncToolStyles = value;
            if (value)
            {
                // 동기화를 켜는 순간 활성 그룹 스타일로 통일.
                var color = ColorOf(ActiveStyleGroup);
                var step = ThicknessOf(ActiveStyleGroup);
                for (int i = 0; i < _toolColors.Length; i++)
                {
                    _toolColors[i] = color;
                    _toolThickness[i] = step;
                }
            }
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// 현재 색·굵기 조작이 적용되는 그룹 (도구 없음/지우개/선택 → 펜).
    /// 선택 도구에서도 **읽기 경로는 손대지 않는다** (SEL-B-2, f12-a): 포괄 폴백이 <c>Select</c>를 흡수해
    /// 강조 커서 후광이 펜 색으로 정상 표시된다. 무시 대상은 아래 쓰기 경로뿐이다.
    /// </summary>
    public ToolStyleGroup ActiveStyleGroup => _activeTool switch
    {
        ToolKind.Highlighter => ToolStyleGroup.Highlighter,
        ToolKind.Line or ToolKind.Arrow or ToolKind.Rectangle or ToolKind.Ellipse or ToolKind.Table or ToolKind.Text => ToolStyleGroup.Shape,
        _ => ToolStyleGroup.Pen,
    };

    /// <summary>바로가기 색상 읽기 (순서 = 툴바 모자이크 칸 순서 = Ctrl+Shift+1..6).</summary>
    public IReadOnlyList<Color> QuickColors => _quickColors;

    /// <summary>바로가기 색상 한 칸 지정 (설정 창). 범위 밖 인덱스는 무시한다.</summary>
    public void SetQuickColor(int index, Color color)
    {
        if (index < 0 || index >= _quickColors.Length || _quickColors[index] == color)
        {
            return;
        }
        _quickColors[index] = color;
        Changed?.Invoke();
    }

    public Color ColorOf(ToolStyleGroup group) => _toolColors[(int)group];

    public ThicknessStep ThicknessOf(ToolStyleGroup group) => _toolThickness[(int)group];

    /// <summary>활성 그룹의 색. 대입 시 동기화 모드면 세 그룹 모두 변경.</summary>
    public Color CurrentColor
    {
        get => ColorOf(ActiveStyleGroup);
        set => SetColor(ActiveStyleGroup, value);
    }

    /// <summary>활성 그룹의 굵기. 대입 시 동기화 모드면 세 그룹 모두 변경.</summary>
    public ThicknessStep Thickness
    {
        get => ThicknessOf(ActiveStyleGroup);
        set => SetThickness(ActiveStyleGroup, value);
    }

    /// <summary>활성 그룹 색 지정. 선택 도구 활성 중에는 **어떤 그룹의 스타일도 바꾸지 않는다** (SEL-5, f12).</summary>
    public void SetColor(ToolStyleGroup group, Color color)
    {
        if (_activeTool == ToolKind.Select)
        {
            return;
        }
        bool changed = false;
        foreach (var target in Targets(group))
        {
            if (_toolColors[(int)target] != color)
            {
                _toolColors[(int)target] = color;
                changed = true;
            }
        }
        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// 활성 그룹 굵기 지정. 선택 도구 활성 중에는 차단된다 (SEL-5).
    /// <see cref="StepThickness"/>와 휠 조정도 <see cref="Thickness"/> 프로퍼티를 경유하므로 함께 막힌다.
    /// <c>SyncToolStyles</c>는 SEL-5가 지목한 쓰기 경로가 아니므로 차단 대상이 아니다 (CRIT-15).
    /// </summary>
    public void SetThickness(ToolStyleGroup group, ThicknessStep step)
    {
        if (_activeTool == ToolKind.Select)
        {
            return;
        }
        bool changed = false;
        foreach (var target in Targets(group))
        {
            if (_toolThickness[(int)target] != step)
            {
                _toolThickness[(int)target] = step;
                changed = true;
            }
        }
        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private IEnumerable<ToolStyleGroup> Targets(ToolStyleGroup group) => _syncToolStyles
        ? [ToolStyleGroup.Pen, ToolStyleGroup.Highlighter, ToolStyleGroup.Shape]
        : [group];

    public BoardMode Board
    {
        get => _board;
        set => Set(ref _board, value);
    }

    /// <summary>보드 표시 범위: 기본 모든 화면, 설정에서 한 화면으로 변경 가능 (Round 13).</summary>
    public bool BoardAllMonitors
    {
        get => _boardAllMonitors;
        set => Set(ref _boardAllMonitors, value);
    }

    /// <summary>Alt+Shift+1 표시 토글.</summary>
    public bool SurfacesVisible
    {
        get => _surfacesVisible;
        set => Set(ref _surfacesVisible, value);
    }

    /// <summary>
    /// Alt+Shift+2 클릭 통과: 토글이 아닌 하나의 선택 (사용자 조타) — 선택 시 도구가 해제된다.
    ///
    /// 순서 계약 (CRIT-19 + ARCH-22): <c>_clickThrough</c>를 **먼저** 갱신해 구독자가 일관된 상태를 보게 한 뒤
    /// <see cref="SetActiveTool"/>로 도구를 해제한다. 한 논리적 변경에 <see cref="Changed"/>는 **정확히 1회**다 —
    /// 전이가 있었으면 <see cref="SetActiveTool"/> 안에서 이미 발화했고, 없었으면(클릭 통과 해제,
    /// 또는 도구가 이미 없음) 여기서 발화한다. 이 분기를 빠뜨리면 툴바 버튼이 실제 상태와 어긋난 채 남는다.
    /// </summary>
    public bool ClickThrough
    {
        get => _clickThrough;
        set
        {
            if (_clickThrough == value)
            {
                return;
            }
            _clickThrough = value;
            bool transitioned = value && SetActiveTool(ToolKind.None);
            if (!transitioned)
            {
                Changed?.Invoke();
            }
        }
    }

    /// <summary>강조 커서 (Round 13: 활성 시 항상 표시, 40px 후광).</summary>
    public bool HaloActive
    {
        get => _haloActive;
        set => Set(ref _haloActive, value);
    }

    /// <summary>마우스 휠로 펜 크기 조정 (설정 항목, WI-16).</summary>
    public bool WheelAdjustsPenSize
    {
        get => _wheelAdjustsPenSize;
        set => Set(ref _wheelAdjustsPenSize, value);
    }

    /// <summary>
    /// 페이딩 잉크 토글 (사용자 요청 17차). 도구가 아니라 **그리기 도구에 업히는 속성**이다:
    /// 켜면 펜·형광펜·도형·텍스트가 그대로 동작하면서 커밋된 요소만 시간이 지나 사라진다.
    ///
    /// 이전에는 <c>ToolKind.FadingPen</c>이라는 별도 도구였다. 그 구조의 한계:
    /// 페이딩을 쓰려면 반드시 자유선이어야 했고(도형·형광펜과 조합 불가),
    /// 페이딩을 켜는 순간 쓰던 도구가 해제됐다.
    /// </summary>
    public bool FadingInk
    {
        get => _fadingInk;
        set => Set(ref _fadingInk, value);
    }

    /// <summary>
    /// 보드 버튼·핫키가 켜는 기본 보드 색 (사용자 요청 17차: 설정에서 화이트↔블랙 선택).
    /// <see cref="BoardMode.None"/>은 기본값이 될 수 없으므로 무시한다 — 그런 값이 들어오면
    /// 보드 버튼이 아무것도 켜지 않는 죽은 버튼이 된다.
    /// </summary>
    public BoardMode DefaultBoard
    {
        get => _defaultBoard;
        set
        {
            if (value != BoardMode.None)
            {
                Set(ref _defaultBoard, value);
            }
        }
    }

    /// <summary>표 기본 행 수 (1..10).</summary>
    public int TableRows
    {
        get => _tableRows;
        set => Set(ref _tableRows, Math.Clamp(value, 1, 10));
    }

    /// <summary>표 기본 열 수 (1..10).</summary>
    public int TableColumns
    {
        get => _tableColumns;
        set => Set(ref _tableColumns, Math.Clamp(value, 1, 10));
    }

    /// <summary>
    /// 지금 그리는 요소가 페이딩 대상인가 = 토글이 켜져 있고 현재 도구가 그리기 도구일 때.
    /// </summary>
    public bool FadingApplies => _fadingInk && FadingAppliesTo(_activeTool);

    /// <summary>
    /// 페이딩이 업힐 수 있는 도구인가 (사용자 요청 17차: 펜·도형 조합).
    /// 지우개·선택·도구 없음은 새 요소를 만들지 않으므로 페이딩 개념이 성립하지 않는다.
    /// </summary>
    public static bool FadingAppliesTo(ToolKind tool) => tool is
        ToolKind.Pen or ToolKind.Highlighter or ToolKind.Text
        or ToolKind.Line or ToolKind.Arrow or ToolKind.Rectangle or ToolKind.Ellipse or ToolKind.Table;

    /// <summary>서피스가 마우스 입력을 받는가 (ARCH-1 히트테스트 배경 스위치와 연동).</summary>
    public bool IsInteractive => SurfacesVisible && ActiveTool != ToolKind.None && !ClickThrough;

    /// <summary>단계 → 펜 굵기 (논리 px), 5단계: 2/4/6/10/16.</summary>
    public static double PenPixels(ThicknessStep step) => step switch
    {
        ThicknessStep.XSmall => 2,
        ThicknessStep.Small => 4,
        ThicknessStep.Medium => 6,
        ThicknessStep.Large => 10,
        _ => 16,
    };

    /// <summary>펜 획 굵기 (펜 그룹).</summary>
    public double PenThickness => PenPixels(ThicknessOf(ToolStyleGroup.Pen));

    /// <summary>형광펜 획 굵기 (형광펜 그룹, 3배 폭).</summary>
    public double HighlighterThickness => PenPixels(ThicknessOf(ToolStyleGroup.Highlighter)) * 3;

    /// <summary>도형 획 굵기 (도형 그룹).</summary>
    public double ShapeThickness => PenPixels(ThicknessOf(ToolStyleGroup.Shape));

    /// <summary>텍스트 크기 (도형 그룹 연동, 5단계): 12/16/24/36/48.</summary>
    public double TextFontSize => ThicknessOf(ToolStyleGroup.Shape) switch
    {
        ThicknessStep.XSmall => 12,
        ThicknessStep.Small => 16,
        ThicknessStep.Medium => 24,
        ThicknessStep.Large => 36,
        _ => 48,
    };

    /// <summary>활성 그룹 굵기 한 단계 증감 (휠/핫키, 0..4 클램프).</summary>
    public void StepThickness(int direction)
    {
        int next = Math.Clamp((int)Thickness + direction, 0, 4);
        Thickness = (ThicknessStep)next;
    }

    /// <summary>화이트보드 활성 중 블랙보드를 누르면 전환 (Round 13).</summary>
    public void ToggleBoard(BoardMode requested)
    {
        Board = Board == requested ? BoardMode.None : requested;
    }

    /// <summary>
    /// 보드 그룹 버튼 다음 상태 (사용자 요청 15차): 꺼져 있으면 <paramref name="preferred"/>를 켜고,
    /// <b>켜져 있으면 색과 무관하게 끓다</b>.
    ///
    /// 이전 로테이션(없음→화이트→블랙→없음)을 버린 이유: 보드를 끄려면 버튼을 두 번
    /// 눌러야 했고, 그 사이에 의도하지 않은 블랙보드가 화면을 덤치는 게 당혹스러운다.
    ///
    /// 사용자 요청 17차: 켜지는 색이 화이트 고정이 아니라 설정값(<see cref="DefaultBoard"/>)이다.
    /// 반대션은 여전히 호버 플라이아웃과 Alt+Shift+W/B로 바로 갈 수 있다.
    /// </summary>
    public static BoardMode NextBoard(BoardMode current, BoardMode preferred) =>
        current == BoardMode.None ? preferred : BoardMode.None;

    private void Set<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            Changed?.Invoke();
        }
    }
}
