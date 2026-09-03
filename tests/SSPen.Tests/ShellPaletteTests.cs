using System.Windows.Media;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="ShellPalette"/>의 증인. 이 스위트의 존재 이유는 <b>대비 회귀를 빨간불로 만드는 것</b>이다 —
/// 색은 눈으로 봐서 "괜찮아 보이면" 통과하므로, 숫자로 잠그지 않으면 조용히 나빠진다.
/// 기준: WCAG 2.1 1.4.3(글자 4.5:1) / 1.4.11(UI 경계 3:1).
/// </summary>
public class ShellPaletteTests
{
    /// <summary>계산기 자체의 눈금 — 알려진 값 세 개로 공식을 고정한다.</summary>
    [Fact]
    public void ContrastRatio_KnownPairs()
    {
        Assert.Equal(21.0, ShellPalette.ContrastRatio(Colors.Black, Colors.White), 2);
        Assert.Equal(1.0, ShellPalette.ContrastRatio(Colors.White, Colors.White), 2);
        // 순서를 바꿔도 같다.
        Assert.Equal(
            ShellPalette.ContrastRatio(ShellPalette.Accent, Colors.White),
            ShellPalette.ContrastRatio(Colors.White, ShellPalette.Accent),
            6);
    }

    /// <summary>강조색 위에는 흰 글자가 얹힌다 (활성 버튼·보드 플라이아웃 선택 항목).</summary>
    [Fact]
    public void Accent_CarriesWhiteText_AtOrAbove_4_5()
    {
        double ratio = ShellPalette.ContrastRatio(ShellPalette.Accent, Colors.White);

        Assert.True(ratio >= 4.5, $"강조색 대비 {ratio:0.00}:1 — 흰 글자 기준 4.5:1 미달");
    }

    /// <summary>
    /// 브랜드 색은 로고 배지 전용이다. 강조색과 같아지면(예전 상태) 활성 버튼의 흰 글자가 2.55:1로 돌아간다.
    /// </summary>
    [Fact]
    public void Accent_IsNotTheBrandColor()
    {
        Assert.NotEqual(ShellPalette.Brand, ShellPalette.Accent);
        Assert.True(ShellPalette.ContrastRatio(ShellPalette.Brand, Colors.White) < 4.5); // 그래서 글자를 얹지 않는다
    }

    /// <summary>구분선·테두리는 UI 경계다 — 1.4.11의 3:1.</summary>
    [Theory]
    [InlineData(nameof(ShellPalette.Separator))]
    [InlineData(nameof(ShellPalette.StripBorder))]
    [InlineData(nameof(ShellPalette.SwatchBorder))]
    public void Boundaries_AgainstTheStrip_AtOrAbove_3(string token)
    {
        var color = token switch
        {
            nameof(ShellPalette.Separator) => ShellPalette.Separator,
            nameof(ShellPalette.StripBorder) => ShellPalette.StripBorder,
            _ => ShellPalette.SwatchBorder,
        };

        double ratio = ShellPalette.ContrastRatio(color, ShellPalette.Strip);

        Assert.True(ratio >= 3.0, $"{token} 대비 {ratio:0.00}:1 — 경계 기준 3:1 미달");
    }

    /// <summary>아이콘 글리프와 툴팁 보조 글자는 이미 통과했다 — 회귀 감시로만 남긴다.</summary>
    [Fact]
    public void Text_OnTheStrip_StaysReadable()
    {
        Assert.True(ShellPalette.ContrastRatio(ShellPalette.Icon, ShellPalette.Strip) >= 4.5);
        Assert.True(ShellPalette.ContrastRatio(ShellPalette.TooltipCombo, ShellPalette.Strip) >= 4.5);
    }

    /// <summary>
    /// 눌림은 호버보다 확실히 진해야 한다. 두 배경이 서로 구분되지 않으면 상태가 셋(평시·호버·눌림)이라는
    /// 사실 자체가 화면에 없다.
    /// </summary>
    [Fact]
    public void Pressed_IsDarkerThanHover()
    {
        Assert.True(ShellPalette.RelativeLuminance(ShellPalette.ButtonPressed)
            < ShellPalette.RelativeLuminance(ShellPalette.ButtonHover));
        Assert.True(ShellPalette.ContrastRatio(ShellPalette.ButtonPressed, ShellPalette.ButtonHover) >= 1.2);
    }

    /// <summary>
    /// 호버 배경 단독으로는 흰 스트립과 거의 구분되지 않는다 — 그래서 어포던스를 배경에만 맡기지 않고
    /// 1px 강조색 외곽선을 함께 그린다. 이 사실을 표로 남겨 둔다(외곽선을 지우면 근거가 사라진다).
    /// </summary>
    [Fact]
    public void Hover_Background_Alone_IsNotEnough_HenceTheOutline()
    {
        Assert.True(ShellPalette.ContrastRatio(ShellPalette.ButtonHover, ShellPalette.Strip) < 3.0);
        Assert.True(ShellPalette.ContrastRatio(ShellPalette.Accent, ShellPalette.ButtonHover) >= 3.0);
    }
}
