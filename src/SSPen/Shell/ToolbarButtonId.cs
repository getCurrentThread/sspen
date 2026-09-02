namespace SSPen.Shell;

/// <summary>
/// 툴바 버튼 식별자 (god file 분할, ARCH-11 후속): 기존에는 한국어 툴팁 문자열(Strings.*)을
/// 딕셔너리 키로 썼으나, 표시 문자열과 식별자를 분리하기 위해 도입. 표시 문자열은
/// 여전히 Strings에서 가져와 툴팁에만 사용한다.
/// 각 id의 표시 속성(툴팁·아이콘·플라이아웃·배지 그룹·핫키 id·휠)은 <see cref="ToolbarLayout"/>이 데이터로 싣고,
/// 클릭 동작은 ToolbarStripBuilder.Build의 ActionFor 스위치가, 활성 판정은 <see cref="ToolbarStateMap.IsActive"/>가 잇는다 (51단계).
/// 값을 더하면 셋을 같이 늘려야 한다 — ActionFor는 던지고 IsActive는 <c>_ =&gt; false</c>로 조용히 실패한다 (X7/R9).
/// </summary>
public enum ToolbarButtonId
{
    Visibility,
    ClickThrough,
    Select,
    Shapes,
    Pen,
    Eraser,
    Fading,
    Preview,
    Undo,
    ClearAll,
    Board,
    Capture,
    Settings,
}
