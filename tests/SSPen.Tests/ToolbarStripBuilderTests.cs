using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Shapes;
using SSPen.Annotation;
using SSPen.Shell;
using Xunit;

using static SSPen.Tests.StaThread;
namespace SSPen.Tests;

/// <summary>
/// 툴바 스트립 조립의 헤드리스 스파이크 (48단계, ARCH-11/X7). <see cref="ToolbarStripBuilder.Build"/>를 <c>Application</c>도
/// <c>Window</c>도 없는 STA에서 구동한다 — <c>Icons.Regular</c>의 pack:// URI는 스킴 등록만 있으면 <c>FontFamily</c> 생성까지
/// 통과한다 (글리프 해석은 렌더 시점이고 여기서는 measure하지 않는다). 프로덕션에서는 첫 <c>Window</c>의 정적 초기화가 하는
/// 등록을 <c>System.IO.Packaging.PackUriHelper</c> 정적 생성자로 대신한다.
///
/// 이 스파이크가 초록이라는 것은 버튼 순서·구분선·플라이아웃 연결(스트립 "레이아웃 스펙")을 창 없이 고정할 수 있다는 뜻이다 —
/// 그 스펙은 51단계에서 <see cref="ToolbarLayout"/> 순수 데이터로 뺐다. 이 파일의 STA 사실들은 이제 "실현이 스펙을 따른다"의
/// 교차 증인이고(같은 순서 배열을 <c>ToolbarLayoutTests.Menu_Sequence_MatchesSnapshot</c>(MTA)이 든다), 종류→Popup 연결은
/// <see cref="Build_FlyoutBearingEntries_AreThePlacementTargetsOfTheirFlyouts"/>가 잠근다.
/// </summary>
public class ToolbarStripBuilderTests
{
    private sealed class FakeShellActions : IShellActions
    {
        public List<string> Calls { get; } = [];

        public double FadingSeconds { get; private set; } = 1.0;

        public void Undo() => Calls.Add("undo");

        public void ClearAll() => Calls.Add("clear-all");

        public void StartCapture() => Calls.Add("capture");

        public void OpenSettings() => Calls.Add("settings");

        public string? HotkeyLabel(string hotkeyId) => null;

        public void SetFadingDuration(double seconds)
        {
            FadingSeconds = seconds;
            Calls.Add($"fading:{seconds}");
        }

        public void ShowStatusReadout() => Calls.Add("status");
    }

    private sealed record Strip(UIElement Host, ToolbarParts Parts, FakeShellActions Actions, AppState State, ToolbarFlyouts Flyouts);

