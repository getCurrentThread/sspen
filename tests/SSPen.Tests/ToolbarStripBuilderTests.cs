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

    /// <summary>설정 메뉴 카드 안의 행들 — FlyoutBorder(Grid → 카드 Border) → 세로 StackPanel.</summary>
    private static List<Border> SettingsMenuRows(Strip strip)
    {
        var card = Assert.IsType<Border>(Assert.IsType<Grid>(strip.Flyouts.SettingsFlyout.Child).Children[0]);
        return Assert.IsType<StackPanel>(card.Child).Children.Cast<Border>().ToList();
    }

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

    [Fact]
    public void Build_SettingsMenuRows_DispatchToShellActions_AndCloseTheMenu() => RunSta(() =>
    {
        var strip = BuildStrip();
        var rows = SettingsMenuRows(strip);

        foreach (var row in rows)
        {
            Click(strip.Parts.Buttons[ToolbarButtonId.Settings].Root);
            Assert.True(IsOpenRequested(strip.Flyouts.SettingsFlyout));

            Release(row);

            Assert.False(IsOpenRequested(strip.Flyouts.SettingsFlyout));
        }

        Assert.Equal(["settings", "hide-toolbar", "request-exit"], strip.Actions.Calls);
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
}
