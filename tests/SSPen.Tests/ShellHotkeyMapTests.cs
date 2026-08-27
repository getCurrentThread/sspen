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
/// </summary>
public class ShellHotkeyMapTests
{
    // NativeMethods는 internal이고 이 레포에는 InternalsVisibleTo가 없다 — Win32 수식키 값을 직접 쓴다.
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint AltShift = ModAlt | ModShift;
    private const uint CtrlShift = ModControl | ModShift;

    private static ShellHotkeys CreateSut(AppSettings? settings = null)
    {
        var effective = settings ?? new AppSettings();
        return new ShellHotkeys(
            Dispatcher.CurrentDispatcher,
            new AppState(),
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
}
