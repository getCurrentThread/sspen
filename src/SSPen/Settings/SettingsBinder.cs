using System.Windows.Media;
using System.Windows.Threading;
using SSPen.Annotation;

namespace SSPen.Settings;

/// <summary>
/// 설정 ↔ 상태 양방향 동기화 + 디바운스 저장 소유 (WI-14/WI-16, AppController에서 분리).
/// AppSettings/SettingsService/저장 디바운스 타이머/재진입 가드를 이 클래스가 소유한다.
/// </summary>
public sealed class SettingsBinder
{
    private readonly AppState _state;
    private readonly SettingsService _settingsService;
    private readonly FadingInkController _fading;
    private AppSettings _settings = new();
    private DispatcherTimer? _saveDebounce;
    private bool _applyingSettings;

    public SettingsBinder(AppState state, FadingInkController fading, SettingsService? settingsService = null)
    {
        _state = state;
        _fading = fading;
        _settingsService = settingsService ?? new SettingsService();
    }

    public AppSettings Settings => _settings;

    /// <summary>설정 로드 (WI-14). Start()에서 최초 1회 호출.</summary>
    public void Load() => _settings = _settingsService.Load();

    /// <summary>설정 → 상태 반영 (설정 로드 직후 / 일반 설정 적용 확인 시).</summary>
    public void ApplyToState()
    {
        // 아키텍트 3세대 블로커 수리: 적용 중 상태 대입이 Changed → SyncFromState를 재진입시켜
        // 아직 적용 전인 설정 필드(특히 SyncToolStyles=true)를 되write하던 문제 — 가드로 차단하고
        // 동기화 값은 상태 변형 전에 선독한다.
        bool sync = _settings.SyncToolStyles;
        _applyingSettings = true;
        try
        {
            _state.BoardAllMonitors = _settings.BoardAllMonitors;
            _state.DefaultBoard = _settings.DefaultBoardIsBlack ? BoardMode.Black : BoardMode.White;
            _state.HaloActive = _settings.HighlightCursor;
            _state.WheelAdjustsPenSize = _settings.WheelAdjustsPenSize;
            ApplyQuickColors();
            // 도구 그룹별 색·굵기 (사용자 조타: 기본 개별 보유). 동기화 플래그는 마지막에 적용
            // (먼저 켜면 그룹별 복원이 서로 덮어쓴다).
            _state.SyncToolStyles = false;
            _state.SetThickness(ToolStyleGroup.Pen, (ThicknessStep)Math.Clamp(_settings.PenThickness, 0, 4));
            _state.SetThickness(ToolStyleGroup.Highlighter, (ThicknessStep)Math.Clamp(_settings.HighlighterThickness, 0, 4));
            _state.SetThickness(ToolStyleGroup.Shape, (ThicknessStep)Math.Clamp(_settings.ShapeThickness, 0, 4));
            _state.SetColor(ToolStyleGroup.Pen, ColorPalette.Parse(_settings.PenColor, ColorPalette.DefaultQuickColors[5]));
            _state.SetColor(ToolStyleGroup.Highlighter, ColorPalette.Parse(_settings.HighlighterColor, ColorPalette.DefaultQuickColors[1]));
            _state.SetColor(ToolStyleGroup.Shape, ColorPalette.Parse(_settings.ShapeColor, ColorPalette.DefaultQuickColors[4]));
            _state.SyncToolStyles = sync;
            _fading.Duration = TimeSpan.FromSeconds(FadingDurations.Clamp(_settings.FadingSeconds));
        }
        finally
        {
            _applyingSettings = false;
        }
        // 적용 결과를 설정에 한 번만 역반영 (재진입 없이 일관 스냅샷 저장).
        SyncFromState();
    }

    /// <summary>
    /// 바로가기 색상 복원 (사용자 요청 17차). 칸 수가 모자라도 나머지를 기본색으로 채우고,
    /// 깨진 항목은 그 칸만 기본색으로 되돌린다 — 한 칸이 상해도 나머지 설정은 살린다.
    /// </summary>
    private void ApplyQuickColors()
    {
        var saved = _settings.QuickColors;
        for (int i = 0; i < AppState.QuickColorCount; i++)
        {
            var fallback = ColorPalette.DefaultQuickColors[i];
            var color = saved is not null && i < saved.Length
                ? ColorPalette.Parse(saved[i], fallback)
                : fallback;
            _state.SetQuickColor(i, color);
        }
    }

    /// <summary>상태 → 설정 역반영 (AppState.Changed 구독용).</summary>
    public void SyncFromState()
    {
        if (_applyingSettings)
        {
            return; // 설정 → 상태 적용 중의 Changed 재진입 차단 (3세대 HIGH 블로커).
        }
        _settings.PenColor = _state.ColorOf(ToolStyleGroup.Pen).ToString();
        _settings.PenThickness = (int)_state.ThicknessOf(ToolStyleGroup.Pen);
        _settings.HighlighterColor = _state.ColorOf(ToolStyleGroup.Highlighter).ToString();
        _settings.HighlighterThickness = (int)_state.ThicknessOf(ToolStyleGroup.Highlighter);
        _settings.ShapeColor = _state.ColorOf(ToolStyleGroup.Shape).ToString();
        _settings.ShapeThickness = (int)_state.ThicknessOf(ToolStyleGroup.Shape);
        _settings.SyncToolStyles = _state.SyncToolStyles;
        _settings.BoardAllMonitors = _state.BoardAllMonitors;
        _settings.DefaultBoardIsBlack = _state.DefaultBoard == BoardMode.Black;
        _settings.HighlightCursor = _state.HaloActive;
        _settings.QuickColors = [.. _state.QuickColors.Select(ColorPalette.ToHex)];
        // 페이딩 잉크는 그리기 도구에 업히는 토글 (사용자 요청 17차):
        // 토글이 켜져 있고 현재 도구가 그리기 도구일 때만 커밋 획이 페이드 대상이다.
        _fading.Active = _state.FadingApplies;
        ScheduleSave();
    }

    public void ScheduleSave()
    {
        if (_saveDebounce is null)
        {
            _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _saveDebounce.Tick += (_, _) =>
            {
                _saveDebounce!.Stop();
                _settingsService.Save(_settings);
            };
        }
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    public void SaveNow() => _settingsService.Save(_settings);

    /// <summary>페이딩 잉크 지속 시간을 범위(0.1~5초)로 재단해 저장 (툴바 플라이아웃/설정 콤보 공통 소유 지점).</summary>
    public void SetFadingDuration(double seconds)
    {
        _settings.FadingSeconds = FadingDurations.Clamp(seconds);
        _fading.Duration = TimeSpan.FromSeconds(_settings.FadingSeconds);
        ScheduleSave();
    }

    /// <summary>일반 설정 창 확인: 설정 교체 + 상태 재적용 + 즉시 저장.</summary>
    public void Replace(AppSettings updated)
    {
        _settings = updated;
        ApplyToState();
        SaveNow();
    }
}
