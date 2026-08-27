namespace SSPen.Shell;

/// <summary>
/// 툴바 버튼 식별자 (god file 분할, ARCH-11 후속): 기존에는 한국어 툴팁 문자열(Strings.*)을
/// 딕셔너리 키로 썼으나, 표시 문자열과 식별자를 분리하기 위해 도입. 표시 문자열은
/// 여전히 Strings에서 가져와 툴팁에만 사용한다.
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
