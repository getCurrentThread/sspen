using System.Windows.Threading;
using SSPen.Annotation;
using SSPen.Interop;
using SSPen.Settings;

namespace SSPen.Shell;

/// <summary>
/// 핫키 테이블·유효 바인딩·표시 라벨 소유 (WI-4/WI-16, AppController에서 분리).
/// 실제 등록/해제는 HotkeyService가 담당하고, 이 클래스는 테이블 정의 + 맵 빌드 + 라벨 조회만 책임진다.
/// Undo/전체 지우기/캡처 시작/툴바 토글은 다른 컴포넌트가 소유하므로 델리게이트로 주입받는다.
/// UI 디스패처는 생성자로 주입받는다 (LD-4): <c>Application.Current</c>에 의존하면 통합 테스트가
/// STA 스레드마다 AppDomain 단일 <c>Application</c> 제약에 걸려 무너진다 (R24).
/// </summary>
public sealed class ShellHotkeys
{
    private readonly Dispatcher _dispatcher;
    private readonly AppState _state;
    private readonly Func<AppSettings> _settings;
    private readonly Action _undo;
    private readonly Action _clearAll;
    private readonly Action _startCapture;
    private readonly Action _toggleToolbar;
    private readonly Action _deleteSelection;

    public ShellHotkeys(
        Dispatcher dispatcher,
        AppState state,
        Func<AppSettings> settings,
        Action undo,
        Action clearAll,
        Action startCapture,
        Action toggleToolbar,
        Action deleteSelection)
    {
        _dispatcher = dispatcher;
        _state = state;
        _settings = settings;
        _undo = undo;
        _clearAll = clearAll;
        _startCapture = startCapture;
        _toggleToolbar = toggleToolbar;
        _deleteSelection = deleteSelection;
    }

    private sealed record HotkeyEntry(string Id, string Name, uint DefaultMods, uint DefaultVk, Action Action);

    private const uint AltShift = NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT;

    // 클리너 B3: 표시명은 전부 Strings 테이블에서만 나온다 (AC-20 단일 감사 지점).
    private List<HotkeyEntry> HotkeyTable() =>
    [
        new("visibility", Strings.HotkeyVisibility, AltShift, VirtualKeys.D1, () => _state.SurfacesVisible = !_state.SurfacesVisible),
        new("clickthrough", Strings.ClickThrough, AltShift, VirtualKeys.D2, () => _state.ClickThrough = !_state.ClickThrough),
        new("pen", Strings.Pen, AltShift, VirtualKeys.D3, () => ToggleTool(ToolKind.Pen)),
        new("highlighter", Strings.Highlighter, AltShift, VirtualKeys.D4, () => ToggleTool(ToolKind.Highlighter)),
        new("eraser", Strings.Eraser, AltShift, VirtualKeys.D5, () => ToggleTool(ToolKind.Eraser)),
        new("undo", Strings.Undo, AltShift, VirtualKeys.D6, _undo),
        new("clear", Strings.ClearAll, AltShift, VirtualKeys.D7, _clearAll),
        new("toolbar", Strings.HotkeyToolbar, AltShift, VirtualKeys.D0, _toggleToolbar),
        new("thicker", Strings.HotkeyThicker, AltShift, VirtualKeys.OemCloseBracket, () => _state.StepThickness(+1)),
        new("thinner", Strings.HotkeyThinner, AltShift, VirtualKeys.OemOpenBracket, () => _state.StepThickness(-1)),
        new("line", Strings.ShapeLine, AltShift, VirtualKeys.L, () => ToggleTool(ToolKind.Line)),
        new("ellipse", Strings.ShapeEllipse, AltShift, VirtualKeys.E, () => ToggleTool(ToolKind.Ellipse)),
        new("rectangle", Strings.ShapeRectangle, AltShift, VirtualKeys.U, () => ToggleTool(ToolKind.Rectangle)),
        new("arrow", Strings.ShapeArrow, AltShift, VirtualKeys.A, () => ToggleTool(ToolKind.Arrow)),
        new("text", Strings.ShapeText, AltShift, VirtualKeys.T, () => ToggleTool(ToolKind.Text)),
        new("whiteboard", Strings.Whiteboard, AltShift, VirtualKeys.W, () => _state.ToggleBoard(BoardMode.White)),
        new("blackboard", Strings.Blackboard, AltShift, VirtualKeys.B, () => _state.ToggleBoard(BoardMode.Black)),
        // 페이딩 잉크는 도구 선택이 아니라 토글이다 (사용자 요청 17차): 쓰던 도구를 유지한 채 업힌다.
        new("fading", Strings.HotkeyFadingInk, AltShift, VirtualKeys.F, () => _state.FadingInk = !_state.FadingInk),
        new("capture", Strings.Capture, AltShift, VirtualKeys.S, _startCapture),
        // X8: 기존 19개 Alt+Shift 바인딩과 퍼즘가 없는 V/D를 기본 배정한다.
        // id 기반이므로 사용자 재지정(RemappableHotkeys)을 그대로 상속받는다.
        new("select", Strings.Select, AltShift, VirtualKeys.V, () => ToggleTool(ToolKind.Select)),
        new("delete-selection", Strings.HotkeyDeleteSelection, AltShift, VirtualKeys.D, _deleteSelection),
    ];

