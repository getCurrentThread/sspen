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
    private sealed record Strip(UIElement Host, ToolbarParts Parts, FakeShellActions Actions, AppState State, ToolbarFlyouts Flyouts);

    /// <summary>
    /// 창 콜백 여섯 개는 무동작 람다가 아니라 <see cref="FakeShellActions.Calls"/>에 기록한다 (59단계, A5-1) — 그래야 ActionFor의
    /// 뒤바뀐 팔(Select→지우개, 도형↔펜 교차 등)이 <see cref="Build_EveryButtonClick_DispatchesItsAction"/>에서 빨간불이 된다.
    /// </summary>
    private static Strip BuildStrip()
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack; // pack:// 스킴 등록 — Application/Window 없는 STA에서 Icons.Regular가 살아난다
        var state = new AppState();
        var actions = new FakeShellActions();
        var flyouts = new ToolbarFlyouts(state, actions, () => false);
        var (host, _, parts) = ToolbarStripBuilder.Build(
            state, actions, flyouts,
            onToggleMenuCollapsed: () => actions.Calls.Add("toggle-menu"),
            onRotateShapes: () => actions.Calls.Add("rotate-shapes"),
            onRotatePenGroup: () => actions.Calls.Add("rotate-pen"),
            onSelectTool: tool => actions.Calls.Add($"select:{tool}"),
            onToggleFading: () => actions.Calls.Add("toggle-fading"),
            onRotateBoard: () => actions.Calls.Add("rotate-board"));
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
    public void Build_ButtonsCoverEveryToolbarButtonId() => RunSta(() =>
    {
        var strip = BuildStrip();

        Assert.Equal(Enum.GetValues<ToolbarButtonId>().ToHashSet(), strip.Parts.Buttons.Keys.ToHashSet());
    });

    public static IEnumerable<object[]> AllButtonIds() => Enum.GetValues<ToolbarButtonId>().Select(id => new object[] { id });

    /// <summary>
    /// 59단계(A5-1): ActionFor 스위치의 모든 팔을 잠근다. 빠진 팔은 Build가 던지지만(X7/R9) 뒤바뀐 팔은 그 트립와이어를 지나간다 —
    /// 여기서는 버튼마다 클릭 한 번의 결과 전부(기록된 호출, 클릭 통과 상태, 설정 메뉴 열림 요청)를 본다. 그래서 "다른 버튼의 동작을
    /// 하나 더 한다"도 빨간불이다. 새 id가 생기면 기대값 스위치가 던진다 — 기대값을 여기 적어야 초록이 된다.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllButtonIds))]
    public void Build_EveryButtonClick_DispatchesItsAction(ToolbarButtonId id) => RunSta(() =>
    {
        var strip = BuildStrip();
        string[] expectedCalls = id switch
        {
            ToolbarButtonId.Visibility => ["toggle-menu"],
            ToolbarButtonId.ClickThrough => [], // 창 콜백이 아니라 상태를 직접 뒤집는다 — 아래 ClickThrough 단언이 본다
            ToolbarButtonId.Select => ["select:Select"],
            ToolbarButtonId.Shapes => ["rotate-shapes"],
            ToolbarButtonId.Pen => ["rotate-pen"],
            ToolbarButtonId.Eraser => ["select:Eraser"],
            ToolbarButtonId.Fading => ["toggle-fading"],
            ToolbarButtonId.Undo => ["undo"],
            ToolbarButtonId.ClearAll => ["clear-all"],
            ToolbarButtonId.Board => ["rotate-board"],
            ToolbarButtonId.Capture => ["capture"],
            ToolbarButtonId.Settings => [], // 설정 창이 아니라 메뉴를 연다 (55단계) — 아래 SettingsFlyout 단언이 본다
            _ => throw new Xunit.Sdk.XunitException($"새 버튼 {id}의 클릭 기대값을 이 증인에 적으세요."),
        };

        Click(strip.Parts.Buttons[id].Root);

        Assert.Equal(expectedCalls, strip.Actions.Calls);
        Assert.Equal(id == ToolbarButtonId.ClickThrough, strip.State.ClickThrough);
        Assert.Equal(id == ToolbarButtonId.Settings, IsOpenRequested(strip.Flyouts.SettingsFlyout));
    });

    /// <summary>논리 트리를 깊이 우선으로 모두 돈다 — host Grid의 Popup 자식은 Child를 논리 자식으로 가지므로 플라이아웃 내용도 포함된다.</summary>
    private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            foreach (var descendant in LogicalDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// 59단계(A5-4): "미등록 툴팁은 표현할 수 없다"를 Attach 단위가 아니라 조립 결과 전체로 잠근다. 스트립과 플라이아웃 트리의
    /// 모든 툴팁은 (a) 문자열이 아닌 ToolTip 인스턴스이고, (b) 그 집합이 <see cref="ToolbarFlyouts.RegisteredTooltips"/>와 참조로 같으며,
    /// (c) 레지스트리에 두 번 든 것이 없다. 로고·팔레트처럼 Attach를 거치지 않는 툴팁의 등록이 빠지거나 창 쪽에 이중 등록이
    /// 되살아나면 빨간불이다 — 둘 다 툴바가 숨을 때 닫히지 않는 툴팁(AGENTS L89)으로 이어진다.
    /// </summary>
    [Fact]
    public void Build_EveryToolTipInStripAndFlyouts_IsRegisteredExactlyOnce() => RunSta(() =>
    {
        var strip = BuildStrip();

        var values = LogicalDescendants(strip.Host).OfType<FrameworkElement>()
            .Select(e => e.ToolTip).Where(t => t is not null).ToList();
        var registered = strip.Flyouts.RegisteredTooltips;

        Assert.All(values, v => Assert.IsType<ToolTip>(v));
        var found = values.Cast<ToolTip>().ToHashSet(ReferenceEqualityComparer.Instance);
        Assert.True(found.SetEquals(registered), $"트리 {found.Count}개 / 레지스트리 {registered.Count}개가 같은 집합이 아니다");
        Assert.Equal(registered.Count, registered.Distinct(ReferenceEqualityComparer.Instance).Count());
        // 실측(59단계): 스트립 21(버튼 12·미리보기 1·퀵컬러 6·팔레트 1·로고 1) + 플라이아웃 13(도형 5·펜 3·보드 2·설정 메뉴 3).
        Assert.Equal(34, registered.Count);
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
            ToolbarFlyoutKind.Settings => strip.Flyouts.SettingsFlyout,
            _ => throw new Xunit.Sdk.XunitException($"새 플라이아웃 종류 {kind}를 이 증인에 적으세요."),
        };

        var flyoutButtons = ToolbarLayout.Menu.OfType<ToolbarButtonEntry>().Where(b => b.Flyout is not null).ToList();
        Assert.Equal(5, flyoutButtons.Count);
        Assert.All(flyoutButtons, b => Assert.Same(strip.Parts.Buttons[b.Id].Root, PopupOf(b.Flyout!.Value).PlacementTarget));

        Assert.Same(PreviewButton(strip), strip.Flyouts.ThicknessFlyout.PlacementTarget);
    });

    /// <summary>
    /// 팝업이 "열림을 요청받았는가". 헤드리스 STA에는 <c>Window</c>가 없어 <c>Popup.IsLoaded</c>가 거짓이고, WPF는 그동안
    /// <c>IsOpen</c>을 거짓으로 강제한다(도형·보드 등 기존 플라이아웃도 같다) — 그래서 강제 전의 로컬 값을 읽는다.
    /// 코드가 쓴 값이지 화면 상태가 아니다. 그리고 이 때문에 <c>IsOpen</c>을 읽는 <c>ToggleFlyout</c>의 닫기 방향은 여기서 볼 수 없다.
    /// </summary>
    private static bool IsOpenRequested(Popup popup) => popup.ReadLocalValue(Popup.IsOpenProperty) is true;

    /// <summary>플라이아웃 카드 — FlyoutBorder가 만든 Grid의 첫 자식 Border.</summary>
    private static Border FlyoutCard(Popup flyout) =>
        Assert.IsType<Border>(Assert.IsType<Grid>(flyout.Child).Children[0]);

    /// <summary>StackPanel 카드 안의 항목들 — FlyoutBorder(Grid → 카드 Border) → StackPanel(가로 타일 또는 세로 메뉴).</summary>
    private static List<Border> FlyoutItems(Popup flyout) =>
        Assert.IsType<StackPanel>(FlyoutCard(flyout).Child).Children.Cast<Border>().ToList();

    /// <summary>설정 메뉴 카드 안의 행들 — FlyoutBorder(Grid → 카드 Border) → 세로 StackPanel.</summary>
    private static List<Border> SettingsMenuRows(Strip strip) => FlyoutItems(strip.Flyouts.SettingsFlyout);

    private static string RowLabel(Border row) =>
        Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(row.Child).Children[1]).Text;

    /// <summary>55단계: 설정 버튼은 창을 바로 열지 않고 메뉴를 연다 — 설정 창은 메뉴의 첫 항목이 연다.</summary>
    [Fact]
    public void Build_SettingsButtonClick_OpensTheMenuWithoutOpeningSettingsDirectly() => RunSta(() =>
    {
        var strip = BuildStrip();

        Click(strip.Parts.Buttons[ToolbarButtonId.Settings].Root);

        Assert.True(IsOpenRequested(strip.Flyouts.SettingsFlyout));
        Assert.Empty(strip.Actions.Calls);
    });

    /// <summary>
    /// 55단계: 호버로 열리는 플라이아웃(도형·펜·보드)과 달리 설정 메뉴는 클릭으로만 열린다 —
    /// 프로그램 종료를 품은 메뉴가 포인터가 스치기만 해도 뜨면 안 된다.
    /// </summary>
    [Fact]
    public void Build_SettingsButtonHover_DoesNotOpenTheMenu_ButBoardHoverStillOpensItsFlyout() => RunSta(() =>
    {
        var strip = BuildStrip();
        void Hover(ToolbarButtonId id) =>
            strip.Parts.Buttons[id].Root.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });

        Hover(ToolbarButtonId.Settings);
        Assert.False(IsOpenRequested(strip.Flyouts.SettingsFlyout));

        Hover(ToolbarButtonId.Board); // 대조군: 호버 전개는 그대로다
        Assert.True(IsOpenRequested(strip.Flyouts.BoardFlyout));
    });

    /// <summary>열린 설정 메뉴 위로 포인터가 되돌아와도 닫히지 않고, 다른 플라이아웃 버튼으로 옮기면 닫힌다.</summary>
    [Fact]
    public void Build_SettingsMenuOpen_SurvivesItsOwnButtonHover_ButClosesWhenAnotherFlyoutOpens() => RunSta(() =>
    {
        var strip = BuildStrip();
        void Hover(ToolbarButtonId id) =>
            strip.Parts.Buttons[id].Root.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });

        Click(strip.Parts.Buttons[ToolbarButtonId.Settings].Root);
        Hover(ToolbarButtonId.Settings);
        Assert.True(IsOpenRequested(strip.Flyouts.SettingsFlyout));

        Hover(ToolbarButtonId.Board);
        Assert.False(IsOpenRequested(strip.Flyouts.SettingsFlyout));
        Assert.True(IsOpenRequested(strip.Flyouts.BoardFlyout));
    });

    [Fact]
    public void Build_SettingsMenu_HasSettingsHideToolbarExit_InThatOrder() => RunSta(() =>
    {
        var strip = BuildStrip();

        Assert.Equal(
            [Strings.Settings, Strings.MenuHideToolbar, Strings.SettingsExitApp],
            SettingsMenuRows(strip).Select(RowLabel));
    });

    /// <summary>행 발화는 누름 + 뗌 한 쌍이다 (84단계, A5-5) — 같은 행에서 누르고 떼야 동작하고, 발화하면 메뉴가 닫힌다.</summary>
    [Fact]
    public void Build_SettingsMenuRows_DispatchToShellActions_AndCloseTheMenu() => RunSta(() =>
    {
        var strip = BuildStrip();
        var rows = SettingsMenuRows(strip);

        foreach (var row in rows)
        {
            Click(strip.Parts.Buttons[ToolbarButtonId.Settings].Root);
            Assert.True(IsOpenRequested(strip.Flyouts.SettingsFlyout));

            Click(row);

            Assert.False(IsOpenRequested(strip.Flyouts.SettingsFlyout));
        }

        Assert.Equal(["settings", "hide-toolbar", "request-exit"], strip.Actions.Calls);
    });

    // ── 설정 메뉴 행의 눌림 래치 (84단계, A5-5) ──────────────────────────────────────────────────────────────
    // 예전 행은 뗌 하나만으로 발화했다. 세로 메뉴에서 '도구 막대 닫기'를 누른 채 '프로그램 종료'로 미끄러져 떼면 누른 적 없는
    // 종료 행이 발화했고, 종료는 확인 없이 판서 전체를 잃는다. 스트립 버튼과 같은 규칙(PressStateRules.ShouldFire)을 행에도 건다.

    private static void Leave(UIElement element) =>
        element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent });

    /// <summary>밖에서 시작한 뗌은 남의 것이다 — 종료 행에 뗌만 오면 아무 일도 없고 메뉴도 열린 채다.</summary>
    [Fact]
    public void Build_SettingsMenuRow_ReleaseWithoutPress_DoesNotFire() => RunSta(() =>
    {
        var strip = BuildStrip();
        Click(strip.Parts.Buttons[ToolbarButtonId.Settings].Root);
        var exitRow = SettingsMenuRows(strip)[^1];

        Release(exitRow);

        Assert.Empty(strip.Actions.Calls);
        Assert.True(IsOpenRequested(strip.Flyouts.SettingsFlyout));
    });

    /// <summary>행에서 눌렀다가 밖으로 나가면 취소다 — 캡처가 없어 돌아와 떼도 발화하지 않는다(안전한 쪽).</summary>
    [Fact]
    public void Build_SettingsMenuRow_PressedThenLeftThenReleased_DoesNotFire() => RunSta(() =>
    {
        var strip = BuildStrip();
        Click(strip.Parts.Buttons[ToolbarButtonId.Settings].Root);
        var hideRow = SettingsMenuRows(strip)[1];

        Press(hideRow);
        Leave(hideRow);
        Release(hideRow);

        Assert.Empty(strip.Actions.Calls);
        Assert.True(IsOpenRequested(strip.Flyouts.SettingsFlyout));
    });

    /// <summary>신고된 결함 모양: '도구 막대 닫기'에서 누르고 '프로그램 종료'로 미끄러져 떼면 어느 행도 발화하지 않는다.</summary>
    [Fact]
    public void Build_SettingsMenuRow_PressOnOneRowReleaseOnAnother_DoesNotFire() => RunSta(() =>
    {
        var strip = BuildStrip();
        Click(strip.Parts.Buttons[ToolbarButtonId.Settings].Root);
        var rows = SettingsMenuRows(strip);

        Press(rows[1]);
        Leave(rows[1]);
        Release(rows[2]);

        Assert.DoesNotContain("request-exit", strip.Actions.Calls);
        Assert.Empty(strip.Actions.Calls);
        Assert.True(IsOpenRequested(strip.Flyouts.SettingsFlyout));
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

    // ── 휠 배선 (59단계, A8-4) ─────────────────────────────────────────────────────────────────────────────
    // 예전 E2E 'Toolbar_ScrollWheel' 2건은 AppController와 실제 창을 띄우고도 순수 함수(NextToolByWheel·StepThickness·StepByWheel)만
    // 다시 불렀다 — ToolbarWheel 스위치 팔이 뒤바뀌거나 ShowStatusReadout 호출이 빠져도 초록이었다. 여기서는 실현된 요소에
    // 휠 이벤트를 직접 올려 Build가 단 핸들러를 구동한다. 휠은 화면 흔적이 거의 없는 변경이라 배선 증인이 특히 필요한 자리다.

    /// <summary>휠 한 칸을 요소에 올리고, 핸들러가 이벤트를 소비했는지(Handled)를 돌려준다.</summary>
    private static bool Wheel(UIElement element, int delta)
    {
        var e = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = UIElement.MouseWheelEvent };
        element.RaiseEvent(e);
        return e.Handled;
    }

    /// <summary>미리보기 항목의 실현 요소 — ToolbarLayout.Menu에서 미리보기 항목의 자리가 곧 메뉴 패널의 자식 번호다.</summary>
    private static UIElement PreviewButton(Strip strip)
    {
        int previewIndex = ToolbarLayout.Menu.ToList().FindIndex(e => e is ToolbarPreviewEntry);
        return MenuPanel(strip.Host).Children[previewIndex];
    }

    [Fact]
    public void Build_PenButtonWheel_CyclesPenGroup_AndReadsOutStatus() => RunSta(() =>
    {
        var strip = BuildStrip();
        strip.State.ActiveTool = ToolKind.Pen;

        bool handled = Wheel(strip.Parts.Buttons[ToolbarButtonId.Pen].Root, -120);

        Assert.Equal(ToolbarStateMap.NextInCycle(ToolbarStateMap.PenCycle, ToolKind.Pen, -120), strip.State.ActiveTool);
        Assert.Equal(ToolKind.Highlighter, strip.State.ActiveTool); // 위 기대값이 제자리가 아님을 못 박는다
        Assert.Equal(["status"], strip.Actions.Calls);
        Assert.True(handled);
    });

    [Fact]
    public void Build_ShapesButtonWheel_CyclesShapeGroup_AndReadsOutStatus() => RunSta(() =>
    {
        var strip = BuildStrip();
        strip.State.ActiveTool = ToolKind.Line;

        bool handled = Wheel(strip.Parts.Buttons[ToolbarButtonId.Shapes].Root, -120);

        Assert.Equal(ToolbarStateMap.NextInCycle(ToolbarStateMap.ShapeCycle, ToolKind.Line, -120), strip.State.ActiveTool);
        Assert.Equal(ToolKind.Arrow, strip.State.ActiveTool); // 펜 그룹 순환으로 뒤바뀌면 Line은 펜 순환에 없어 Pen이 된다
        Assert.Equal(["status"], strip.Actions.Calls);
        Assert.True(handled);
    });

    [Fact]
    public void Build_FadingButtonWheel_StepsDurationThroughShellActions() => RunSta(() =>
    {
        var strip = BuildStrip();
        double before = strip.Actions.FadingSeconds;

        bool handled = Wheel(strip.Parts.Buttons[ToolbarButtonId.Fading].Root, 120);

        double expected = FadingDurations.StepByWheel(before, 120);
        Assert.True(expected > before); // 위로 한 칸은 실제로 길어진다 — 제자리 기대값이면 증인이 비어 버린다
        Assert.Equal([$"fading:{expected}", "status"], strip.Actions.Calls);
        Assert.Equal(expected, strip.Actions.FadingSeconds);
        Assert.True(handled);
    });

    [Fact]
    public void Build_PreviewWheel_StepsThickness() => RunSta(() =>
    {
        var strip = BuildStrip();
        Assert.Equal(ThicknessStep.Medium, strip.State.Thickness);

        bool handled = Wheel(PreviewButton(strip), 120);

        Assert.Equal(ThicknessStep.Large, strip.State.Thickness);
        Assert.Empty(strip.Actions.Calls); // 굵기 휠은 상태 읽기를 띄우지 않는다 — 미리보기 원 크기가 곧 흔적이다
        Assert.True(handled);
    });

    /// <summary>휠은 퀵컬러 칸 위에서 굴러 모자이크 그리드(UniformGrid)의 핸들러까지 버블링한다 — 실제 포인터 경로 그대로다.</summary>
    [Fact]
    public void Build_QuickColorsWheel_AdvancesToNextSlot() => RunSta(() =>
    {
        var strip = BuildStrip();
        var quickColors = strip.State.QuickColors;
        strip.State.CurrentColor = quickColors[0];
        int next = ToolbarStateMap.NextQuickColorSlotByWheel(0, -120, quickColors.Count);
        Assert.NotEqual(quickColors[0], quickColors[next]);

        bool handled = Wheel(strip.Parts.QuickSwatches[0].Ring, -120);

        Assert.Equal(quickColors[next], strip.State.CurrentColor);
        Assert.Empty(strip.Actions.Calls);
        Assert.True(handled);
    });

    /// <summary>ToolbarWheel.None 팔: 휠이 없는 버튼은 상태도 호출도 건드리지 않고 이벤트를 소비하지도 않는다(창의 도구 순환 휠로 흘러간다).</summary>
    [Fact]
    public void Build_UndoButtonWheel_ChangesNothing() => RunSta(() =>
    {
        var strip = BuildStrip();
        strip.State.ActiveTool = ToolKind.Pen;
        var colorBefore = strip.State.CurrentColor;
        var thicknessBefore = strip.State.Thickness;

        bool handled = Wheel(strip.Parts.Buttons[ToolbarButtonId.Undo].Root, -120);

        Assert.Equal(ToolKind.Pen, strip.State.ActiveTool);
        Assert.Equal(colorBefore, strip.State.CurrentColor);
        Assert.Equal(thicknessBefore, strip.State.Thickness);
        Assert.Equal(1.0, strip.Actions.FadingSeconds);
        Assert.Empty(strip.Actions.Calls);
        Assert.False(handled);
    });

    // ── 휠·토글 배선 통합 (69단계, A5-6) ─────────────────────────────────────────────────────────────────────
    // 스트립과 플라이아웃에 두 벌씩 있던 굵기·페이딩 휠과 팔레트 토글이 ToolbarFlyouts 한 곳으로 모였다. 여기서는 두 진입점이
    // 같은 결과를 내는지(한쪽만 고쳐 갈라지면 빨간불), 그리고 버튼 쪽에만 있는 의도된 차이(상태 리드아웃)가 남았는지를 본다.

    /// <summary>굵기 플라이아웃의 현재 단계 강조 — 강조색으로 칠해진 점의 단계 목록.</summary>
    private static List<ThicknessStep> HighlightedThicknessSteps(Strip strip) =>
        FlyoutItems(strip.Flyouts.ThicknessFlyout)
            .Select((item, i) => (Dot: Assert.IsType<Ellipse>(Assert.IsType<Grid>(item.Child).Children[0]), Step: (ThicknessStep)i))
            .Where(x => ReferenceEquals(x.Dot.Fill, ToolbarTheme.AccentBrush))
            .Select(x => x.Step)
            .ToList();

    /// <summary>delta 0 행은 특성화다 — 두 경로 모두 가늘게 간다(ThicknessDirectionByWheel). 보존이지 승인이 아니다.</summary>
    [Theory]
    [InlineData(120, ThicknessStep.Large)]
    [InlineData(-120, ThicknessStep.Small)]
    [InlineData(0, ThicknessStep.Small)]
    public void Build_PreviewWheel_And_ThicknessFlyoutWheel_StepIdentically(int delta, ThicknessStep expected) => RunSta(() =>
    {
        var viaPreview = BuildStrip();
        var viaFlyout = BuildStrip();
        Assert.Equal(ThicknessStep.Medium, viaPreview.State.Thickness);
        Assert.Equal(ThicknessStep.Medium, viaFlyout.State.Thickness);

        bool previewHandled = Wheel(PreviewButton(viaPreview), delta);
        bool flyoutHandled = Wheel(viaFlyout.Flyouts.ThicknessFlyout.Child, delta);

        Assert.Equal(expected, viaPreview.State.Thickness);
        Assert.Equal(expected, viaFlyout.State.Thickness);
        Assert.Equal([expected], HighlightedThicknessSteps(viaPreview)); // 두 경로 모두 플라이아웃 강조를 갱신한다
        Assert.Equal([expected], HighlightedThicknessSteps(viaFlyout));
        Assert.True(previewHandled);
        Assert.True(flyoutHandled);
        Assert.Empty(viaPreview.Actions.Calls);
        Assert.Empty(viaFlyout.Actions.Calls);
    });

    /// <summary>
    /// 페이딩 휠 두 경로는 같은 사다리 이동이고, 상태 리드아웃은 버튼 쪽에만 있다 — 플라이아웃이 열려 있으면 강조가 곧 흔적이지만
    /// 버튼 휠은 플라이아웃이 아직 안 열렸을 수 있다(호버 지연). 플라이아웃 휠 쪽 강조 갱신도 함께 본다.
    /// </summary>
    [Fact]
    public void Build_FadingButtonWheel_ShowsReadout_FlyoutWheelDoesNot() => RunSta(() =>
    {
        var strip = BuildStrip();
        double start = strip.Actions.FadingSeconds;
        double afterFlyout = FadingDurations.StepByWheel(start, 120);
        double afterButton = FadingDurations.StepByWheel(afterFlyout, 120);
        Assert.True(start < afterFlyout && afterFlyout < afterButton); // 두 칸 모두 실제로 움직인다 — 제자리 기대값이면 증인이 비어 버린다

        bool flyoutHandled = Wheel(strip.Flyouts.FadingFlyout.Child, 120);

        Assert.Equal([$"fading:{afterFlyout}"], strip.Actions.Calls);
        Assert.True(flyoutHandled);
        var selected = FlyoutItems(strip.Flyouts.FadingFlyout)
            .Select(item => Assert.IsType<TextBlock>(item.Child))
            .Where(label => ReferenceEquals(label.Foreground, ToolbarTheme.AccentBrush))
            .Select(label => label.Text);
        Assert.Equal([Strings.FadingDuration(afterFlyout)], selected);

        strip.Actions.Calls.Clear();
        bool buttonHandled = Wheel(strip.Parts.Buttons[ToolbarButtonId.Fading].Root, 120);

        Assert.Equal([$"fading:{afterButton}", "status"], strip.Actions.Calls);
        Assert.True(buttonHandled);
    });

    /// <summary>퀵컬러 항목의 실현 요소 — [모자이크 UniformGrid, 현재 색 대형 스와치].</summary>
    private static StackPanel QuickColorsPanel(Strip strip)
    {
        int index = ToolbarLayout.Menu.ToList().FindIndex(e => e is ToolbarQuickColorsEntry);
        return Assert.IsType<StackPanel>(MenuPanel(strip.Host).Children[index]);
    }

    /// <summary>현재 색 스와치 뗌은 ToggleFlyout(PaletteFlyout)으로 팔레트를 연다 — 인라인 사본을 걷어낸 뒤에도 같은 팝업을 연다.</summary>
    [Fact]
    public void Build_CurrentColorSwatchRelease_RequestsPaletteOpen() => RunSta(() =>
    {
        var strip = BuildStrip();
        var swatch = Assert.IsType<Border>(QuickColorsPanel(strip).Children[1]);
        Assert.Same(strip.Flyouts.PaletteFlyout.PlacementTarget, swatch);

        Release(swatch);

        Assert.True(IsOpenRequested(strip.Flyouts.PaletteFlyout));
        Assert.All(strip.Flyouts.AllFlyouts.Where(p => !ReferenceEquals(p, strip.Flyouts.PaletteFlyout)),
            p => Assert.False(IsOpenRequested(p)));
        Assert.Empty(strip.Actions.Calls);
    });

    /// <summary>굵기 미리보기 뗌은 ToggleFlyout(ThicknessFlyout)으로 굵기 플라이아웃을 연다 (전용 래퍼 ToggleThicknessFlyout 제거 뒤에도 같다).</summary>
    [Fact]
    public void Build_PreviewRelease_RequestsThicknessOpen() => RunSta(() =>
    {
        var strip = BuildStrip();

        Release(PreviewButton(strip));

        Assert.True(IsOpenRequested(strip.Flyouts.ThicknessFlyout));
        Assert.False(IsOpenRequested(strip.Flyouts.PaletteFlyout));
        Assert.Empty(strip.Actions.Calls);
    });

    // ── ShellMetrics 토큰 (69단계, A5-7) ─────────────────────────────────────────────────────────────────────
    // 값이 같은 리터럴만 토큰으로 바꿨다 — 토큰 값 자체는 ShellMetricsTests.Tokens_KeepTheirLegacyPixelValues가 옛 리터럴에 묶는다.

    [Fact]
    public void Build_PreviewButton_IsButtonSizeSquare() => RunSta(() =>
    {
        var strip = BuildStrip();
        var preview = Assert.IsType<Border>(PreviewButton(strip));

        Assert.Equal(ShellMetrics.ButtonSize, preview.Width);
        Assert.Equal(ShellMetrics.ButtonSize, preview.Height);
        Assert.Equal(strip.Parts.Buttons[ToolbarButtonId.Select].Root.Width, preview.Width); // 스트립 버튼과 같은 열
    });

    /// <summary>플라이아웃 카드 일곱 장 모두 스트립과 같은 모서리 반경이다 — CardRadius를 바꾸면 둘이 함께 바뀐다.</summary>
    [Fact]
    public void Build_FlyoutCards_UseCardRadius() => RunSta(() =>
    {
        var strip = BuildStrip();
        var outer = Assert.IsType<StackPanel>(((Grid)strip.Host).Children[0]);
        var stripBorder = Assert.IsType<Border>(outer.Children[1]);

        Assert.Equal(7, strip.Flyouts.AllFlyouts.Length);
        Assert.All(strip.Flyouts.AllFlyouts, popup =>
            Assert.Equal(new CornerRadius(ShellMetrics.CardRadius), FlyoutCard(popup).CornerRadius));
        Assert.Equal(new CornerRadius(ShellMetrics.CardRadius), stripBorder.CornerRadius);
    });

    [Fact]
    public void Build_ToolFlyoutGlyph_IsFlyoutGlyphSize() => RunSta(() =>
    {
        var strip = BuildStrip();

        foreach (var flyout in new[] { strip.Flyouts.ShapesFlyout, strip.Flyouts.PenFlyout })
        {
            Assert.All(FlyoutItems(flyout), item =>
            {
                var glyph = Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(item.Child).Children[0]);
                Assert.Equal(ShellMetrics.FlyoutGlyphSize, glyph.FontSize);
            });
        }
    });

    /// <summary>플라이아웃 라벨은 보조 크기, 설정 메뉴 행은 본문 크기 라벨 + 메뉴 글리프다 (타입 스케일 토큰).</summary>
    [Fact]
    public void Build_FlyoutText_UsesTypeScaleTokens() => RunSta(() =>
    {
        var strip = BuildStrip();

        // 도형·펜 타일: [글리프, 라벨]
        foreach (var flyout in new[] { strip.Flyouts.ShapesFlyout, strip.Flyouts.PenFlyout })
        {
            Assert.All(FlyoutItems(flyout), item =>
                Assert.Equal(ShellMetrics.FontCaption, Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(item.Child).Children[1]).FontSize));
        }
        // 페이딩: 라벨 하나
        Assert.All(FlyoutItems(strip.Flyouts.FadingFlyout), item =>
            Assert.Equal(ShellMetrics.FontCaption, Assert.IsType<TextBlock>(item.Child).FontSize));
        // 보드: [스와치, 라벨]
        Assert.All(FlyoutItems(strip.Flyouts.BoardFlyout), item =>
            Assert.Equal(ShellMetrics.FontCaption, Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(item.Child).Children[1]).FontSize));
        // 설정 메뉴 행: [글리프, 라벨]
        Assert.All(SettingsMenuRows(strip), row =>
        {
            var children = Assert.IsType<StackPanel>(row.Child).Children;
            Assert.Equal(ShellMetrics.MenuGlyphSize, Assert.IsType<TextBlock>(children[0]).FontSize);
            Assert.Equal(ShellMetrics.FontBody, Assert.IsType<TextBlock>(children[1]).FontSize);
        });
    });

    // ── 도구 그룹 플라이아웃 파생 (69단계, A5-2) ─────────────────────────────────────────────────────────────

    /// <summary>타일 항목의 [글리프 텍스트, 라벨 텍스트].</summary>
    private static (string Glyph, string Label) TileTexts(Border item)
    {
        var children = Assert.IsType<StackPanel>(item.Child).Children;
        return (Assert.IsType<TextBlock>(children[0]).Text, Assert.IsType<TextBlock>(children[^1]).Text);
    }

    /// <summary>
    /// 도형·펜 플라이아웃 항목은 그룹 순환 순서 그대로이고, 라벨은 상태 리드아웃과 같은 이름, 글리프는 그룹 버튼과 같은 표다.
    /// 순환에 도구를 더하고 플라이아웃을 빠뜨리는 결함 모양은 이제 표현할 수 없다 — 이 증인은 그 파생이 유지되는지를 본다.
    /// </summary>
    [Fact]
    public void Build_ShapesAndPenFlyouts_ItemsFollowCycleOrder() => RunSta(() =>
    {
        var strip = BuildStrip();

        foreach (var (flyout, cycle) in new[] { (strip.Flyouts.ShapesFlyout, ToolbarStateMap.ShapeCycle), (strip.Flyouts.PenFlyout, ToolbarStateMap.PenCycle) })
        {
            var texts = FlyoutItems(flyout).Select(TileTexts).ToList();

            Assert.Equal(cycle.Select(StatusReadout.ToolName), texts.Select(t => t.Label));
            Assert.Equal(cycle.Select(tool => ToolbarStateMap.ToolIcon(tool)!.Value.Regular), texts.Select(t => t.Glyph));
        }
        // 파생 전 하드코딩 순서의 특성화 (라벨 문자열 그대로).
        Assert.Equal(
            [Strings.ShapeLine, Strings.ShapeArrow, Strings.ShapeRectangle, Strings.ShapeEllipse, Strings.ShapeTable],
            FlyoutItems(strip.Flyouts.ShapesFlyout).Select(item => TileTexts(item).Label));
        Assert.Equal(
            [Strings.Pen, Strings.Highlighter, Strings.ShapeText],
            FlyoutItems(strip.Flyouts.PenFlyout).Select(item => TileTexts(item).Label));
    });

    /// <summary>플라이아웃 항목도 ToggleTool을 거친다 — 같은 항목을 다시 고르면 도구가 풀린다. 고를 때마다 플라이아웃이 닫힌다.</summary>
    [Fact]
    public void Build_ToolFlyoutItem_AppliesToggleTool() => RunSta(() =>
    {
        var strip = BuildStrip();
        var first = FlyoutItems(strip.Flyouts.ShapesFlyout)[0];
        strip.Parts.Buttons[ToolbarButtonId.Shapes].Root.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
        Assert.True(IsOpenRequested(strip.Flyouts.ShapesFlyout));

        Release(first);

        Assert.Equal(ToolKind.Line, strip.State.ActiveTool);
        Assert.False(IsOpenRequested(strip.Flyouts.ShapesFlyout));

        Release(first);

        Assert.Equal(ToolKind.None, strip.State.ActiveTool);
    });

    /// <summary>
    /// 표에는 도구 핫키가 없다(ShellHotkeys.ToolHotkeyIds) — 툴팁은 제목 한 줄뿐이다. 예전에는 해석되지 않는 id "table"을 넘겨
    /// 늘 숨겨지는 빈 둘째 줄을 만들었다. 대조군: 핫키가 있는 도구 항목은 두 줄이다.
    /// </summary>
    [Fact]
    public void Build_TableFlyoutItem_TooltipHasTitleOnly() => RunSta(() =>
    {
        var strip = BuildStrip();
        int TooltipLines(Border item) => Assert.IsType<StackPanel>(Assert.IsType<ToolTip>(item.ToolTip).Content).Children.Count;
        var shapeItems = FlyoutItems(strip.Flyouts.ShapesFlyout);
        int tableIndex = Array.IndexOf(ToolbarStateMap.ShapeCycle, ToolKind.Table);

        Assert.Equal(1, TooltipLines(shapeItems[tableIndex]));
        foreach (var (flyout, cycle) in new[] { (strip.Flyouts.ShapesFlyout, ToolbarStateMap.ShapeCycle), (strip.Flyouts.PenFlyout, ToolbarStateMap.PenCycle) })
        {
            var items = FlyoutItems(flyout);
            for (int i = 0; i < cycle.Length; i++)
            {
                Assert.Equal(ShellHotkeys.ToolHotkeyIds.ContainsKey(cycle[i]) ? 2 : 1, TooltipLines(items[i]));
            }
        }
    });

    /// <summary>레지스트리 보기는 읽기 전용 래퍼다 — 내부 List로 되캐스팅해 등록을 지우거나 끼워 넣을 수 없다 (59단계 리뷰 후속).</summary>
    [Fact]
    public void Build_RegisteredTooltips_IsReadOnlyView() => RunSta(() =>
    {
        var strip = BuildStrip();
        var registered = strip.Flyouts.RegisteredTooltips;

        Assert.IsNotType<List<ToolTip>>(registered);
        var asList = Assert.IsAssignableFrom<IList<ToolTip>>(registered);
        Assert.True(asList.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => asList.Add(new ToolTip()));
        Assert.Throws<NotSupportedException>(() => asList.RemoveAt(0));
    });
}