    private static Strip BuildStrip()
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack; // pack:// 스킴 등록 — Application/Window 없는 STA에서 Icons.Regular가 살아난다
        var state = new AppState();
        var actions = new FakeShellActions();
        var flyouts = new ToolbarFlyouts(state, actions, () => false);
        var (host, _, parts) = ToolbarStripBuilder.Build(
            state, actions, flyouts,
            onToggleMenuCollapsed: () => { },
            onRotateShapes: () => { },
            onRotatePenGroup: () => { },
            onSelectTool: _ => { },
            onToggleFading: () => { },
            onRotateBoard: () => { });
        return new Strip(host, parts, actions, state, flyouts);
    }

    /// <summary>host(Grid) → outer(StackPanel) → strip(Border) → stack2(StackPanel) → [눈 버튼, 메뉴 패널].</summary>
    private static StackPanel MenuPanel(UIElement host)
    {
        var outer = Assert.IsType<StackPanel>(((Grid)host).Children[0]);
        var strip = Assert.IsType<Border>(outer.Children[1]);
        var stack = Assert.IsType<StackPanel>(strip.Child);
        return Assert.IsType<StackPanel>(stack.Children[1]);
    }

    [Fact]
    public void Build_ButtonsCoverEveryToolbarButtonIdExceptPreview() => RunSta(() =>
    {
        var strip = BuildStrip();

        var expected = Enum.GetValues<ToolbarButtonId>().Where(id => id != ToolbarButtonId.Preview).ToHashSet();
        Assert.Equal(expected, strip.Parts.Buttons.Keys.ToHashSet());
    });

    /// <summary>스트립 레이아웃 스펙의 스냅샷 — 그룹 1 클릭 통과 / 그룹 2 도구 + 미리보기 / 그룹 3 편집 / 그룹 4 보드·캡처·설정 / 그룹 5 퀵컬러.</summary>
    [Fact]
    public void Build_MenuPanelChildSequence_MatchesSnapshot() => RunSta(() =>
    {
        var strip = BuildStrip();
        var byRoot = strip.Parts.Buttons.ToDictionary(kv => (UIElement)kv.Value.Root, kv => kv.Key.ToString());

        string Classify(UIElement child) => child switch
        {
            Border root when byRoot.TryGetValue(root, out var id) => id,
            Border { Child: Grid grid } when grid.Children.OfType<Ellipse>().Any() => "Preview",
            StackPanel => "QuickColors",
            _ => "---",
        };
        var sequence = MenuPanel(strip.Host).Children.Cast<UIElement>().Select(Classify).ToArray();

        Assert.Equal(
            [
                "ClickThrough", "---",
                "Select", "Shapes", "Pen", "Eraser", "Fading", "Preview", "---",
                "Undo", "---", "ClearAll", "---",
                "Board", "Capture", "Settings", "---",
                "QuickColors",
            ],
            sequence);
    });

    [Fact]
    public void Build_VisibilityButton_SitsAboveTheCollapsibleMenu() => RunSta(() =>
    {
        var strip = BuildStrip();
        var outer = Assert.IsType<StackPanel>(((Grid)strip.Host).Children[0]);
        var stack = Assert.IsType<StackPanel>(Assert.IsType<Border>(outer.Children[1]).Child);

        Assert.Same(strip.Parts.Buttons[ToolbarButtonId.Visibility].Root, stack.Children[0]);
        strip.Parts.SetMenuCollapsed(true);
        Assert.Equal(Visibility.Collapsed, MenuPanel(strip.Host).Visibility);
        Assert.Equal(Visibility.Visible, stack.Children[0].Visibility); // 눈 버튼은 접혀도 남는다
    });

    [Fact]
    public void Build_QuickSwatches_CoverEverySlotInOrder() => RunSta(() =>
    {
        var strip = BuildStrip();

        Assert.Equal(Enumerable.Range(0, AppState.QuickColorCount), strip.Parts.QuickSwatches.Select(s => s.Slot));
    });

    [Fact]
    public void Build_UndoAndClearAllButtons_DispatchToShellActions() => RunSta(() =>
    {
        var strip = BuildStrip();

        Click(strip.Parts.Buttons[ToolbarButtonId.Undo].Root);
        Click(strip.Parts.Buttons[ToolbarButtonId.ClearAll].Root);

        Assert.Equal(["undo", "clear-all"], strip.Actions.Calls);
    });

    [Fact]
    public void Build_ClickThroughButton_TogglesState() => RunSta(() =>
    {
        var strip = BuildStrip();
        Assert.False(strip.State.ClickThrough);

        Click(strip.Parts.Buttons[ToolbarButtonId.ClickThrough].Root);

        Assert.True(strip.State.ClickThrough);
    });

    /// <summary>
    /// 51단계: 종류→Popup 연결(ToolbarStripBuilder.Build의 PopupFor)의 유일한 증인. 스펙의 플라이아웃 버튼마다 그 종류의 Popup이
    /// 버튼을 PlacementTarget으로 갖고, 미리보기 항목의 실현 요소는 굵기 Popup의 PlacementTarget이다 — 한 팔이 뒤바뀌면 여기서 빨간불.
    /// </summary>
    [Fact]
    public void Build_FlyoutBearingEntries_AreThePlacementTargetsOfTheirFlyouts() => RunSta(() =>
    {
        var strip = BuildStrip();

        Popup PopupOf(ToolbarFlyoutKind kind) => kind switch
        {
            ToolbarFlyoutKind.Shapes => strip.Flyouts.ShapesFlyout,
            ToolbarFlyoutKind.Pen => strip.Flyouts.PenFlyout,
            ToolbarFlyoutKind.Fading => strip.Flyouts.FadingFlyout,
            ToolbarFlyoutKind.Board => strip.Flyouts.BoardFlyout,
            _ => throw new Xunit.Sdk.XunitException($"새 플라이아웃 종류 {kind}를 이 증인에 적으세요."),
        };

        var flyoutButtons = ToolbarLayout.Menu.OfType<ToolbarButtonEntry>().Where(b => b.Flyout is not null).ToList();
        Assert.Equal(4, flyoutButtons.Count);
        Assert.All(flyoutButtons, b => Assert.Same(strip.Parts.Buttons[b.Id].Root, PopupOf(b.Flyout!.Value).PlacementTarget));

        int previewIndex = ToolbarLayout.Menu.ToList().FindIndex(e => e is ToolbarPreviewEntry);
        Assert.Same(MenuPanel(strip.Host).Children[previewIndex], strip.Flyouts.ThicknessFlyout.PlacementTarget);
    });

    /// <summary>
    /// 클릭은 이제 누름 + 뗌 한 쌍이다 (PressStateRules): 버튼에서 누르고 버튼에서 떼야 발화한다.
    /// 뗌 하나만으로 발화하던 예전 계약이 되살아나면 아래 두 증인이 빨간불이 된다.
    /// </summary>
    private static void Click(UIElement element)
    {
        Press(element);
        Release(element);
    }

    private static void Press(UIElement element) =>
        element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent,
        });

    private static void Release(UIElement element) =>
        element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent,
        });

    /// <summary>밖에서 시작한 클릭은 남의 것이다 — 뗌만으로는 아무 일도 일어나지 않는다.</summary>
    [Fact]
    public void Click_ReleaseWithoutPress_DoesNotFire() => RunSta(() =>
    {
        var strip = BuildStrip();

        Release(strip.Parts.Buttons[ToolbarButtonId.ClearAll].Root);

        Assert.Empty(strip.Actions.Calls);
    });

    /// <summary>눌렀다가 버튼 밖으로 끌면 취소다 — 되돌리기 힘든 버튼에서 특히 중요하다.</summary>
    [Fact]
    public void Click_PressedThenDraggedAway_DoesNotFire() => RunSta(() =>
    {
        var strip = BuildStrip();
        var button = strip.Parts.Buttons[ToolbarButtonId.ClearAll].Root;

        Press(button);
        button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });
        Release(button);

        Assert.Empty(strip.Actions.Calls);
    });
}
