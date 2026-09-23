using System.Windows.Input;
using SSPen.Settings;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="HotkeyCaptureRules"/>의 증인 (67단계, A6-5). 예전에는 이 판정이 <c>HotkeyCaptureDialog.OnPreviewKeyDown</c> 안에만 있어
/// 어떤 키를 조합으로 받고, 무시하고, 대화상자 조작으로 흘리는지에 헤드리스 증인이 없었다.
/// 판정 순서(수식키 단독 → 대화상자 조작 키 → 수식키 매핑 → 수식키 없음)가 계약이다. 순수 함수라 기본 MTA 스레드에서 돈다.
/// </summary>
public class HotkeyCaptureRulesTests
{
    // Win32 수식키 값을 직접 쓴다 — 등록되는 값(계약)을 검증한다.
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;

    /// <summary>Alt가 눌리면 WPF는 <see cref="Key.System"/>을 보고하고 실제 키는 SystemKey에 담는다 — 기본 조합 Alt+Shift+S가 이 경로다.</summary>
    [Fact]
    public void Decide_AltShiftS_ViaSystemKey_Captures()
    {
        var verdict = HotkeyCaptureRules.Decide(Key.System, Key.S, ModifierKeys.Alt | ModifierKeys.Shift);

        Assert.Equal(HotkeyCaptureAction.Capture, verdict.Action);
        Assert.Equal(new HotkeyDef(ModAlt | ModShift, 0x53), verdict.Captured);
    }

    [Fact]
    public void Decide_CtrlShiftF5_Captures()
    {
        var verdict = HotkeyCaptureRules.Decide(Key.F5, Key.None, ModifierKeys.Control | ModifierKeys.Shift);

        Assert.Equal(HotkeyCaptureAction.Capture, verdict.Action);
        Assert.Equal(new HotkeyDef(ModControl | ModShift, 0x74), verdict.Captured);
    }

    /// <summary>SystemKey는 key가 <see cref="Key.System"/>일 때만 읽는다 — 아니면 key 자체가 조합의 키다.</summary>
    [Fact]
    public void Decide_NonSystemKey_IgnoresSystemKeyArgument()
    {
        var verdict = HotkeyCaptureRules.Decide(Key.P, Key.Q, ModifierKeys.Control);

        Assert.Equal(new HotkeyDef(ModControl, 0x50), verdict.Captured);
    }

    /// <summary>수식키 단독은 조합이 아니다 — 수식키가 함께 눌려 있어도(누르는 도중) 무시한다.</summary>
    [Theory]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.RightCtrl)]
    [InlineData(Key.LeftShift)]
    [InlineData(Key.RightShift)]
    [InlineData(Key.LeftAlt)]
    [InlineData(Key.RightAlt)]
    [InlineData(Key.LWin)]
    [InlineData(Key.RWin)]
    [InlineData(Key.None)]
    public void Decide_ModifierOnly_Ignored(Key key)
    {
        var verdict = HotkeyCaptureRules.Decide(key, Key.None, ModifierKeys.Control | ModifierKeys.Shift);

        Assert.Equal(HotkeyCaptureAction.Ignore, verdict.Action);
        Assert.Null(verdict.Captured);
    }

    /// <summary>Alt 단독도 <see cref="Key.System"/>으로 온다 — SystemKey로 풀어서 수식키 단독으로 본다.</summary>
    [Fact]
    public void Decide_AltAlone_ViaSystemKey_Ignored()
    {
        var verdict = HotkeyCaptureRules.Decide(Key.System, Key.LeftAlt, ModifierKeys.Alt);

        Assert.Equal(HotkeyCaptureAction.Ignore, verdict.Action);
        Assert.Null(verdict.Captured);
    }

    /// <summary>전역 핫키는 최소 1개의 수식키가 필요하다 — 맨 글자 키는 무시한다.</summary>
    [Fact]
    public void Decide_NoModifier_Ignored()
    {
        var verdict = HotkeyCaptureRules.Decide(Key.P, Key.None, ModifierKeys.None);

        Assert.Equal(HotkeyCaptureAction.Ignore, verdict.Action);
        Assert.Null(verdict.Captured);
    }

    /// <summary>
    /// 대화상자 조작 키는 수식키 매핑보다 먼저 본다 — 그래서 Ctrl+Shift+Esc도 기본 처리로 흘러 취소(IsCancel)가 동작한다.
    /// 순서를 뒤집으면 Esc가 조합으로 잡혀 대화상자를 키보드로 닫을 수 없다.
    /// </summary>
    [Theory]
    [InlineData(Key.Escape, ModifierKeys.None)]
    [InlineData(Key.Escape, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.Enter, ModifierKeys.None)]
    [InlineData(Key.Enter, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.Tab, ModifierKeys.None)]
    [InlineData(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift)]
    public void Decide_EscapeEnterTab_PassThrough_EvenWithModifiers(Key key, ModifierKeys modifiers)
    {
        var verdict = HotkeyCaptureRules.Decide(key, Key.None, modifiers);

        Assert.Equal(HotkeyCaptureAction.PassThrough, verdict.Action);
        Assert.Null(verdict.Captured);
    }

    /// <summary>Alt와 함께면 조작 키도 SystemKey로 온다 — 풀어서 본 뒤 통과시킨다.</summary>
    [Fact]
    public void Decide_AltTab_ViaSystemKey_PassThrough()
    {
        var verdict = HotkeyCaptureRules.Decide(Key.System, Key.Tab, ModifierKeys.Alt);

        Assert.Equal(HotkeyCaptureAction.PassThrough, verdict.Action);
    }

    /// <summary>특성화: Windows 수식키는 조합에 넣지 않고 조용히 버린다 (Win+Alt+X → Alt+X). 바꾸지 않고 고정만 한다.</summary>
    [Fact]
    public void Decide_WindowsModifier_IsDropped()
    {
        var verdict = HotkeyCaptureRules.Decide(Key.System, Key.X, ModifierKeys.Windows | ModifierKeys.Alt);

        Assert.Equal(HotkeyCaptureAction.Capture, verdict.Action);
        Assert.Equal(new HotkeyDef(ModAlt, 0x58), verdict.Captured);
    }

    /// <summary>특성화: Windows 수식키 하나뿐이면 수식키가 없는 것과 같다 — 무시한다.</summary>
    [Fact]
    public void Decide_WindowsModifierOnly_Ignored()
    {
        var verdict = HotkeyCaptureRules.Decide(Key.X, Key.None, ModifierKeys.Windows);

        Assert.Equal(HotkeyCaptureAction.Ignore, verdict.Action);
        Assert.Null(verdict.Captured);
    }

    /// <summary>세 수식키가 모두 조합에 들어간다 (Alt/Control/Shift → MOD_ALT/MOD_CONTROL/MOD_SHIFT).</summary>
    [Fact]
    public void Decide_AllThreeModifiers_MapToWin32Flags()
    {
        var verdict = HotkeyCaptureRules.Decide(Key.System, Key.D1, ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Shift);

        Assert.Equal(new HotkeyDef(ModAlt | ModControl | ModShift, 0x31), verdict.Captured);
    }
}
