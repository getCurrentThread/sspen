using System.Windows;
using System.Windows.Threading;
using SSPen.Annotation;
using SSPen.Settings;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 핫키 맵 구성 검증 (LD-4로 헤드리스화). <see cref="ShellHotkeys"/>가 <c>Application.Current</c> 대신
/// 주입된 <see cref="Dispatcher"/>를 쓰므로 WPF <c>Application</c> 없이 맵을 만들 수 있다.
/// R24 회귀 감시: 여기서 <c>Application.Current</c> 의존이 되살아나면 통합 스위트가 두 번째
/// STA 스레드부터 무너진다 (AppDomain당 Application 1개 + 디스패처의 생성 스레드 바인딩).
/// 50단계부터 도구 핫키의 "같은 도구 재선택 = 해제" 판정이 <see cref="ToolbarStateMap.ToggleTool"/> 하나임도 여기서 잠근다 —
/// 바인딩 Action은 <c>Dispatcher.Invoke</c>를 거치지만 생성 스레드(<c>Dispatcher.CurrentDispatcher</c>)에서는 인라인 실행이라 펌프가 필요 없다.
/// </summary>
public class ShellHotkeyMapTests
{
    // Win32 수식키 값을 직접 쓴다 — 이 스위트는 NativeMethods 상수가 아니라 등록되는 값(계약)을 검증한다.
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint AltShift = ModAlt | ModShift;
    private const uint CtrlShift = ModControl | ModShift;

    private static ShellHotkeys CreateSut(AppSettings? settings = null, AppState? state = null)
    {
        var effective = settings ?? new AppSettings();
        return new ShellHotkeys(
            Dispatcher.CurrentDispatcher,
            state ?? new AppState(),
            () => effective,
            undo: () => { },
            clearAll: () => { },
            startCapture: () => { },
            toggleToolbar: () => { },
            deleteSelection: () => { });
    }

    /// <summary>재지정 가능 항목 수 (기존 19 + SEL-16의 select/delete-selection 2).</summary>
    private const int RemappableCount = 21;

    /// <summary>스펙 고정 퀵컴러 6칸은 재지정 대상이 아니다.</summary>
    private const int QuickColorCount = 6;

    [Fact]
    public void BuildHotkeyMap_WithInjectedDispatcher_SucceedsWithoutApplication()
    {
        // 헤드리스 유닛 스위트에는 WPF Application이 없다 — 이 전제가 깨지면 검증이 무의미해진다.
        Assert.Null(Application.Current);

        var map = CreateSut().BuildHotkeyMap();

        Assert.Equal(RemappableCount + QuickColorCount, map.Count);
        Assert.All(map, binding => Assert.False(string.IsNullOrWhiteSpace(binding.Name)));
    }

    [Fact]
    public void BuildHotkeyMap_DefaultTable_UsesAltShiftForRemappableEntries()
    {
        var map = CreateSut().BuildHotkeyMap();

        var remappable = map.Take(RemappableCount).ToList();
        Assert.All(remappable, binding => Assert.Equal(AltShift, binding.Modifiers));

        var quickColors = map.Skip(RemappableCount).ToList();
        Assert.Equal(QuickColorCount, quickColors.Count);
        Assert.All(quickColors, binding => Assert.Equal(CtrlShift, binding.Modifiers));
    }

    [Fact]
    public void BuildHotkeyMap_DefaultTable_BindsUndoAndCaptureToSpecKeys()
    {
        var map = CreateSut().BuildHotkeyMap();

        Assert.Contains(map, b => b.Modifiers == AltShift && b.VirtualKey == VirtualKeys.D6);
        Assert.Contains(map, b => b.Modifiers == AltShift && b.VirtualKey == VirtualKeys.S);
    }

    /// <summary>
    /// SEL-16 / X8: 선택 도구는 Alt+Shift+V, 선택 삭제는 Alt+Shift+D로 기본 배정된다.
    /// 기존 바인딩과 중복되면 둘 중 하나는 등록에 실패해 조용히 안 먹히므로 중복 없음도 함께 못박는다.
    /// </summary>
    [Fact]
    public void BuildHotkeyMap_WithInjectedDispatcher_ContainsSelectAndDeleteActions()
    {
        var map = CreateSut().BuildHotkeyMap();

        Assert.Contains(map, b => b.Modifiers == AltShift && b.VirtualKey == VirtualKeys.V);
        Assert.Contains(map, b => b.Modifiers == AltShift && b.VirtualKey == VirtualKeys.D);

        // 전체 맵에 (수식키, 키) 중복이 없어야 한다.
        var combos = map.Select(b => (b.Modifiers, b.VirtualKey)).ToList();
        Assert.Equal(combos.Count, combos.Distinct().Count());
    }

