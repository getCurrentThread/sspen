using SSPen.Annotation;

namespace SSPen.Shell;

/// <summary>
/// 버튼 항목이 여는 네 Popup(Shapes/Pen/Fading/Board)과 1:1 — <see cref="ToolbarFlyouts"/>의 여섯 Popup 중
/// ThicknessFlyout(미리보기 버튼 고유)·PaletteFlyout(현재 색 스와치 고유)은 버튼 어휘에 없다.
/// 실제 Popup 연결은 ToolbarStripBuilder.Build의 PopupFor 스위치가 잇는다 (51단계).
/// </summary>
public enum ToolbarFlyoutKind
{
    Shapes,
    Pen,
    Fading,
    Board,
}

/// <summary>버튼 위 휠 동작. None = 핸들러 없음(창의 전체 도구 순환으로 버블링). 실현은 ToolbarStripBuilder.Build의 Realize (51단계).</summary>
public enum ToolbarWheel
{
    None,
    ShapeCycle,
    PenCycle,
    FadingDuration,
}

/// <summary>
/// 스트립 항목 어휘 (닫힌 계층: 구분선·퀵컬러·미리보기·버튼). 델리게이트·WPF 객체를 싣지 않는다 — 그래서
/// <c>ToolbarLayoutTests</c>가 xUnit 기본 MTA 스레드에서 스냅샷을 찍을 수 있다 (51단계, ARCH-11, X7/R9).
/// </summary>
public abstract record ToolbarLayoutEntry;

/// <summary>그룹 구분선 (<see cref="ToolbarTheme.Separator"/>로 실현, 51단계).</summary>
public sealed record ToolbarSeparatorEntry : ToolbarLayoutEntry;

/// <summary>퀵컬러 6칸 + 현재 색 스와치 패널. 스와치 툴팁·팔레트 플라이아웃 연결은 ToolbarStripBuilder.Build의 BuildQuickColors 고유 (51단계).</summary>
public sealed record ToolbarQuickColorsEntry : ToolbarLayoutEntry;

/// <summary>현재 색·굵기 미리보기 버튼. 굵기 플라이아웃과 굵기 휠은 이 항목의 정의라 필드가 아니다 (51단계).</summary>
public sealed record ToolbarPreviewEntry(string Tooltip, string HotkeyId) : ToolbarLayoutEntry;

/// <summary>
/// 스트립 버튼 한 항목의 표시 속성 (ARCH-11, X7/R9): 툴팁·아이콘 쌍·여는 플라이아웃·색 배지 그룹·툴팁 핫키 id·휠 동작.
/// 클릭 동작은 ToolbarStripBuilder.Build의 ActionFor 스위치가, 보드 배지(Id == Board)는 같은 곳의 Realize가 붙인다 (51단계).
/// </summary>
public sealed record ToolbarButtonEntry(
    ToolbarButtonId Id,
    string Tooltip,
    (string Regular, string Filled) Icon,
    ToolbarFlyoutKind? Flyout,
    ToolStyleGroup? BadgeGroup,
    string? HotkeyId,
    ToolbarWheel Wheel) : ToolbarLayoutEntry
{
    /// <summary>플라이아웃 어포던스 삼각형·호버 시 다른 플라이아웃 닫기 판정 — 링크가 있으면 참 (따로 표현 불가).</summary>
    public bool HasFlyout => Flyout is not null;
}

/// <summary>
/// 툴바 스트립 레이아웃 스펙 (순수 데이터, 51단계, ARCH-11): 눈 버튼 + 접히는 메뉴의 항목 순서·그룹·구분선·버튼별 속성.
/// 48단계 스파이크(<c>ToolbarStripBuilderTests</c>)가 "창 없이 고정할 수 있다"고 증명한 스펙을 데이터로 뺀 것이다 —
/// 실현(시각 트리·클릭 동작·Popup 연결)은 ToolbarStripBuilder.Build가 한다. 여기에는 델리게이트·WPF 객체가 없다:
/// 동작은 전부 Build 인자(state/actions/창 콜백)를 닫아야 하고, Action은 비교할 수 없어 스펙에 실으면 스냅샷이 불가능하다.
/// 정적 초기화는 선언 순서다 — 구분선은 자리마다 새 인스턴스로 쓴다(공유 정적 없음).
/// </summary>
public static class ToolbarLayout
{
    /// <summary>눈 버튼: 메뉴 접기/펼치기 + 판서 동시 숨김/표시 (사용자 조타). Alt+Shift+1/트레이는 판서만 토글. 접히는 메뉴 위에 남는다.</summary>
    public static readonly ToolbarButtonEntry Visibility = new(
        ToolbarButtonId.Visibility, Strings.Visibility, Icons.Eye,
        Flyout: null, BadgeGroup: null, HotkeyId: null, Wheel: ToolbarWheel.None);

