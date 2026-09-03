using System.Windows.Media;
using SSPen.Annotation;

namespace SSPen.Shell;

/// <summary>
/// 툴바 버튼 ↔ 상태 매핑 순수 함수 (god file 분할, ARCH-11 후속): 활성 판정·아이콘·배지 그룹.
/// 도형/펜 그룹 재클릭 로테이션 순환 데이터도 함께 소유한다. 36단계부터 어댑터(ToolbarParts/StripBuilder/Flyouts/Window, 50단계부터 ShellHotkeys)에
/// 인라인이던 값 판정(점 지름 표·보드 배지·퀵스와치 링·현재 칸·같은 도구 재선택 해제)도 여기 둔다 — 입력은 AppState가
/// 아니라 값(ThicknessStep/BoardMode/Color/ToolKind)이라 헤드리스 표로 잠긴다 (X7/R9).
/// </summary>
public static class ToolbarStateMap
{
    /// <summary>도형 그룹 재클릭 로테이션 순서 (선→화살표→사각형→타원→표).</summary>
    public static readonly ToolKind[] ShapeCycle = [ToolKind.Line, ToolKind.Arrow, ToolKind.Rectangle, ToolKind.Ellipse, ToolKind.Table];

    /// <summary>펜 그룹 재클릭 로테이션 순서 (펜→형광펜→텍스트) — Epic Pen 펜+A 대응 (사용자 조타).</summary>
    public static readonly ToolKind[] PenCycle = [ToolKind.Pen, ToolKind.Highlighter, ToolKind.Text];

    /// <summary>툴바 휠 스크롤 도구 순환 순서.</summary>
    public static readonly ToolKind[] WheelToolCycle =
    [
        ToolKind.Select,
        ToolKind.Pen,
        ToolKind.Highlighter,
        ToolKind.Eraser,
        ToolKind.Line,
        ToolKind.Arrow,
        ToolKind.Rectangle,
        ToolKind.Ellipse,
        ToolKind.Table,
        ToolKind.Text,
    ];

    /// <summary>그룹 버튼 재클릭 로테이션 (사용자 조타): 비활성 → 첫 도구, 활성 → 다음 하위 도구 순환.</summary>
    public static ToolKind NextInCycle(ToolKind[] cycle, ToolKind current)
    {
        int idx = Array.IndexOf(cycle, current);
        return idx < 0 ? cycle[0] : cycle[(idx + 1) % cycle.Length];
    }

    /// <summary>
    /// 그룹 내 휠 스크롤 순환 (delta > 0 이전 도구/위쪽, delta < 0 다음 도구/아래쪽).
    /// </summary>
    public static ToolKind NextInCycle(ToolKind[] cycle, ToolKind current, int delta)
    {
        if (delta == 0)
        {
            return current;
        }
        int idx = Array.IndexOf(cycle, current);
        if (idx < 0)
        {
            return delta < 0 ? cycle[0] : cycle[^1];
        }
        int step = delta < 0 ? 1 : -1;
        int next = (idx + step + cycle.Length) % cycle.Length;
        return cycle[next];
    }

    /// <summary>
    /// 퀵컬러 슬롯 번호 휠 순환 (0..count-1).
    /// </summary>
    public static int NextQuickColorSlotByWheel(int currentSlot, int delta, int count)
    {
        if (count <= 0 || delta == 0)
        {
            return currentSlot;
        }
        int step = delta < 0 ? 1 : -1;
        return (currentSlot + step + count) % count;
    }

    /// <summary>
    /// 툴바 휠 스크롤에 따른 도구 순환 (delta > 0 이전 도구/위쪽, delta < 0 다음 도구/아래쪽).
    /// </summary>
    public static ToolKind NextToolByWheel(ToolKind current, int delta)
    {
        if (delta == 0)
        {
            return current;
        }
        int idx = Array.IndexOf(WheelToolCycle, current);
        if (idx < 0)
        {
            return delta < 0 ? WheelToolCycle[0] : WheelToolCycle[^1];
        }
        int step = delta < 0 ? 1 : -1;
        int next = (idx + step + WheelToolCycle.Length) % WheelToolCycle.Length;
        return WheelToolCycle[next];
    }

