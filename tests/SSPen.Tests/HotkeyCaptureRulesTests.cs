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

    // ── Shift 단독 + 글자 입력 키 거부 (80단계, A6-7) ──────────────────────────────────────────────
    // Shift는 글자 입력의 일부다. Shift+P를 전역으로 등록하면 WM_HOTKEY가 모든 앱의 대문자 P를 삼킨다 —
    // "최소 1개의 수식키"가 지키려던 "다른 앱 입력을 빼앗지 않는다"를 Shift 하나로는 지키지 못한다.

    /// <summary>회귀: Shift+P는 조합으로 잡히지 않는다 — 거부하고 이유 문구를 싣는다. 조합은 비워 둔다.</summary>
    [Fact]
    public void Decide_ShiftOnlyLetter_Rejected()
    {
        var verdict = HotkeyCaptureRules.Decide(Key.P, Key.None, ModifierKeys.Shift);

        Assert.Equal(HotkeyCaptureAction.Rejected, verdict.Action);
        Assert.Null(verdict.Captured);
        Assert.Equal(Strings.HotkeyShiftOnlyRejected, verdict.Reason);
    }

    /// <summary>회귀: Shift+1은 '!' 입력이다 — 거부한다.</summary>
    [Fact]
    public void Decide_ShiftOnlyDigit_Rejected()
    {
        var verdict = HotkeyCaptureRules.Decide(Key.D1, Key.None, ModifierKeys.Shift);

        Assert.Equal(HotkeyCaptureAction.Rejected, verdict.Action);
        Assert.Null(verdict.Captured);
    }

    /// <summary>회귀: Shift+기호 키({ &lt; _ ? ~ |)도 글자 입력이다 — 거부한다.</summary>
    [Theory]
    [InlineData(Key.OemOpenBrackets)]
    [InlineData(Key.OemComma)]
    [InlineData(Key.OemMinus)]
    [InlineData(Key.OemQuestion)]
    [InlineData(Key.OemTilde)]
    [InlineData(Key.Oem102)]
    public void Decide_ShiftOnlyOem_Rejected(Key key)
    {
        var verdict = HotkeyCaptureRules.Decide(key, Key.None, ModifierKeys.Shift);

        Assert.Equal(HotkeyCaptureAction.Rejected, verdict.Action);
        Assert.Null(verdict.Captured);
    }

    [Fact]
    public void Decide_ShiftOnlySpace_Rejected()
    {
        var verdict = HotkeyCaptureRules.Decide(Key.Space, Key.None, ModifierKeys.Shift);

        Assert.Equal(HotkeyCaptureAction.Rejected, verdict.Action);
    }

    [Fact]
    public void Decide_ShiftOnlyNumPad5_Rejected()
    {
        var verdict = HotkeyCaptureRules.Decide(Key.NumPad5, Key.None, ModifierKeys.Shift);

        Assert.Equal(HotkeyCaptureAction.Rejected, verdict.Action);
    }

    /// <summary>
    /// Windows 수식키는 조합에서 버려지므로(특성화 위) Win+Shift+P는 실제로 Shift+P로 등록된다 — 같은 이유로 거부한다.
    /// 판정이 등록될 수식키(mods)를 보고 하는 것이지 눌린 수식키 전체를 보는 것이 아님을 고정한다.
    /// </summary>
    [Fact]
    public void Decide_WindowsShiftLetter_Rejected()
    {
        var verdict = HotkeyCaptureRules.Decide(Key.P, Key.None, ModifierKeys.Windows | ModifierKeys.Shift);

        Assert.Equal(HotkeyCaptureAction.Rejected, verdict.Action);
    }

    /// <summary>글자를 입력하지 않는 키는 Shift 하나로도 지금처럼 잡는다 — 거부 이유 문구는 없다.</summary>
    [Fact]
    public void Decide_ShiftF5_Captured()
    {
        var verdict = HotkeyCaptureRules.Decide(Key.F5, Key.None, ModifierKeys.Shift);

        Assert.Equal(HotkeyCaptureAction.Capture, verdict.Action);
        Assert.Equal(new HotkeyDef(ModShift, 0x74), verdict.Captured);
        Assert.Null(verdict.Reason);
    }

    [Fact]
    public void Decide_ShiftInsert_Captured()
    {
        var verdict = HotkeyCaptureRules.Decide(Key.Insert, Key.None, ModifierKeys.Shift);

        Assert.Equal(HotkeyCaptureAction.Capture, verdict.Action);
        Assert.Equal(new HotkeyDef(ModShift, 0x2D), verdict.Captured);
    }

    /// <summary>회귀: Ctrl이나 Alt가 섞이면 글자 입력이 아니다 — 기본 표(Ctrl+Shift, Alt+Shift)는 그대로 잡힌다.</summary>
    [Fact]
    public void Decide_CtrlShiftP_Captured()
    {
        var verdict = HotkeyCaptureRules.Decide(Key.P, Key.None, ModifierKeys.Control | ModifierKeys.Shift);

        Assert.Equal(HotkeyCaptureAction.Capture, verdict.Action);
        Assert.Equal(new HotkeyDef(ModControl | ModShift, 0x50), verdict.Captured);
        Assert.Null(verdict.Reason);
    }

    /// <summary>순서 회귀: 대화상자 조작 키는 Shift 거부보다 먼저 본다 — Shift+Esc는 여전히 기본 처리로 흘러 취소가 동작한다.</summary>
    [Fact]
    public void Decide_ShiftEscape_StillPassThrough()
    {
        var verdict = HotkeyCaptureRules.Decide(Key.Escape, Key.None, ModifierKeys.Shift);

        Assert.Equal(HotkeyCaptureAction.PassThrough, verdict.Action);
        Assert.Null(verdict.Reason);
    }

    /// <summary>
    /// 글자 입력 키 집합의 경계 (명시 집합: 0x20, 0x30–0x39, 0x41–0x5A, 0x60–0x6F, 0xBA–0xC0, 0xDB–0xDF, 0xE2).
    /// PrtScn·Insert·Delete·방향키·F1 같은 비입력 키는 집합 밖이다.
    /// </summary>
    [Theory]
    [InlineData(0x20u, true)]   // Space
    [InlineData(0x21u, false)]  // PageUp
    [InlineData(0x25u, false)]  // Left
    [InlineData(0x2Cu, false)]  // PrtScn
    [InlineData(0x2Du, false)]  // Insert
    [InlineData(0x2Eu, false)]  // Delete
    [InlineData(0x2Fu, false)]  // Help
    [InlineData(0x30u, true)]   // 0
    [InlineData(0x39u, true)]   // 9
    [InlineData(0x3Au, false)]
    [InlineData(0x40u, false)]
    [InlineData(0x41u, true)]   // A
    [InlineData(0x5Au, true)]   // Z
    [InlineData(0x5Bu, false)]  // LWin
    [InlineData(0x5Fu, false)]  // Sleep
    [InlineData(0x60u, true)]   // NumPad0
    [InlineData(0x6Fu, true)]   // Divide
    [InlineData(0x70u, false)]  // F1
    [InlineData(0xB9u, false)]
    [InlineData(0xBAu, true)]   // ;
    [InlineData(0xC0u, true)]   // `
    [InlineData(0xC1u, false)]
    [InlineData(0xDAu, false)]
    [InlineData(0xDBu, true)]   // [
    [InlineData(0xDFu, true)]   // OEM_8
    [InlineData(0xE0u, false)]
    [InlineData(0xE1u, false)]
    [InlineData(0xE2u, true)]   // OEM_102
    [InlineData(0xE3u, false)]
    public void IsTextInputKey_Boundaries_MatchExplicitSet(uint vk, bool expected)
    {
        Assert.Equal(expected, HotkeyCaptureRules.IsTextInputKey(vk));
    }

    /// <summary>거부 문구는 대화상자가 그대로 보여 준다 — 비면 사용자는 왜 눌러도 안 잡히는지 알 수 없다.</summary>
    [Fact]
    public void HotkeyShiftOnlyRejected_Text_IsNotBlank()
    {
        Assert.False(string.IsNullOrWhiteSpace(Strings.HotkeyShiftOnlyRejected));
    }
}
