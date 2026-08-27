using SSPen.Annotation;

namespace SSPen.Shell;

/// <summary>
/// 툴바 버튼 ↔ 상태 매핑 순수 함수 (god file 분할, ARCH-11 후속): 활성 판정·아이콘·배지 그룹.
/// 도형/펜 그룹 재클릭 로테이션 순환 데이터도 함께 소유한다.
/// </summary>
public static class ToolbarStateMap
{
    /// <summary>도형 그룹 재클릭 로테이션 순서 (선→화살표→사각형→타원).</summary>
    public static readonly ToolKind[] ShapeCycle = [ToolKind.Line, ToolKind.Arrow, ToolKind.Rectangle, ToolKind.Ellipse];

    /// <summary>펜 그룹 재클릭 로테이션 순서 (펜→형광펜→텍스트) — Epic Pen 펜+A 대응 (사용자 조타).</summary>
    public static readonly ToolKind[] PenCycle = [ToolKind.Pen, ToolKind.Highlighter, ToolKind.Text];

    /// <summary>그룹 버튼 재클릭 로테이션 (사용자 조타): 비활성 → 첫 도구, 활성 → 다음 하위 도구 순환.</summary>
    public static ToolKind NextInCycle(ToolKind[] cycle, ToolKind current)
    {
        int idx = Array.IndexOf(cycle, current);
        return idx < 0 ? cycle[0] : cycle[(idx + 1) % cycle.Length];
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
        ToolbarButtonId.Shapes => state.ActiveTool is ToolKind.Line or ToolKind.Arrow or ToolKind.Rectangle or ToolKind.Ellipse,
        ToolbarButtonId.Board => state.Board != BoardMode.None,
        // 페이딩 잉크는 도구가 아니라 토글이다 (사용자 요청 17차): 현재 도구와 무관하게
        // 토글 상태 그대로 보여준다 — 지우개로 잠시 전환해도 켜둔 것은 켜져 있음을 알려야 한다.
        ToolbarButtonId.Fading => state.FadingInk,
        _ => false,
    };

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
}