    public static bool IsActive(AppState state, ToolbarButtonId id, bool menuCollapsed) => id switch
    {
        ToolbarButtonId.Visibility => menuCollapsed,
        ToolbarButtonId.ClickThrough => state.ClickThrough,
        // X7/R9: enum 값과 이 분기는 반드시 같이 추가된다 — enum만 늘리면 `_ => false` 폴백으로
        // 버튼이 영원히 비활성으로 보이는 무증상 회귀가 된다.
        ToolbarButtonId.Select => state.ActiveTool == ToolKind.Select,
        ToolbarButtonId.Pen => state.ActiveTool is ToolKind.Pen or ToolKind.Highlighter or ToolKind.Text,
        ToolbarButtonId.Eraser => state.ActiveTool == ToolKind.Eraser,
        ToolbarButtonId.Shapes => state.ActiveTool is ToolKind.Line or ToolKind.Arrow or ToolKind.Rectangle or ToolKind.Ellipse or ToolKind.Table,
        ToolbarButtonId.Board => state.Board != BoardMode.None,
        // 페이딩 잉크는 도구가 아니라 토글이다 (사용자 요청 17차): 현재 도구와 무관하게
        // 토글 상태 그대로 보여준다 — 지우개로 잠시 전환해도 켜둔 것은 켜져 있음을 알려야 한다.
        ToolbarButtonId.Fading => state.FadingInk,
        _ => false,
    };

    /// <summary>
    /// 활성 글리프를 Filled 폰트로 그릴 것인가.
    ///
    /// 아이콘 표의 여섯 항목(텍스트·실행취소·전체 지우기·캡처·화살표·타원)은 Filled 코드포인트가 없어
    /// <c>Pair(x, x)</c>로 같은 값을 두 번 적어 두었다. 그 코드포인트를 Filled <b>폰트</b>로 그리면
    /// 그 폰트에 없는 글리프라 두부(.notdef)나 다른 그림이 나온다 — 활성일 때만 아이콘이 깨지는
    /// 증상이다. 같은 쌍이면 Regular 폰트를 유지한다.
    /// </summary>
    public static bool GlyphFontIsFilled((string Regular, string Filled) icon, bool active) =>
        active && !string.Equals(icon.Regular, icon.Filled, StringComparison.Ordinal);

    /// <summary>상태 연동 아이콘: 눈(접힘↔펼침), 펜 그룹(현재 선택 도구 반영).</summary>
    public static (string Regular, string Filled) IconFor(AppState state, ToolbarButtonId id, bool menuCollapsed, (string Regular, string Filled) fallback) => id switch
    {
        ToolbarButtonId.Visibility => menuCollapsed ? Icons.EyeOff : Icons.Eye,
        ToolbarButtonId.Pen => state.ActiveTool switch
        {
            ToolKind.Highlighter => Icons.Highlight,
            ToolKind.Text => Icons.TextT,
            _ => Icons.Pen,
        },
        // 사용자 조타 14차: 도형 그룹 버튼도 펜 그룹처럼 현재 선택 도형을 글리프로 반영.
        ToolbarButtonId.Shapes => state.ActiveTool switch
        {
            ToolKind.Line => Icons.Line,
            ToolKind.Arrow => Icons.ArrowUpRight,
            ToolKind.Rectangle => Icons.Square,
            ToolKind.Ellipse => Icons.Circle,
            ToolKind.Table => Icons.Table,
            _ => Icons.Shapes,
        },
        _ => fallback,
    };

