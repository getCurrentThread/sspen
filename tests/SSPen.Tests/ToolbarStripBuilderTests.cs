using System.Windows;
using System.Windows.Controls;
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
/// 그 스펙을 데이터로 빼는 <c>ToolbarLayout</c>은 후속 로드맵이다 (이 커밋은 프로덕션 무변경).
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
    }

    private sealed record Strip(UIElement Host, ToolbarParts Parts, FakeShellActions Actions, AppState State);

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
        return new Strip(host, parts, actions, state);
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
                "Undo", "ClearAll", "---",
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

    private static void Click(UIElement element) =>
        element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent,
        });
}
