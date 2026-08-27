namespace SSPen.Annotation;

/// <summary>보드 표시 전이 종류 (사용자 요청 16차: 위→아래로 내려오고 다시 위로 걷힌다).</summary>
public enum BoardTransitionKind
{
    /// <summary>이번 상태 적용으로 바뀜 것이 없다 — 애니메이션을 다시 걸면 안 된다.</summary>
    None,

    /// <summary>보드가 화면 위에서 아래로 내려온다 (블라인드를 내리는 느낌).</summary>
    SlideDown,

    /// <summary>보드가 다시 위로 걷혀 사라진다 (완료 후 Collapsed).</summary>
    SlideUp,

    /// <summary>보드는 계속 보이고 색만 바뀜다 (화이트 ↔ 블랙). 애니메이션 없이 즉시 교체.</summary>
    Recolor,
}

/// <summary>
/// 보드 전이 판정 (순수 로직).
///
/// UI에서 분리한 이유: <c>ApplyState()</c>는 색·굵기·가시성 등 <b>모든</b> 상태 변경에 호출되므로
/// (AppState.Changed는 단일 coarse 이벤트다), 전이 판정 없이 애니메이션을 걸면 퀵컬러를 누를 때마다
/// 보드가 다시 내려와 덬그든다. "직전에 무엇이 적용되어 있었는가"와 "지금 무엇이어야 하는가"를
/// 비교해 <b>실제 전이일 때만</b> 애니메이션을 내보낸다.
/// </summary>
public static class BoardTransition
{
    /// <summary>
    /// 이 모니터에 보드를 그려야 하는가 (SEL 무관, Round 13 범위 규칙):
    /// 보드가 켜져 있고, 모든 화면 표시이거나 이 모니터가 주 모니터일 때.
    /// </summary>
    public static bool ShouldShow(BoardMode board, bool allMonitors, bool isPrimary) =>
        board != BoardMode.None && (allMonitors || isPrimary);

    /// <summary>
    /// 직전 적용 상태(<paramref name="wasShown"/>, <paramref name="previous"/>)와 목표 상태를 비교해
    /// 필요한 전이를 고른다.
    /// </summary>
    public static BoardTransitionKind Resolve(bool wasShown, BoardMode previous, bool shouldShow, BoardMode current)
    {
        if (!wasShown && shouldShow)
        {
            return BoardTransitionKind.SlideDown;
        }
        if (wasShown && !shouldShow)
        {
            return BoardTransitionKind.SlideUp;
        }
        if (wasShown && shouldShow && previous != current)
        {
            return BoardTransitionKind.Recolor;
        }
        return BoardTransitionKind.None;
    }
}