    /// <summary>선택 관련 항목도 id 기반이므로 사용자 재지정을 그대로 상속받는다 (X8).</summary>
    [Fact]
    public void RemappableHotkeys_IncludesSelectionEntries()
    {
        var ids = CreateSut().RemappableHotkeys.Select(e => e.Id).ToList();

        Assert.Contains("select", ids);
        Assert.Contains("delete-selection", ids);
        Assert.Equal(RemappableCount, ids.Count);
    }

    [Fact]
    public void BuildHotkeyMap_RemappedEntry_UsesOverrideInsteadOfDefault()
    {
        var settings = new AppSettings();
        settings.Hotkeys["undo"] = new HotkeyDef(ModControl, VirtualKeys.A);

        var map = CreateSut(settings).BuildHotkeyMap();

        Assert.Contains(map, b => b.Modifiers == ModControl && b.VirtualKey == VirtualKeys.A);
        Assert.DoesNotContain(map, b => b.Modifiers == AltShift && b.VirtualKey == VirtualKeys.D6);
    }

    [Fact]
    public void BuildHotkeyMap_RepeatedIndependentConstruction_StaysStable()
    {
        // R24: 통합 스위트가 StaRunner.Run을 13회 이상 호출한다. Application 의존이 남아 있으면
        // 두 번째 호출부터 단일 인스턴스 제약 또는 죽은 디스패처로 깨진다. 헤드리스 등가 검증.
        for (int i = 0; i < 15; i++)
        {
            var map = CreateSut().BuildHotkeyMap();
            Assert.Equal(RemappableCount + QuickColorCount, map.Count);
        }

        Assert.Null(Application.Current);
    }

    [Fact]
    public void HotkeyLabel_KnownId_ReflectsEffectiveBinding()
    {
        var settings = new AppSettings();
        settings.Hotkeys["capture"] = new HotkeyDef(ModControl, VirtualKeys.S);
        var sut = CreateSut(settings);

        Assert.Equal("Ctrl+S", sut.HotkeyLabel("capture"));
        Assert.Null(sut.HotkeyLabel("no-such-hotkey"));
    }

    /// <summary>50단계: 도구 핫키 아홉 개가 모두 <see cref="ToolbarStateMap.ToggleTool"/>의 답을 <see cref="AppState.ActiveTool"/>에 넣는다.
    /// 세 출발 상태(없음 / 같은 도구 / 다른 도구)에서 스트립 버튼 경로와 갈라지면 여기서 빨간불 — 이전에는 ShellHotkeys가 같은 삼항식을 한 벌 더 갖고 있었다.</summary>
    [Theory]
    [InlineData("pen", ToolKind.Pen)]
    [InlineData("highlighter", ToolKind.Highlighter)]
    [InlineData("eraser", ToolKind.Eraser)]
    [InlineData("line", ToolKind.Line)]
    [InlineData("ellipse", ToolKind.Ellipse)]
    [InlineData("rectangle", ToolKind.Rectangle)]
    [InlineData("arrow", ToolKind.Arrow)]
    [InlineData("text", ToolKind.Text)]
    [InlineData("select", ToolKind.Select)]
    public void ToolHotkey_EveryToolId_AppliesToolbarStateMapToggleTool(string id, ToolKind tool)
    {
        var state = new AppState();
        var sut = CreateSut(state: state);
        var effective = sut.RemappableHotkeys.Single(e => e.Id == id).Effective;
        var binding = sut.BuildHotkeyMap().Single(b => b.Modifiers == effective.Modifiers && b.VirtualKey == effective.VirtualKey);

        foreach (var before in new[] { ToolKind.None, tool, tool == ToolKind.Pen ? ToolKind.Eraser : ToolKind.Pen })
        {
            state.ActiveTool = before;

            binding.Action();

            Assert.Equal(ToolbarStateMap.ToggleTool(before, tool), state.ActiveTool);
        }
    }

    /// <summary>같은 도구 재선택 → None, 다른 도구 → 그 도구: ToggleTool의 두 행을 핫키 경로로 한 번 더 글자 그대로 잠근다 (표가 바뀌면 위 Theory와 함께 깨진다).</summary>
    [Fact]
    public void ToolHotkey_SameToolTwice_ReleasesToNone()
    {
        var state = new AppState();
        var sut = CreateSut(state: state);
        var effective = sut.RemappableHotkeys.Single(e => e.Id == "pen").Effective;
        var pen = sut.BuildHotkeyMap().Single(b => b.Modifiers == effective.Modifiers && b.VirtualKey == effective.VirtualKey);

        pen.Action();
        Assert.Equal(ToolKind.Pen, state.ActiveTool);

        pen.Action();
        Assert.Equal(ToolKind.None, state.ActiveTool);
    }
}
