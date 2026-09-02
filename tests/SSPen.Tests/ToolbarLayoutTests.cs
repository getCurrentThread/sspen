using System.Windows.Threading;
using SSPen.Annotation;
using SSPen.Settings;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="ToolbarLayout"/> 스펙의 증인 (51단계, ARCH-11, X7/R9). 스펙에 델리게이트·WPF 객체가 없어 xUnit 기본 MTA 스레드에서 돈다 —
/// <c>RunSta</c> 없음. 같은 순서 배열을 <c>ToolbarStripBuilderTests.Build_MenuPanelChildSequence_MatchesSnapshot</c>(STA, 시각 트리)이
/// 들고 있어 둘이 함께 "스펙 = 실현"을 잠근다. 핫키 라벨 증인은 <c>ShellHotkeyMapTests</c>와 같은 MTA 생성 경로
/// (<c>Dispatcher.CurrentDispatcher</c> + <c>new AppSettings()</c>)를 쓴다 — RunSta 불필요.
/// </summary>
public class ToolbarLayoutTests
{
    private static IEnumerable<ToolbarButtonEntry> AllButtons() =>
        new[] { ToolbarLayout.Visibility }.Concat(ToolbarLayout.Menu.OfType<ToolbarButtonEntry>());

    private static ToolbarButtonEntry Button(ToolbarButtonId id) => AllButtons().Single(b => b.Id == id);

    private static IEnumerable<string> AllHotkeyIds() =>
        AllButtons().Select(b => b.HotkeyId).OfType<string>()
            .Concat(ToolbarLayout.Menu.OfType<ToolbarPreviewEntry>().Select(p => p.HotkeyId));

    public static IEnumerable<object[]> AllButtonIds() =>
        Enum.GetValues<ToolbarButtonId>().Where(id => id != ToolbarButtonId.Preview).Select(id => new object[] { id });

    private static string Classify(ToolbarLayoutEntry? entry) => entry switch
    {
        ToolbarButtonEntry b => b.Id.ToString(),
        ToolbarPreviewEntry => "Preview",
        ToolbarQuickColorsEntry => "QuickColors",
        ToolbarSeparatorEntry => "---",
        null => throw new Xunit.Sdk.XunitException("Menu에 null 항목 — ToolbarLayout 정적 초기화 순서를 확인하세요."),
        _ => throw new Xunit.Sdk.XunitException($"새 항목 종류 {entry.GetType().Name}를 Classify에 적으세요."),
    };

    /// <summary>ToolbarStripBuilderTests.Build_MenuPanelChildSequence_MatchesSnapshot과 같은 배열 — 스트립이 바뀌면 둘을 함께 고친다.</summary>
    [Fact]
    public void Menu_Sequence_MatchesSnapshot()
    {
        Assert.Equal(
            [
                "ClickThrough", "---",
                "Select", "Shapes", "Pen", "Eraser", "Fading", "Preview", "---",
                "Undo", "ClearAll", "---",
                "Board", "Capture", "Settings", "---",
                "QuickColors",
            ],
            ToolbarLayout.Menu.Select(Classify).ToArray());
    }