    /// <summary>
    /// 접히는 메뉴의 항목 순서 — 그룹 1 클릭 통과 / 2 선택·그리기·미리보기 / 3 편집 / 4 보드·캡처·설정 / 5 퀵컬러, 구분선 4개.
    /// 같은 순서 배열을 <c>ToolbarLayoutTests</c>(MTA, 스펙)와 <c>ToolbarStripBuilderTests</c>(STA, 실현)가 각각 든다.
    /// </summary>
    public static readonly IReadOnlyList<ToolbarLayoutEntry> Menu =
    [
        // 그룹 1: 클릭 통과.
        new ToolbarButtonEntry(
            ToolbarButtonId.ClickThrough, Strings.ClickThrough, Icons.Cursor,
            Flyout: null, BadgeGroup: null, HotkeyId: "clickthrough", Wheel: ToolbarWheel.None),
        new ToolbarSeparatorEntry(),

        // 선택 도구 (SEL-15): 기존 획을 고르고 이동·크기·회전한다. 그리기가 아니라 조작이므로
        // 그리기 도구 그룹 앞에 두고, 색·굵기를 쓰지 않으므로 색 배지도 없다 (SEL-5).
        new ToolbarButtonEntry(
            ToolbarButtonId.Select, Strings.Select, Icons.Select,
            Flyout: null, BadgeGroup: null, HotkeyId: "select", Wheel: ToolbarWheel.None),

        // 그룹 2: 그리기 도구 (도형·펜·형광펜은 각자 그룹 색 배지, 플라이아웃 어포던스 삼각형).
        new ToolbarButtonEntry(
            ToolbarButtonId.Shapes, Strings.Shapes, Icons.Shapes,
            Flyout: ToolbarFlyoutKind.Shapes, BadgeGroup: ToolStyleGroup.Shape, HotkeyId: null, Wheel: ToolbarWheel.ShapeCycle),

        // 펜 그룹 버튼 (사용자 조타: 펜·형광펜·텍스트를 한 그룹으로 — Epic Pen 펜+A 플라이아웃 대응).
        new ToolbarButtonEntry(
            ToolbarButtonId.Pen, Strings.Pen, Icons.Pen,
            Flyout: ToolbarFlyoutKind.Pen, BadgeGroup: ToolStyleGroup.Pen, HotkeyId: "pen", Wheel: ToolbarWheel.PenCycle),
        new ToolbarButtonEntry(
            ToolbarButtonId.Eraser, Strings.Eraser, Icons.Eraser,
            Flyout: null, BadgeGroup: null, HotkeyId: "eraser", Wheel: ToolbarWheel.None),

        // 페이딩 잉크 (사용자 요청 17차): 도구가 아니라 그리기 도구에 얹히는 토글.
        // 색 배지를 뗀 이유: 이제 자체 색이 없다 — 획 색은 현재 도구(펜·형광펜·도형)의 색을 따른다.
        // 지속 시간은 호버 플라이아웃에서 고른다.
        new ToolbarButtonEntry(
            ToolbarButtonId.Fading, Strings.HotkeyFadingInk, Icons.Timer,
            Flyout: ToolbarFlyoutKind.Fading, BadgeGroup: null, HotkeyId: "fading", Wheel: ToolbarWheel.FadingDuration),

        // 현재 색 + 굵기 미리보기 (Epic Pen의 채워진 원 대응): 활성 그룹 기준, 호버 시 굵기 선택기.
        new ToolbarPreviewEntry(Strings.Thickness, "thickness-pair"),
        new ToolbarSeparatorEntry(),

        // 그룹 3: 편집.
        new ToolbarButtonEntry(
            ToolbarButtonId.Undo, Strings.Undo, Icons.ArrowUndo,
            Flyout: null, BadgeGroup: null, HotkeyId: "undo", Wheel: ToolbarWheel.None),
        // 실행취소와 전체 지우기 사이의 구분선은 장식이 아니다: 30px 버튼이 여백 없이 맞붙어 있어
        // 되돌리려다 한 칸 아래를 눌러 전부 지우는 오클릭이 1px 차이로 일어난다.
        new ToolbarSeparatorEntry(),
        new ToolbarButtonEntry(
            ToolbarButtonId.ClearAll, Strings.ClearAll, Icons.Delete,
            Flyout: null, BadgeGroup: null, HotkeyId: "clear", Wheel: ToolbarWheel.None),
        new ToolbarSeparatorEntry(),

        // 그룹 4: 보드/캡처/설정. 보드 그룹 버튼 (사용자 조타 14차): 클릭 = 없음→화이트→블랙 로테이션,
        // 호버 플라이아웃 = 직접 선택, 활성 보드는 우상단 스와치 배지로 표시 — 배지 부착은 데이터가 아니라
        // ToolbarStripBuilder.Build의 Realize가 Id == Board로 잇는다 (ToolbarParts.RefreshButton과 같은 키).
        new ToolbarButtonEntry(
            ToolbarButtonId.Board, Strings.Board, Icons.Whiteboard,
            Flyout: ToolbarFlyoutKind.Board, BadgeGroup: null, HotkeyId: "whiteboard", Wheel: ToolbarWheel.None),
        new ToolbarButtonEntry(
            ToolbarButtonId.Capture, Strings.Capture, Icons.Camera,
            Flyout: null, BadgeGroup: null, HotkeyId: "capture", Wheel: ToolbarWheel.None),
        new ToolbarButtonEntry(
            ToolbarButtonId.Settings, Strings.Settings, Icons.Settings,
            Flyout: null, BadgeGroup: null, HotkeyId: null, Wheel: ToolbarWheel.None),
        new ToolbarSeparatorEntry(),

        // 그룹 5: 퀵컬러 6칸 (2열 x 3행) + 현재 색 대형 스와치 + 빠른 색상 확장.
        new ToolbarQuickColorsEntry(),
    ];
}
