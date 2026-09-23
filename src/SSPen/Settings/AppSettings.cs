namespace SSPen.Settings;

/// <summary>재지정 가능한 핫키 정의 (수식키 + 가상 키).</summary>
public sealed record HotkeyDef(uint Modifiers, uint VirtualKey);

/// <summary>
/// 앱 설정 POCO (WI-14, C1 확정): %APPDATA%\SS Pen\settings.json.
/// 마이그레이션 없이 추가 속성 + 기본값 전략 (개인용 앱).
/// UI 배율 설정은 명시적 제외 (CRIT-7, 이연 목록 5번).
/// </summary>
public sealed class AppSettings
{
    // 툴바 위치 (AC-21). NaN = 기본 위치(주 모니터 우측).
    public double? ToolbarLeft { get; set; }

    public double? ToolbarTop { get; set; }

    /// <summary>빈 문자열 = 기본값 (사진\SS Pen).</summary>
    public string SaveFolder { get; set; } = string.Empty;

    /// <summary>윈도우 로그인 시 시작 (AC-26: 설정에서 켤 수 있다). 기본은 꺼짐 — 설치 마법사 태스크(SSPen.iss)와 같은 기본값이다.</summary>
    public bool RunAtLogin { get; set; } = false;

    /// <summary>시작 시 업데이트 확인. 켜져 있으면 셸이 준비된 3초 뒤 한 번 확인한다 (AppController.Start). 기본은 켜짐.</summary>
    public bool CheckUpdateOnStart { get; set; } = true;

    /// <summary>마우스 휠로 펜 크기 조정.</summary>
    public bool WheelAdjustsPenSize { get; set; } = true;

    /// <summary>화이트보드 범위: true=모든 화면, false=한 화면 (Round 13).</summary>
    public bool BoardAllMonitors { get; set; } = true;

    /// <summary>
    /// 보드 버튼을 눌렀을 때 켜지는 색 (사용자 요청 17차): true=블랙, false=화이트.
    /// enum 대신 bool인 이유: 선택지가 둘뿐이고, JSON에 숫자(0/1/2)로 써져 사람이 읽기
    /// 어려워지는 것보다 true/false가 명확하다. BoardMode.None은 기본값이 될 수 없으므로
    /// 표현할 필요가 없다.
    /// </summary>
    public bool DefaultBoardIsBlack { get; set; }

    /// <summary>
    /// 바로가기 색상 6칸 (사용자 요청 17차: 설정에서 편집). #AARRGGBB 문자열.
    /// 칸 수가 모자라거나 항목이 깨졌으면 해당 칸만 기본색으로 되돌린다 (SettingsBinder).
    /// </summary>
    public string[] QuickColors { get; set; } = Annotation.ColorPalette.DefaultQuickColorHex();

    /// <summary>
    /// 페이딩 잉크 지속 시간(초). 범위 0.1~5 (사용자 요청 16차).
    /// 범위 밖 값은 로드 시 재단된다 — 이전 체계(3/6/12초)로 저장된 설정이 그대로 열려도
    /// 6→15가 아니라 5초로 안전하게 내려앉는다.
    /// </summary>
    public double FadingSeconds { get; set; } = Annotation.FadingDurations.Default;

    /// <summary>강조된 커서 (40px 후광).</summary>
    public bool HighlightCursor { get; set; }

    /// <summary>세 도구 그룹(펜/형광펜/도형)의 색·굵기 동기화 여부 (기본 개별 — 사용자 조타).</summary>
    public bool SyncToolStyles { get; set; }

    /// <summary>
    /// 실험적 기능: z-순서 주기 정정 (73단계, <c>Shell/ZBandPoller</c>). 2초마다 툴바·핀·서피스 순서를 확인해 어긋나면 바로잡는다.
    /// 기본은 켜짐(사용자 결정) — 속성이 없는 옛 settings.json도 켜진 채 열린다(추가 속성 + 기본값).
    /// </summary>
    public bool ZBandPolling { get; set; } = true;

    /// <summary>
    /// 펜 색 (#RRGGBB). 깨진 값은 로드 시 <see cref="Annotation.ColorPalette.DefaultToolColor"/>로 돌아온다.
    /// 기본값 리터럴은 JSON 표기 호환·가독성 때문에 hex 문자열로 둔다 — 원천은 <c>DefaultToolColor</c>이고 일치는 ToolStyleTests가 잠근다 (66단계, A4-5·A9-2).
    /// </summary>
    public string PenColor { get; set; } = "#E74C3C";

    /// <summary>
    /// 펜 굵기 단계 — <see cref="Annotation.ThicknessStep"/>의 정수값(기본 2 = 보통). 범위 밖 값은 로드 시
    /// <see cref="Annotation.ThicknessScale.FromStored"/>가 양끝 단계로 재단한다(상한은 열거 길이에서 온다).
    /// 기본값 리터럴 2는 JSON 표기 호환 때문에 정수로 둔다 — 원천은 <see cref="Annotation.ThicknessScale.Default"/>이고 일치는 ThicknessScaleTests가 잠근다 (66단계).
    /// </summary>
    public int PenThickness { get; set; } = 2;

    /// <summary>형광펜 색 (#RRGGBB). 폴백·리터럴 규칙은 <see cref="PenColor"/>와 같다.</summary>
    public string HighlighterColor { get; set; } = "#FEF200";

    /// <summary>형광펜 굵기 단계 — 재단·리터럴 규칙은 <see cref="PenThickness"/>와 같다.</summary>
    public int HighlighterThickness { get; set; } = 2;

    /// <summary>도형 색 (#RRGGBB). 폴백·리터럴 규칙은 <see cref="PenColor"/>와 같다.</summary>
    public string ShapeColor { get; set; } = "#1FD430";

    /// <summary>도형 굵기 단계 — 재단·리터럴 규칙은 <see cref="PenThickness"/>와 같다.</summary>
    public int ShapeThickness { get; set; } = 2;

    /// <summary>핫키 재지정 오버라이드 (id → 정의). 비어 있으면 스펙 기본 맵.</summary>
    public Dictionary<string, HotkeyDef> Hotkeys { get; set; } = [];

    /// <summary>
    /// 판서 서피스를 띄우지 않을 모니터 DeviceName 목록.
    /// 비어 있으면 연결된 모든 모니터에 판서 서피스를 띄운다.
    /// </summary>
    public List<string> DisabledMonitors { get; set; } = [];
}
