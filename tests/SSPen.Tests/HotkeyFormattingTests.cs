using SSPen.Settings;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="HotkeyFormatting"/>의 직접 증인 (67단계, A6-5). 설정 창·툴팁·로그·검색 필터가 모두 이 표기를 쓰는데,
/// 대화상자 파일 꼬리에 묻혀 있던 동안에는 ShellHotkeyMapTests의 "Ctrl+S"·"Alt+Shift+[ / ]"로만 간접 검증됐다.
/// </summary>
public class HotkeyFormattingTests
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    /// <summary>수식키 순서는 비트 순서가 아니라 Ctrl → Alt → Shift다.</summary>
    [Fact]
    public void Format_ModifierOrder_CtrlAltShift()
    {
        Assert.Equal("Ctrl+Alt+Shift+A", HotkeyFormatting.Format(new HotkeyDef(ModShift | ModAlt | ModControl, 0x41)));
    }

    [Fact]
    public void Format_NoModifiers_IsKeyNameOnly()
    {
        Assert.Equal("A", HotkeyFormatting.Format(new HotkeyDef(0, 0x41)));
    }

    /// <summary>특성화: Windows 수식키는 표기하지 않는다 (캡처도 그것을 버린다 — HotkeyCaptureRulesTests).</summary>
    [Fact]
    public void Format_WindowsModifier_IsNotShown()
    {
        Assert.Equal("Ctrl+A", HotkeyFormatting.Format(new HotkeyDef(ModWin | ModControl, 0x41)));
    }

    /// <summary>키 이름 표: 숫자·글자는 그대로, 대괄호, F1–F24, Space, PrtScn, 나머지는 16진수. 각 구간의 경계 밖도 함께 본다.</summary>
    [Theory]
    [InlineData(0x31u, "Ctrl+1")]
    [InlineData(0x30u, "Ctrl+0")]
    [InlineData(0x39u, "Ctrl+9")]
    [InlineData(0x3Au, "Ctrl+0x3A")]
    [InlineData(0x40u, "Ctrl+0x40")]
    [InlineData(0x41u, "Ctrl+A")]
    [InlineData(0x5Au, "Ctrl+Z")]
    [InlineData(0x5Bu, "Ctrl+0x5B")]
    [InlineData(0xDBu, "Ctrl+[")]
    [InlineData(0xDDu, "Ctrl+]")]
    [InlineData(0x6Fu, "Ctrl+0x6F")]
    [InlineData(0x70u, "Ctrl+F1")]
    [InlineData(0x87u, "Ctrl+F24")]
    [InlineData(0x88u, "Ctrl+0x88")]
    [InlineData(0x20u, "Ctrl+Space")]
    [InlineData(0x2Cu, "Ctrl+PrtScn")]
    [InlineData(0x25u, "Ctrl+0x25")]
    public void Format_KeyNames(uint vk, string expected)
    {
        Assert.Equal(expected, HotkeyFormatting.Format(new HotkeyDef(ModControl, vk)));
    }
}
