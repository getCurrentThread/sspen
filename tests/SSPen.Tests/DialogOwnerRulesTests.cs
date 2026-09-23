using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="DialogOwnerRules"/>의 증인 (77단계, A1-2). 셸 안내 상자의 owner 우선순위 — 설정창 &gt; 보이는 툴바 &gt; 없음.
/// '숨겨진 툴바는 owner로 못 쓴다'(1.3.5 실측 교훈)가 (false, false) = None 행이다.
/// </summary>
public class DialogOwnerRulesTests
{
    [Theory]
    [InlineData(true, true, DialogOwner.Settings)]
    [InlineData(true, false, DialogOwner.Settings)]
    [InlineData(false, true, DialogOwner.Toolbar)]
    [InlineData(false, false, DialogOwner.None)]
    public void Choose_SettingsThenVisibleToolbar_ElseNone(bool settingsOpen, bool toolbarVisible, DialogOwner expected)
    {
        Assert.Equal(expected, DialogOwnerRules.Choose(settingsOpen, toolbarVisible));
    }
}
