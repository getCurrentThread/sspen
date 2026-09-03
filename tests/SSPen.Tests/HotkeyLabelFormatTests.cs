using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary><see cref="HotkeyLabelFormat"/>의 증인: 그룹 버튼 툴팁 한 줄이 수식키 반복으로 접히지 않는다.</summary>
public class HotkeyLabelFormatTests
{
    [Fact]
    public void Compose_SharedModifiers_ArePrintedOnce() =>
        Assert.Equal(
            "Alt+Shift+L / A / U / E",
            HotkeyLabelFormat.Compose(["Alt+Shift+L", "Alt+Shift+A", "Alt+Shift+U", "Alt+Shift+E"]));

    /// <summary>재지정으로 수식키가 갈라지면 접을 수 없다 — 접으면 없는 조합을 가르치게 된다.</summary>
    [Fact]
    public void Compose_MixedModifiers_KeepsEachLabelWhole() =>
        Assert.Equal(
            "Alt+Shift+L / Ctrl+A",
            HotkeyLabelFormat.Compose(["Alt+Shift+L", "Ctrl+A"]));

    /// <summary>미할당(null)은 조용히 빠진다 — 표(Table)처럼 핫키가 없는 항목이 순환에 섞여 있다.</summary>
    [Fact]
    public void Compose_SkipsUnassigned() =>
        Assert.Equal("Alt+Shift+L / A", HotkeyLabelFormat.Compose(["Alt+Shift+L", null, "Alt+Shift+A", "  "]));

    /// <summary>남는 것이 없으면 null — 툴팁의 핫키 줄이 통째로 숨어야 빈 자리가 남지 않는다.</summary>
    [Fact]
    public void Compose_NothingAssigned_IsNull() =>
        Assert.Null(HotkeyLabelFormat.Compose([null, null]));

    [Fact]
    public void Compose_SingleLabel_IsUnchanged() =>
        Assert.Equal("Alt+Shift+L", HotkeyLabelFormat.Compose([null, "Alt+Shift+L"]));

    /// <summary>수식키 없는 조합(F2 등)은 접두가 없어 그대로 나열된다.</summary>
    [Fact]
    public void Compose_NoModifiers_JoinsPlainly() =>
        Assert.Equal("F2 / F3", HotkeyLabelFormat.Compose(["F2", "F3"]));
}