    /// <summary>펜 그룹 버튼의 배지 그룹: 현재 선택 도구의 스타일 그룹 (개별 색 유지).</summary>
    public static ToolStyleGroup BadgeGroupFor(AppState state, ToolbarButtonId id, ToolStyleGroup fallback) => id == ToolbarButtonId.Pen
        ? state.ActiveTool switch
        {
            ToolKind.Highlighter => ToolStyleGroup.Highlighter,
            ToolKind.Text => ToolStyleGroup.Shape,
            _ => ToolStyleGroup.Pen,
        }
        : fallback;

    // ----- 36단계: 어댑터에 인라인이던 값 판정. 어댑터는 값을 읽어 넘기고 결과를 UI 속성에 쓰기만 한다. -----

    /// <summary>
    /// 스트립 미리보기 원 지름 (Epic Pen 채워진 원 대응). 플라이아웃 점(<see cref="FlyoutThicknessDotDiameter"/>)·
    /// <see cref="ThicknessScale"/>(펜 px)와 값이 일부 겹치지만 목적이 다른 표라 합치지 않는다 (f70c3fb의 원칙).
    /// </summary>
    public static double PreviewDotDiameter(ThicknessStep step) => step switch
    {
        ThicknessStep.XSmall => 8,
        ThicknessStep.Small => 11,
        ThicknessStep.Medium => 14,
        ThicknessStep.Large => 18,
        _ => 22,
    };

    /// <summary>굵기 플라이아웃의 실제 크기 점 5개 지름 (사용자 조타: 5단계, 라벨 없음).</summary>
    public static double FlyoutThicknessDotDiameter(ThicknessStep step) => step switch
    {
        ThicknessStep.XSmall => 6,
        ThicknessStep.Small => 10,
        ThicknessStep.Medium => 14,
        ThicknessStep.Large => 18,
        _ => 22,
    };

    /// <summary>보드 버튼 우상단 스와치 배지 표시 여부 (사용자 조타 14차): 보드가 없으면 숨김.</summary>
    public static bool BoardBadgeVisible(BoardMode board) => board != BoardMode.None;

    /// <summary>보드 배지 색: 블랙보드만 검정, 그 외(화이트·없음)는 흰색 — 없음일 때는 어차피 숨겨진다.</summary>
    public static bool BoardBadgeIsBlack(BoardMode board) => board == BoardMode.Black;

    /// <summary>
    /// 퀵컬러 스와치 흰 링 두께: 현재 색과 같은 칸만 2, 아니면 0 (플러시 모자이크 유지).
    /// 같은 색이 두 칸이면 둘 다 강조된다 — 보존이지 승인이 아니다.
    /// </summary>
    public static double QuickSwatchBorderThickness(Color slotColor, Color currentColor) => slotColor == currentColor ? 2 : 0;

    /// <summary>
    /// 선택 링 바깥쪽(강조색) 두께. 흰 링 하나만으로는 흰색·노랑·연회색 칸에서 사실상 보이지 않아,
    /// 어느 칸이 선택됐는지 알 수 없었다. 안쪽 흰 링 + 바깥 강조색 링의 이중 톤이라 밝은 색·어두운 색
    /// 어느 쪽에서도 한 겹은 대비된다.
    /// </summary>
    public static double QuickSwatchOuterRingThickness(Color slotColor, Color currentColor) => slotColor == currentColor ? 1 : 0;

    /// <summary>현재 색이 든 퀵컬러 칸 (첫 일치); 어느 칸에도 없으면 0 — 휠 순환의 출발점.</summary>
    public static int CurrentQuickColorSlot(IReadOnlyList<Color> quickColors, Color currentColor)
    {
        for (int i = 0; i < quickColors.Count; i++)
        {
            if (quickColors[i] == currentColor)
            {
                return i;
            }
        }
        return 0;
    }

    /// <summary>같은 도구 재선택 시 해제 (Epic Pen 동작: 도구 없음 = 포인터 모드) — 스트립 버튼·플라이아웃 항목·도구 핫키(<c>ShellHotkeys.SelectTool</c>, 50단계)가 같은 판정을 쓴다.</summary>
    public static ToolKind ToggleTool(ToolKind current, ToolKind requested) => current == requested ? ToolKind.None : requested;
}