    [Fact]
    public void Buttons_CoverEveryToolbarButtonIdExceptPreview_ExactlyOnce()
    {
        var ids = AllButtons().Select(b => b.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(Enum.GetValues<ToolbarButtonId>().Where(id => id != ToolbarButtonId.Preview).ToHashSet(), ids.ToHashSet());
        Assert.DoesNotContain(ToolbarLayout.Menu.OfType<ToolbarButtonEntry>(), b => b.Id == ToolbarButtonId.Visibility); // 눈 버튼은 메뉴 밖
    }

    [Fact]
    public void Menu_HasFourSeparators_NeverAdjacentOrAtEdges()
    {
        Assert.Equal(4, ToolbarLayout.Menu.OfType<ToolbarSeparatorEntry>().Count());
        Assert.IsNotType<ToolbarSeparatorEntry>(ToolbarLayout.Menu[0]);
        Assert.IsNotType<ToolbarSeparatorEntry>(ToolbarLayout.Menu[^1]);
        Assert.DoesNotContain(
            Enumerable.Range(0, ToolbarLayout.Menu.Count - 1),
            i => ToolbarLayout.Menu[i] is ToolbarSeparatorEntry && ToolbarLayout.Menu[i + 1] is ToolbarSeparatorEntry);
    }

    /// <summary>51단계 이전 ToolbarStripBuilder.Build의 MakeButton 호출 인자를 옮겨 적은 표 — 전사 오류가 있으면 여기서 빨간불.</summary>
    [Theory]
    [MemberData(nameof(AllButtonIds))]
    public void Button_Attributes_MatchTable(ToolbarButtonId id)
    {
        (string Tooltip, (string Regular, string Filled) Icon, ToolbarFlyoutKind? Flyout, ToolStyleGroup? Badge, string? HotkeyId, ToolbarWheel Wheel) expected = id switch
        {
            ToolbarButtonId.Visibility => (Strings.Visibility, Icons.Eye, null, null, null, ToolbarWheel.None),
            ToolbarButtonId.ClickThrough => (Strings.ClickThrough, Icons.Cursor, null, null, "clickthrough", ToolbarWheel.None),
            ToolbarButtonId.Select => (Strings.Select, Icons.Select, null, null, "select", ToolbarWheel.None),
            ToolbarButtonId.Shapes => (Strings.Shapes, Icons.Shapes, ToolbarFlyoutKind.Shapes, ToolStyleGroup.Shape, null, ToolbarWheel.ShapeCycle),
            ToolbarButtonId.Pen => (Strings.Pen, Icons.Pen, ToolbarFlyoutKind.Pen, ToolStyleGroup.Pen, "pen", ToolbarWheel.PenCycle),
            ToolbarButtonId.Eraser => (Strings.Eraser, Icons.Eraser, null, null, "eraser", ToolbarWheel.None),
            ToolbarButtonId.Fading => (Strings.HotkeyFadingInk, Icons.Timer, ToolbarFlyoutKind.Fading, null, "fading", ToolbarWheel.FadingDuration),
            ToolbarButtonId.Undo => (Strings.Undo, Icons.ArrowUndo, null, null, "undo", ToolbarWheel.None),
            ToolbarButtonId.ClearAll => (Strings.ClearAll, Icons.Delete, null, null, "clear", ToolbarWheel.None),
            ToolbarButtonId.Board => (Strings.Board, Icons.Whiteboard, ToolbarFlyoutKind.Board, null, "whiteboard", ToolbarWheel.None),
            ToolbarButtonId.Capture => (Strings.Capture, Icons.Camera, null, null, "capture", ToolbarWheel.None),
            ToolbarButtonId.Settings => (Strings.Settings, Icons.Settings, null, null, null, ToolbarWheel.None),
            _ => throw new Xunit.Sdk.XunitException($"새 버튼 {id}의 기대 행을 이 표에 적으세요."),
        };

        var b = Button(id);

        Assert.Equal(expected, (b.Tooltip, b.Icon, b.Flyout, b.BadgeGroup, b.HotkeyId, b.Wheel));
        Assert.Equal(expected.Flyout is not null, b.HasFlyout);
    }

    [Fact]
    public void Flyouts_EachKindIsOpenedByExactlyOneButton()
    {
        foreach (var kind in Enum.GetValues<ToolbarFlyoutKind>())
        {
            Assert.Single(AllButtons(), b => b.Flyout == kind);
        }

        Assert.Equal(
            new HashSet<ToolbarButtonId> { ToolbarButtonId.Shapes, ToolbarButtonId.Pen, ToolbarButtonId.Fading, ToolbarButtonId.Board },
            AllButtons().Where(b => b.HasFlyout).Select(b => b.Id).ToHashSet());
    }

    /// <summary>휠은 플라이아웃의 선택지 안을 순환한다 — 보존이지 승인이 아니다 (특성화).</summary>
    [Fact]
    public void Wheel_OnlyOnFlyoutBearingButtons() =>
        Assert.All(AllButtons().Where(b => b.Wheel != ToolbarWheel.None), b => Assert.True(b.HasFlyout));

    [Fact]
    public void HotkeyIds_AreUniqueAndNonEmpty()
    {
        var ids = AllHotkeyIds().ToList();

        Assert.Equal(10, ids.Count);
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("thickness-pair", ids);
    }

    /// <summary>오타 난 id(예: "click-through")는 툴팁의 핫키 줄을 조용히 비운다 — 여기서 잡는다. thickness-pair는 thinner/thicker 합성 라벨.</summary>
    [Fact]
    public void HotkeyIds_EveryOne_ResolvesThroughShellHotkeys()
    {
        var hotkeys = new ShellHotkeys(
            Dispatcher.CurrentDispatcher,
            new AppState(),
            () => new AppSettings(),
            undo: () => { },
            clearAll: () => { },
            startCapture: () => { },
            toggleToolbar: () => { },
            deleteSelection: () => { });

        Assert.All(AllHotkeyIds(), id => Assert.NotNull(hotkeys.HotkeyLabel(id)));
    }

    [Fact]
    public void Preview_TooltipAndHotkey_MatchThicknessPair()
    {
        var preview = Assert.Single(ToolbarLayout.Menu.OfType<ToolbarPreviewEntry>());

        Assert.Equal(Strings.Thickness, preview.Tooltip);
        Assert.Equal("thickness-pair", preview.HotkeyId);
        Assert.Single(ToolbarLayout.Menu.OfType<ToolbarQuickColorsEntry>());
    }

    /// <summary>스펙이 MTA에서 스냅샷 가능한 이유를 잠근다: 항목의 공개 속성에 델리게이트도 DispatcherObject(WPF UI·Freezable)도 없다.</summary>
    [Theory]
    [InlineData(typeof(ToolbarButtonEntry))]
    [InlineData(typeof(ToolbarPreviewEntry))]
    [InlineData(typeof(ToolbarSeparatorEntry))]
    [InlineData(typeof(ToolbarQuickColorsEntry))]
    public void Entries_CarryOnlyPlainData_ByReflection(Type entryType)
    {
        var properties = entryType.GetProperties().Where(p => p.Name != "EqualityContract").ToList();

        Assert.All(properties, p =>
        {
            Assert.False(typeof(Delegate).IsAssignableFrom(p.PropertyType), $"{entryType.Name}.{p.Name}: 델리게이트");
            Assert.False(typeof(DispatcherObject).IsAssignableFrom(p.PropertyType), $"{entryType.Name}.{p.Name}: WPF 객체");
        });
    }
}
