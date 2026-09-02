namespace SSPen.Annotation;

/// <summary>
/// 도구 분류표 (28단계): <see cref="ToolKind"/> 하나로 결정되는 상태 무관 순수 판정. AppState에 흩어져 있던 두 표
/// (<c>ActiveStyleGroup</c> switch, <c>FadingAppliesTo</c>)를 한 곳에 둔다 — 표(Table) 도구를 더할 때 둘을 따로 고쳐야
/// 했던 결함 클래스(837853a). <c>Enum.GetValues&lt;ToolKind&gt;</c> 전수 Theory가 두 표의 누락을 함께 잡는다.
/// </summary>
public static class ToolKindRules
{
    /// <summary>
    /// 색·굵기 조작이 적용되는 그룹 (도구 없음/지우개/선택 → 펜). 선택 도구에서도 <b>읽기 경로는 손대지 않는다</b>
    /// (SEL-B-2, f12-a): 포괄 폴백이 <c>Select</c>를 흡수해 강조 커서 후광이 펜 색으로 정상 표시된다. 무시 대상은
    /// AppState의 쓰기 경로뿐이다.
    /// </summary>
    public static ToolStyleGroup StyleGroupOf(ToolKind tool) => tool switch
    {
        ToolKind.Highlighter => ToolStyleGroup.Highlighter,
        ToolKind.Line or ToolKind.Arrow or ToolKind.Rectangle or ToolKind.Ellipse or ToolKind.Table or ToolKind.Text => ToolStyleGroup.Shape,
        _ => ToolStyleGroup.Pen,
    };

    /// <summary>
    /// 페이딩이 업힐 수 있는 도구인가 (사용자 요청 17차: 펜·도형 조합) — 단일 판정 지점 (AGENTS "Fading ink is a toggle").
    /// 지우개·선택·도구 없음은 새 요소를 만들지 않으므로 페이딩 개념이 성립하지 않는다.
    /// </summary>
    public static bool FadingAppliesTo(ToolKind tool) => tool is
        ToolKind.Pen or ToolKind.Highlighter or ToolKind.Text
        or ToolKind.Line or ToolKind.Arrow or ToolKind.Rectangle or ToolKind.Ellipse or ToolKind.Table;
}
