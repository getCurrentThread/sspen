namespace SSPen.Shell;

/// <summary>
/// 셸 치수 토큰 (순수, DIP).
///
/// 왜 모으는가: 같은 값이 여러 파일에 손으로 적혀 있으면 한쪽만 고쳐도 아무도 모른다. 대표적으로
/// 스트립 폭 34는 <see cref="ToolbarStripBuilder"/>의 <c>Border.Width</c>와 <see cref="ToolbarPlacement"/>의
/// 배치 산술 양쪽에 있었고, 둘이 갈라지면 툴바가 화면 가장자리에서 어긋난 위치에 앉는다.
/// 여기 값은 <b>숫자만</b>이다 — <c>Brush</c>는 <c>Freezable</c>이라 MTA 테스트 스레드에서 만들 수 없으므로
/// 절대 두지 않는다(<c>ShellMetricsTests</c>가 리플렉션으로 감시한다).
/// </summary>
public static class ShellMetrics
{
    /// <summary>스트립 폭. <see cref="ToolbarPlacement.StripWidth"/>와 같아야 한다 (트립와이어 있음).</summary>
    public const double StripWidth = 34;

    /// <summary>
    /// 버튼 히트 타깃 한 변. 30 미만으로 내리면 손가락·펜 입력에서 놓친다. 스트립 버튼과 굵기 미리보기 버튼이 함께 쓴다
    /// (69단계, A5-7 — 미리보기만 리터럴 30이면 이 값을 올릴 때 스트립 열이 어긋난다).
    /// </summary>
    public const double ButtonSize = 30;

    /// <summary>버튼 글리프 크기 (플라이아웃 항목 글리프는 <see cref="FlyoutGlyphSize"/>, 세로 메뉴 행은 <see cref="MenuGlyphSize"/>).</summary>
    public const double GlyphSize = 20;

    /// <summary>도형·펜 플라이아웃 항목(아이콘 위·라벨 아래 타일)의 글리프 크기.</summary>
    public const double FlyoutGlyphSize = 18;

    /// <summary>세로 메뉴 행(설정 메뉴, 55단계)의 글리프 크기 — 라벨 옆에 앉으므로 타일 글리프보다 작다.</summary>
    public const double MenuGlyphSize = 16;

    /// <summary>도구 색 배지 지름 / 보드 스와치 배지 한 변.</summary>
    public const double ColorBadgeSize = 8;

    public const double BoardBadgeSize = 9;

    /// <summary>퀵컬러 한 칸 높이(2열 모자이크) / 현재 색 대형 스와치 높이.</summary>
    public const double QuickSwatchHeight = 15;

    public const double CurrentColorSwatchHeight = 18;

    /// <summary>모서리 반경: 스트립·플라이아웃 카드 6, 스와치 3, 배지 2.</summary>
    public const double CardRadius = 6;

    public const double SwatchRadius = 3;

    public const double BadgeRadius = 2;

    /// <summary>
    /// 타입 스케일: 보조 11 / 본문 12 / 섹션 머리 14. 보조는 플라이아웃 항목 라벨·툴팁 단축키 줄, 본문은 세로 메뉴 행 라벨·툴팁 제목이
    /// 쓴다 (69단계, A5-7).
    /// </summary>
    public const double FontCaption = 11;

    public const double FontBody = 12;

    public const double FontSection = 14;

    /// <summary>호버·눌림 외곽선 두께 — 배경만으로는 어포던스가 보이지 않는다 (ShellPaletteTests 참조).</summary>
    public const double FocusOutline = 1;
}
