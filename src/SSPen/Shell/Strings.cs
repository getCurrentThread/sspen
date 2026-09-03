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
    public const string ShapeTable = "표";

    /// <summary>표 드래그 HUD 배지: "3 × 4 표". 합성 루트가 창에 포맷터로 주입한다 — Annotation 계층은 Strings를 모른다 (26단계).</summary>
    public static string TableBadge(int rows, int columns) => $"{rows} × {columns} {ShapeTable}";
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

    /// <summary>설정 창 판서 화면 목록의 한 줄: "1번 화면: \\.\DISPLAY1 (1920×1080)". 모니터 크기는 물리 픽셀(Bounds)이다.</summary>
    public static string SettingsMonitorLabel(int index, string deviceName, int width, int height) =>
        $"{index}번 화면: {deviceName} ({width}×{height})";

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
    public const string SettingsExitApp = "프로그램 종료";
    public const string ExitConfirmMessage = "SS Pen을 종료하시겠습니까?";
    public const string SettingsCheckUpdateNow = "지금 확인";
    public const string SettingsCheckUpdateBtn = "업데이트 확인";
    public const string SettingsCurrentVersion = "현재 버전";

    // 업데이트
    public const string UpdateTitle = "SS Pen 업데이트";
    public const string UpdateAvailable = "새로운 버전이 있습니다.";
    public const string UpdateCurrentVersionLabel = "현재 버전:";
    public const string UpdateLatestVersionLabel = "최신 버전:";
    public const string UpdateReleaseNotesLabel = "변경 내용:";
    public const string UpdateReleaseNotesEmpty = "(릴리즈 설명이 없습니다.)";
    public const string UpdateNow = "지금 업데이트";
    public const string UpdateLater = "나중에";
    public const string UpdateOpenWebPage = "웹페이지 열기";
    public const string UpdateDownloading = "업데이트 다운로드 중...";
    public const string UpdateInstalling = "무음 설치 및 재시작 준비 중...";
    public const string UpdateFailedTitle = "업데이트 오류";
    public const string UpdateFailedMessage = "업데이트를 다운로드하거나 설치하지 못했습니다.\n웹페이지에서 직접 다운로드하시겠습니까?\n\n오류: ";
    public const string UpdateLatestAlready = "현재 최신 버전을 사용 중입니다.";
    public const string UpdateChecking = "업데이트를 확인하는 중...";
    public const string TrayCheckUpdate = "업데이트 확인";

    // 캡처 결과 알림 (토스트). 이전에는 저장·복사 성공이 전부 침묵이었고 실패는 일반 치명적 대화상자로 샜다.
    public const string CaptureSaved = "캡처를 저장했습니다";
    public const string CaptureSaveFailed = "캡처를 저장하지 못했습니다. 저장 폴더의 권한과 남은 공간을 확인하세요.";
    public const string CaptureCopied = "캡처를 클립보드에 복사했습니다";
    public const string CapturePinned = "캡처를 화면에 고정했습니다";
    public const string CapturePinFailed = "캡처를 화면에 고정하지 못했습니다.";
    public const string OpenFolder = "폴더 열기";

    /// <summary>저장 성공 토스트의 둘째 줄: 파일 이름만 보여 준다 (전체 경로는 토스트 폭을 넘긴다).</summary>
    public static string CaptureSavedDetail(string fileName) => $"{CaptureSaved}: {fileName}";

    // 핀 창 어포던스 (AC-14..18). 예전에는 모든 조작이 문서화되지 않은 제스처였고 화면에 단서가 없었다.
    public const string PinClose = "닫기";
    public const string PinClickThrough = "클릭 통과";
    public const string PinZoomReset = "원래 크기";
    public const string PinClickThroughBadge = "클릭 통과 중";
    public const string PinClickThroughHint = "되돌리려면 핀 위에서 Ctrl+가운데 버튼";
    public const string PinClickThroughEngaged = "이 핀은 클릭을 통과시킵니다. 되돌리려면 핀 위에서 Ctrl+가운데 버튼을 누르세요.";

    // 파괴적 조작 확인·결과 (AC-19)
    public const string ClearAllConfirmTitle = "전체 지우기";
    public const string ClearAllDone = "판서를 지웠습니다";
    public const string UndoNothing = "되돌릴 조작이 없습니다";

    /// <summary>핀은 실행취소 대상이 아니므로(원장은 판서 문서만 다룬다) 개수를 밝혀 확인을 받는다.</summary>
    public static string ClearAllConfirm(int pinCount) =>
        $"판서를 모두 지우고 고정된 캡처 {pinCount}개를 닫습니다.\n고정된 캡처는 실행 취소로 되돌릴 수 없습니다.\n계속할까요?";

    /// <summary>실행취소 조합키를 함께 알린다 — 지운 직후가 되돌리는 법을 알려 줄 유일한 시점이다.</summary>
    public static string ClearAllDoneWithUndo(string undoCombo) => $"{ClearAllDone} (되돌리기: {undoCombo})";

    // 설정 창 진단
    /// <summary>단축키 충돌: 어느 항목이 이미 쓰고 있는지 이름으로 알린다.</summary>
    public static string HotkeyAlreadyUsed(string name) => $"이미 \"{name}\"에 지정된 단축키입니다.";

    /// <summary>판서 화면을 모두 해제하면 첫 화면을 되살린다 — 조용히 되돌리면 사용자는 설정이 무시됐다고 읽는다.</summary>
    public static string MonitorRestored(string deviceName) =>
        $"판서 화면을 최소 하나는 켜 두어야 해서 {deviceName}을(를) 다시 켰습니다.";

    // 진단/경고 (한국어 전용 제약)
    public const string HotkeyConflictWarning = "다음 단축키를 등록하지 못했습니다 (다른 앱과 충돌): ";
    public const string ClipboardCopyFailed = "클립보드 복사에 실패했습니다. 잠시 후 다시 시도하세요.";
    public const string FatalErrorTitle = "SS Pen 오류";
    public const string FatalErrorBody = "예기치 않은 오류가 발생했습니다. 로그 파일을 확인하세요: ";
}
