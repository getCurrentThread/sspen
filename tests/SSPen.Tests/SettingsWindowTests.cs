using System.Windows;
using System.Windows.Controls;
using SSPen.Shell;
using Xunit;
using static SSPen.Tests.StaThread;

namespace SSPen.Tests;

/// <summary>
/// 설정 창이 섹션을 <see cref="SettingsSectionPlan.Order"/>대로 쌓는지의 증인 (102단계, FINAL-REVIEW-SECTION-ORDER).
/// 73단계의 순서 테스트는 제품 코드가 읽지 않던 <c>Order</c>만 잠갔고 창은 섹션을 손으로 쌓았다 — 둘이 어긋나도 스위트는 초록이었다.
/// 이제 창이 <c>Order</c>를 돌며 섹션 → 요소 빌더를 부르므로, 여기서는 실현된 창이 그 순서를 따르는지와 빌더가 모든
/// <see cref="SettingsSection"/> 값을 다루는지(팔이 없으면 생성자가 던진다)를 잠근다.
/// 창은 띄우지 않는다(Show 없음) — 생성자가 만든 논리 트리만 읽는다. 테스트용 접근자를 창에 두지 않는다.
/// </summary>
public class SettingsWindowTests
{
    /// <summary>실현된 창의 섹션 머리 순서가 <see cref="SettingsSectionPlan.Order"/>와 같다 — 순서 단언이 실제 창을 잠근다.</summary>
    [Fact]
    public void Constructor_StacksSectionsInPlanOrder() => RunSta(() =>
    {
        var window = new SettingsWindow(new FakeSettingsHost());

        Assert.Equal(SettingsSectionPlan.Order, RealizedSections(window));
    });

    /// <summary>
    /// 전수 증인: 모든 <see cref="SettingsSection"/> 값이 창에 정확히 한 번 실현된다. 새 값에 빌더 팔이 없으면
    /// 생성자가 <see cref="ArgumentOutOfRangeException"/>으로 던져 여기서 빨간불이 된다(<c>Order</c>가 모든 값을 담는 것은
    /// <see cref="SettingsSectionPlanTests.Order_CoversEverySection_ExactlyOnce"/>가 잠근다).
    /// </summary>
    [Fact]
    public void Constructor_BuildsEverySectionExactlyOnce() => RunSta(() =>
    {
        var window = new SettingsWindow(new FakeSettingsHost());

        var realized = RealizedSections(window);
        Assert.Equal(Enum.GetValues<SettingsSection>().ToHashSet(), realized.ToHashSet());
        Assert.Equal(realized.Count, realized.Distinct().Count());
    });

    /// <summary>
    /// 특성화: 스크롤 영역의 직계 자식 전체 순서. 섹션 안의 행 순서(일반 섹션의 업데이트 줄·휠·동기화 등)와 섹션을 새 패널로
    /// 싸지 않는 평평한 배치가 102단계 이전 손 조립과 같다 — 트리가 같으니 배치·픽셀도 같다.
    /// </summary>
    [Fact]
    public void Constructor_ScrollStack_KeepsHandAssembledChildOrder() => RunSta(() =>
    {
        var window = new SettingsWindow(new FakeSettingsHost());

        Assert.Equal(
            [
                $"Text:{Strings.SettingsGeneral}",
                $"CheckBox:{Strings.SettingsRunAtLogin}",
                $"Panel[CheckBox:{Strings.SettingsCheckUpdate}]",
                $"CheckBox:{Strings.SettingsWheelSize}",
                $"CheckBox:{Strings.SettingsSyncToolStyles}",
                $"RadioButton:{Strings.SettingsBoardAll}",
                $"RadioButton:{Strings.SettingsBoardSingle}",
                $"Text:{Strings.SettingsBoardDefault}",
                $"RadioButton:{Strings.Whiteboard}",
                $"RadioButton:{Strings.Blackboard}",
                $"CheckBox:{Strings.SettingsHighlightCursor}",
                $"Text:{Strings.SettingsSaveFolder}",
                "Panel[TextBox]",
                $"Panel[Text:{Strings.SettingsMonitors}]",
                $"Text:{Strings.SettingsQuickColors}",
                $"Panel[Text:{Strings.SettingsQuickColorsHint}]",
                $"Text:{Strings.SettingsExperimental}",
                $"CheckBox:{Strings.SettingsZBandPolling}",
                $"Text:{Strings.SettingsZBandPollingHint}",
                $"Expander:{Strings.SettingsHotkeys}",
            ],
            ScrollStack(window).Children.Cast<UIElement>().Select(Describe).ToList());
    });

    /// <summary>섹션 머리 문구 — 테스트 쪽 표. 새 섹션이 생기면 여기에도 팔을 달아야 한다(없으면 던진다).</summary>
    private static string TitleOf(SettingsSection section) => section switch
    {
        SettingsSection.General => Strings.SettingsGeneral,
        SettingsSection.Monitors => Strings.SettingsMonitors,
        SettingsSection.QuickColors => Strings.SettingsQuickColors,
        SettingsSection.Experimental => Strings.SettingsExperimental,
        SettingsSection.Hotkeys => Strings.SettingsHotkeys,
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, "테스트 머리 문구 표에 없는 섹션"),
    };

    /// <summary>논리 트리를 문서 순서로 훑어 섹션 머리(굵은 <see cref="TextBlock"/> 또는 단축키 <see cref="Expander"/>)를 섹션으로 되읽는다.</summary>
    private static List<SettingsSection> RealizedSections(SettingsWindow window)
    {
        var byTitle = Enum.GetValues<SettingsSection>().ToDictionary(TitleOf);
        var found = new List<SettingsSection>();
        Walk(window);
        return found;

        void Walk(DependencyObject node)
        {
            string? title = node switch
            {
                Expander { Header: string header } => header,
                TextBlock { FontWeight: var weight } block when weight == FontWeights.Bold => block.Text,
                _ => null,
            };
            if (title is not null && byTitle.TryGetValue(title, out var section))
            {
                found.Add(section);
            }
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            {
                Walk(child);
            }
        }
    }

    /// <summary>창 내용 = DockPanel[아래 버튼 줄, ScrollViewer[섹션 StackPanel]].</summary>
    private static StackPanel ScrollStack(SettingsWindow window)
    {
        var root = Assert.IsType<DockPanel>(window.Content);
        var scroll = Assert.IsType<ScrollViewer>(root.Children[1]);
        return Assert.IsType<StackPanel>(scroll.Content);
    }

    private static string Describe(UIElement element) => element switch
    {
        Expander expander => $"Expander:{expander.Header}",
        TextBlock block => $"Text:{block.Text}",
        ContentControl { Content: string text } control => $"{control.GetType().Name}:{text}",
        ContentControl { Content: TextBlock block } control => $"{control.GetType().Name}:{block.Text}",
        Panel panel => $"Panel[{(panel.Children.Count > 0 ? Describe(panel.Children[0]) : string.Empty)}]",
        _ => element.GetType().Name,
    };
}
