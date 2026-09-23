using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="DialogOwnerRules"/>의 증인 (77단계, A1-2 → 99단계). 셸 안내 상자의 owner 우선순위 — 설정창 &gt; 툴바 &gt; 없음.
/// 툴바는 보이든 숨겨졌든 HWND가 있으면 owner다 (99단계): 숨긴 툴바를 뺐더니 Alt+Shift+7 확인 상자가 owner 없이 떠
/// 톱모스트 서피스·핀 밑에 숨었다. None은 툴바 창이 없을 때뿐이다.
/// </summary>
public class DialogOwnerRulesTests
{
    [Theory]
    [InlineData(true, ToolbarPresence.Visible, DialogOwner.Settings)]
    [InlineData(true, ToolbarPresence.Hidden, DialogOwner.Settings)]
    [InlineData(true, ToolbarPresence.Absent, DialogOwner.Settings)]
    [InlineData(false, ToolbarPresence.Visible, DialogOwner.Toolbar)]
    [InlineData(false, ToolbarPresence.Hidden, DialogOwner.Toolbar)]
    [InlineData(false, ToolbarPresence.Absent, DialogOwner.None)]
    public void Choose_SettingsThenToolbarWindow_ElseNone(bool settingsOpen, ToolbarPresence toolbar, DialogOwner expected)
    {
        Assert.Equal(expected, DialogOwnerRules.Choose(settingsOpen, toolbar));
    }

    /// <summary>
    /// 99단계 회귀 증인: 툴바를 숨기고 설정창도 닫은 채 Alt+Shift+7을 누르면 확인 상자가 숨긴 툴바에 물려야 한다.
    /// None이면 owner 없는 일반 창이 되어 톱모스트 서피스 밑에 뜨고, 펜 도구가 켜져 있으면 클릭을 서피스가 삼킨다.
    /// </summary>
    [Fact]
    public void Choose_HiddenToolbarAndSettingsClosed_OwnsByToolbar()
    {
        Assert.Equal(DialogOwner.Toolbar, DialogOwnerRules.Choose(settingsOpen: false, ToolbarPresence.Hidden));
    }
}
