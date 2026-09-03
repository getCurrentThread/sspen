using System.Windows.Media;
using SSPen.Annotation;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="TextCommitRules.EditorBackdrop"/>의 증인. 편집 상자 아래는 사용자의 화면 아무거나이므로,
/// 배경이 글자색을 따라가지 않으면 확정 전까지 자기가 쓴 글자를 못 보는 경우가 생긴다.
/// </summary>
public class TextEditorBackdropTests
{
    [Theory]
    [InlineData(0xFF, 0xFF, 0xFF)] // 흰 글자
    [InlineData(0xFF, 0xFF, 0x00)] // 노랑
    public void LightText_GetsADarkScrim(byte r, byte g, byte b)
    {
        var backdrop = TextCommitRules.EditorBackdrop(Color.FromRgb(r, g, b));

        Assert.True(TextCommitRules.IsLight(Color.FromRgb(r, g, b)));
        Assert.False(TextCommitRules.IsLight(Color.FromRgb(backdrop.R, backdrop.G, backdrop.B)));
    }

    [Theory]
    [InlineData(0x00, 0x00, 0x00)] // 검정
    [InlineData(0xC0, 0x39, 0x2B)] // 빨강
    public void DarkText_GetsALightScrim(byte r, byte g, byte b)
    {
        var backdrop = TextCommitRules.EditorBackdrop(Color.FromRgb(r, g, b));

        Assert.False(TextCommitRules.IsLight(Color.FromRgb(r, g, b)));
        Assert.True(TextCommitRules.IsLight(Color.FromRgb(backdrop.R, backdrop.G, backdrop.B)));
    }

    /// <summary>
    /// 스크림은 <b>불투명에 가까워야</b> 아래 화면이 비쳐 글자를 방해하지 않는다. 예전 값은 알파 0x22였다.
    /// 완전 불투명이 아닌 이유: 어디에 쓰고 있는지 맥락(아래 그림)은 남아야 한다.
    /// </summary>
    [Fact]
    public void Scrim_IsMostlyOpaque_ButNotFully()
    {
        foreach (var text in new[] { Colors.White, Colors.Black })
        {
            var backdrop = TextCommitRules.EditorBackdrop(text);

            Assert.InRange(backdrop.A, (byte)0xA0, (byte)0xF0);
        }
    }

    /// <summary>글자와 스크림의 대비가 실제로 읽히는 수준인지 셸의 계산기로 확인한다.</summary>
    [Theory]
    [InlineData(0xFF, 0xFF, 0xFF)]
    [InlineData(0x00, 0x00, 0x00)]
    [InlineData(0xC0, 0x39, 0x2B)]
    public void TextOnItsScrim_IsReadable(byte r, byte g, byte b)
    {
        var text = Color.FromRgb(r, g, b);
        var backdrop = TextCommitRules.EditorBackdrop(text);

        // 스크림 자체의 알파는 무시하고 색끼리 비교한다 (아래 화면이 어떤 색이든 스크림이 지배한다).
        double ratio = ShellPalette.ContrastRatio(text, Color.FromRgb(backdrop.R, backdrop.G, backdrop.B));

        Assert.True(ratio >= 4.5, $"글자 #{r:X2}{g:X2}{b:X2} 대비 {ratio:0.00}:1");
    }
}