    private HotkeyDef Effective(HotkeyEntry entry) =>
        _settings().Hotkeys.TryGetValue(entry.Id, out var overridden)
            ? overridden
            : new HotkeyDef(entry.DefaultMods, entry.DefaultVk);

    public List<HotkeyBinding> BuildHotkeyMap()
    {
        var ui = _dispatcher;
        var map = new List<HotkeyBinding>();
        foreach (var entry in HotkeyTable())
        {
            var def = Effective(entry);
            var action = entry.Action;
            map.Add(new HotkeyBinding(
                $"{entry.Name} ({HotkeyFormatting.Format(def)})",
                def.Modifiers, def.VirtualKey, () => ui.Invoke(action)));
        }

        // 바로가기 색상 6칸: 조합은 고정(Ctrl+Shift+1..6), **색은 설정에서 바뀌다** (사용자 요청 17차).
        // 끔을 때 색을 읽는 이유: 등록 시점에 값을 박아 두면 설정을 바꿔도 핫키는 옛 색을 칠한다.
        for (int i = 0; i < AppState.QuickColorCount; i++)
        {
            int index = i;
            map.Add(new HotkeyBinding(
                $"{Strings.QuickColorName} {index + 1} (Ctrl+Shift+{index + 1})",
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                (uint)(VirtualKeys.D1 + index),
                () => ui.Invoke(() => _state.CurrentColor = _state.QuickColors[index])));
        }
        return map;
    }

    /// <summary>툴팁용 현재 유효 핫키 조합 (재지정 반영).</summary>
    public string? HotkeyLabel(string hotkeyId)
    {
        if (hotkeyId == "thickness-pair")
        {
            string? thinner = HotkeyLabel("thinner");
            string? thicker = HotkeyLabel("thicker");
            return thinner is null || thicker is null ? null : $"{thinner} / {thicker}";
        }
        if (hotkeyId.StartsWith("quickcolor:", StringComparison.Ordinal)
            && int.TryParse(hotkeyId["quickcolor:".Length..], out int slot))
        {
            return $"Ctrl+Shift+{slot}";
        }
        var entry = HotkeyTable().FirstOrDefault(e => e.Id == hotkeyId);
        return entry is null ? null : HotkeyFormatting.Format(Effective(entry));
    }

    /// <summary>설정 창 재지정 목록 (id/표시명/현재 유효 바인딩).</summary>
    public IReadOnlyList<(string Id, string Name, HotkeyDef Effective)> RemappableHotkeys =>
        HotkeyTable().Select(entry => (entry.Id, entry.Name, Effective(entry))).ToList();

    private void ToggleTool(ToolKind tool) =>
        _state.ActiveTool = _state.ActiveTool == tool ? ToolKind.None : tool;
}
