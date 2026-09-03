using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary><see cref="SettingsSectionPlan"/>의 증인: 접힘 기본값·검색 판정·최소 크기.</summary>
public class SettingsSectionPlanTests
{
    [Fact]
    public void Order_CoversEverySection_ExactlyOnce()
    {
        Assert.Equal(Enum.GetValues<SettingsSection>().ToHashSet(), SettingsSectionPlan.Order.ToHashSet());
        Assert.Equal(SettingsSectionPlan.Order.Count, SettingsSectionPlan.Order.Distinct().Count());
        Assert.Equal(SettingsSection.General, SettingsSectionPlan.Order[0]);
    }

    /// <summary>단축키 21행만 접혀 있다 — 펼쳐 두면 자주 쓰는 일반 항목이 화면 밖으로 밀린다.</summary>
    [Fact]
    public void StartsExpanded_OnlyHotkeysIsCollapsed()
    {
        Assert.False(SettingsSectionPlan.StartsExpanded(SettingsSection.Hotkeys));
        Assert.All(
            SettingsSectionPlan.Order.Where(s => s != SettingsSection.Hotkeys),
            s => Assert.True(SettingsSectionPlan.StartsExpanded(s)));
    }

    [Fact]
    public void MinSize_LeavesRoomForTheLabelAndComboColumns()
    {
        // 라벨 220 + 조합 버튼 160 + 여백. 이보다 좁으면 두 열이 겹친다.
        Assert.True(SettingsSectionPlan.MinWidth >= 220 + 160);
        Assert.True(SettingsSectionPlan.DefaultHeight > SettingsSectionPlan.MinHeight);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeQuery_BlankIsNoFilter(string? query) =>
        Assert.Null(SettingsSectionPlan.NormalizeQuery(query));

    [Fact]
    public void NormalizeQuery_TrimsSurroundingSpace() =>
        Assert.Equal("펜", SettingsSectionPlan.NormalizeQuery("  펜 "));

    /// <summary>필터가 없으면 모두 보인다 — 빈 검색 상자가 목록을 비우면 안 된다.</summary>
    [Fact]
    public void MatchesHotkeyFilter_NoQuery_KeepsEveryRow() =>
        Assert.True(SettingsSectionPlan.MatchesHotkeyFilter("캡처", "Alt+Shift+S", null));

    [Fact]
    public void MatchesHotkeyFilter_ByName() =>
        Assert.True(SettingsSectionPlan.MatchesHotkeyFilter("전체 지우기", "Alt+Shift+7", "지우"));

    /// <summary>"Alt+Shift+S가 뭐였지"로 찾는 것도 이름으로 찾는 것만큼 흔하다.</summary>
    [Theory]
    [InlineData("Alt+Shift+S")]
    [InlineData("alt+shift+s")]
    [InlineData("shift+S")]
    public void MatchesHotkeyFilter_ByCombo_IgnoringCase(string query) =>
        Assert.True(SettingsSectionPlan.MatchesHotkeyFilter("캡처", "Alt+Shift+S", query));

    [Fact]
    public void MatchesHotkeyFilter_NoMatch_IsHidden() =>
        Assert.False(SettingsSectionPlan.MatchesHotkeyFilter("캡처", "Alt+Shift+S", "형광펜"));
}
