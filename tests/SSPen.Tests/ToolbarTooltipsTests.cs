using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SSPen.Shell;
using Xunit;
using static SSPen.Tests.StaThread;

namespace SSPen.Tests;

/// <summary>
/// <see cref="ToolbarTooltips"/>의 증인 (37단계, TIP-REG/AC-20). ToolTip은 비주얼 트리 객체라 본문만 STA로 보낸다.
/// 펌프가 없어 ToolTipOpening은 실제로 오지 않으므로(ToolTipEventArgs 생성자도 internal) 둘째 줄 표기는 순수
/// <see cref="ToolbarTooltips.ComboLine"/>으로 따로 본다. 가짜 IShellActions는 공용 <see cref="FakeShellActions"/>(58단계 승격)의 HotkeyLabel 사전·호출 수를 쓴다.
/// </summary>
public class ToolbarTooltipsTests
{
    /// <summary>문자열 툴팁 금지 (AGENTS L89): 인스턴스여야 툴바 숨김 때 닫을 수 있고, 그 인스턴스가 정확히 한 번 등록된다.</summary>
    [Fact]
    public void Attach_RegistersTheOneToolTipInstance_AndPlacesItBelow() => RunSta(() =>
    {
        var target = new Border();
        var registered = new List<ToolTip>();

        ToolbarTooltips.Attach(new FakeShellActions(), target, "선 도구", "line", registered.Add);

        var tooltip = Assert.IsType<ToolTip>(target.ToolTip);
        Assert.Single(registered);
        Assert.Same(tooltip, registered[0]);
        Assert.Equal(PlacementMode.Bottom, ToolTipService.GetPlacement(target));
        Assert.Equal(300, ToolTipService.GetInitialShowDelay(target));
    });

    /// <summary>hotkeyId가 null이면 둘째 줄이 아예 없다 (빈 줄을 숨겨 두는 것이 아니라 만들지 않는다 — 오늘 동작).</summary>
    [Fact]
    public void Attach_NullHotkeyId_TitleLineOnly() => RunSta(() =>
    {
        var target = new Border();

        ToolbarTooltips.Attach(new FakeShellActions(), target, "설정", null, _ => { });

        var panel = Assert.IsType<StackPanel>(((ToolTip)target.ToolTip).Content);
        var title = Assert.IsType<TextBlock>(Assert.Single(panel.Children));
        Assert.Equal("설정", title.Text);
    });

    /// <summary>둘째 줄은 열릴 때 읽는다 (재지정 즉시 반영): Attach 시점에는 HotkeyLabel을 부르지 않고 빈 줄만 둔다.</summary>
    [Fact]
    public void Attach_WithHotkeyId_AddsEmptyComboLine_WithoutReadingLabelYet() => RunSta(() =>
    {
        var actions = new FakeShellActions();
        actions.Labels["pen"] = "Ctrl+Shift+P";
        var target = new Border();

        ToolbarTooltips.Attach(actions, target, "펜", "pen", _ => { });

        var panel = Assert.IsType<StackPanel>(((ToolTip)target.ToolTip).Content);
        Assert.Equal(2, panel.Children.Count);
        var combo = Assert.IsType<TextBlock>(panel.Children[1]);
        Assert.Equal(string.Empty, combo.Text);
        Assert.Equal(0, actions.LabelCalls);
    });

    /// <summary>제목 줄은 본문 크기, 단축키 줄은 보조 크기다 — 타입 스케일 토큰 (69단계, A5-7; 값은 치환 전 리터럴 12/11 그대로).</summary>
    [Fact]
    public void Attach_TitleAndComboLine_UseTypeScaleTokens() => RunSta(() =>
    {
        var target = new Border();

        ToolbarTooltips.Attach(new FakeShellActions(), target, "펜", "pen", _ => { });

        var panel = Assert.IsType<StackPanel>(((ToolTip)target.ToolTip).Content);
        Assert.Equal(ShellMetrics.FontBody, Assert.IsType<TextBlock>(panel.Children[0]).FontSize);
        Assert.Equal(ShellMetrics.FontCaption, Assert.IsType<TextBlock>(panel.Children[1]).FontSize);
    });

    [Fact]
    public void ComboLine_Label_ParenthesizedAndVisible()
    {
        Assert.Equal(("(Ctrl+Shift+L)", true), ToolbarTooltips.ComboLine("Ctrl+Shift+L"));
    }

    /// <summary>핫키 미할당(HotkeyLabel null)이면 둘째 줄은 비고 숨겨진다 — 빈 자리를 남기지 않는다.</summary>
    [Fact]
    public void ComboLine_NullLabel_EmptyAndHidden()
    {
        Assert.Equal((string.Empty, false), ToolbarTooltips.ComboLine(null));
    }
}
