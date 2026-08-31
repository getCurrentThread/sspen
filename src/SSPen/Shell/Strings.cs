namespace SSPen.Shell;

/// <summary>
/// 한국어 문자열 확정본 (스펙 고정, Round 14). 사용자에게 보이는 모든 문자열은 이 테이블에서만 나온다
/// (플랜 원칙 5: AC-20을 한 곳에서 감사 가능하게).
/// 클릭 투과는 Epic Pen 공식 용어 "고스트 모드" 대신 "클릭 통과"를 쓴다.
/// </summary>
public static class Strings
{
    public const string AppName = "SS Pen";

    // 툴바 툴팁
    public const string Visibility = "표시";
    public const string ClickThrough = "클릭 통과";
    public const string Shapes = "도형";
    public const string Highlighter = "형광펜";
    public const string Pen = "펜";
    public const string Eraser = "지우개";
    public const string Select = "필기내용 선택";
    public const string Thickness = "굵기";
    public const string Undo = "실행 취소";
    public const string ClearAll = "전체 지우기";
    public const string Board = "보드";
    public const string Whiteboard = "화이트보드";
    public const string Blackboard = "블랙보드";
    public const string Capture = "캡처";
    public const string Settings = "설정";

    // 도형 플라이아웃
    public const string ShapeLine = "선";
    public const string ShapeArrow = "화살표";
    public const string ShapeRectangle = "사각형";
    public const string ShapeEllipse = "타원";
    public const string ShapeText = "텍스트";

    // 굵기 플라이아웃
    public const string ThicknessSmall = "작게";
    public const string ThicknessMedium = "보통";
    public const string ThicknessLarge = "크게";

    // 핫키 표시명 (툴팁과 다른 항목만 별도 상수 — 클리너 B3: 표시명은 이 테이블에서만 나온다)
    public const string HotkeyVisibility = "표시 토글";
    public const string HotkeyToolbar = "툴바 토글";
    public const string HotkeyThicker = "굵기 증가";
    public const string HotkeyThinner = "굵기 감소";
    public const string HotkeyFadingInk = "페이딩 잉크";
    public const string HotkeyDeleteSelection = "선택 삭제";
    public const string QuickColorName = "퀵컬러";

    // 색상
    public const string QuickColors = "빠른 색상";
    public const string QuickColorsExtended = "빠른 색상 확장";

    // 트레이
    public const string TrayEnable = "판서 켜기";
    public const string TrayDisable = "판서 끄기";
    public const string TraySettings = "설정";
    public const string TrayExit = "종료";

    // 캡처 도구모음
    public const string CaptureCopy = "복사";
    public const string CaptureSave = "저장";
    public const string CapturePin = "핀 고정";
    public const string CaptureCancel = "취소";

    // 설정 창
    public const string SettingsGeneral = "일반";
    public const string SettingsRunAtLogin = "윈도우 로그인 시 시작";
    public const string SettingsCheckUpdate = "시작 시 업데이트 확인";
    public const string SettingsHotkeys = "단축키";
    public const string SettingsSaveFolder = "스크린샷 저장 폴더";
    public const string SettingsWheelSize = "마우스 휠로 펜 크기를 조정합니다";
    public const string SettingsSyncToolStyles = "모든 도구가 같은 색과 굵기를 사용";
    public const string SettingsBoardAll = "모든 화면에 화이트보드 표시";
    public const string SettingsBoardSingle = "한 화면에 화이트보드 표시";

    // 보드 기본색 (사용자 요청 17차): 보드 버튼을 눌렀을 때 켜지는 색.
    public const string SettingsBoardDefault = "보드 버튼을 누를 때 켜질 보드";

    // 판서 화면 선택
    public const string SettingsMonitors = "판서 화면";
    public const string SettingsMonitorsHint = "판서 서피스를 띄울 화면을 선택합니다.";
    public const string PrimaryMonitorBadge = "(주 화면)";

    // 바로가기 색상 편집 (사용자 요청 17차).
    public const string SettingsQuickColors = "바로가기 색상";
    public const string SettingsQuickColorsHint = "칸을 눌러 색을 바꿉니다 (Ctrl+Shift+1~6).";
    public const string SettingsQuickColorsReset = "기본값으로";

    /// <summary>
    /// 페이딩 지속 시간 표기 (사용자 요청 16차: 0.1~5초 재조정).
    /// 짧게/보통/길게 고정 명칭을 버린 이유: 칸이 6개로 늘어 세 단계 이름으로는
    /// 대응되지 않고, 실제 초가 보이는 편이 고를 때 직관적이다. 예: "0.1초", "2초", "5초".
    /// </summary>
    public static string FadingDuration(double seconds) => $"{seconds:0.#}초";
    public const string SettingsHighlightCursor = "강조된 커서";
    public const string SettingsPressKeys = "키 조합을 누르세요";
    public const string SettingsOk = "확인";
    public const string SettingsCancel = "취소";

    // 진단/경고 (한국어 전용 제약)
    public const string HotkeyConflictWarning = "다음 단축키를 등록하지 못했습니다 (다른 앱과 충돌): ";
    public const string ClipboardCopyFailed = "클립보드 복사에 실패했습니다. 잠시 후 다시 시도하세요.";
    public const string FatalErrorTitle = "SS Pen 오류";
    public const string FatalErrorBody = "예기치 않은 오류가 발생했습니다. 로그 파일을 확인하세요: ";
}
