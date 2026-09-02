using System.Windows.Media;
using SSPen.Annotation;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 툴바 버튼↔상태 매핑 검증 (god file 분할, ARCH-11 후속): 도형/펜 그룹 재클릭 로테이션,
/// IsActive 매핑, IconFor 글리프 반영. 36단계부터는 어댑터(ToolbarParts/StripBuilder/Flyouts/Window)에
/// 인라인이던 순수 판정(점 지름 표·보드 배지·퀵스와치 링·현재 칸·같은 도구 재선택 해제)의 특성화 표도 여기 둔다.
/// </summary>
public class ToolbarStateMapTests
{
    [Fact]
    public void NextInCycle_Inactive_ReturnsFirstTool()
    {
        var next = ToolbarStateMap.NextInCycle(ToolbarStateMap.ShapeCycle, ToolKind.None);

        Assert.Equal(ToolKind.Line, next);
    }

    [Fact]
    public void NextInCycle_Active_CyclesToNextTool()
    {
        var next = ToolbarStateMap.NextInCycle(ToolbarStateMap.ShapeCycle, ToolKind.Line);

        Assert.Equal(ToolKind.Arrow, next);
    }

    [Fact]
    public void NextInCycle_LastTool_WrapsToFirst()
    {
        var next = ToolbarStateMap.NextInCycle(ToolbarStateMap.ShapeCycle, ToolKind.Table);

        Assert.Equal(ToolKind.Line, next);
    }

    [Fact]
    public void NextInCycle_PenGroup_CyclesThroughPenHighlighterText()
    {
        Assert.Equal(ToolKind.Pen, ToolbarStateMap.NextInCycle(ToolbarStateMap.PenCycle, ToolKind.None));
        Assert.Equal(ToolKind.Highlighter, ToolbarStateMap.NextInCycle(ToolbarStateMap.PenCycle, ToolKind.Pen));
        Assert.Equal(ToolKind.Text, ToolbarStateMap.NextInCycle(ToolbarStateMap.PenCycle, ToolKind.Highlighter));
        Assert.Equal(ToolKind.Pen, ToolbarStateMap.NextInCycle(ToolbarStateMap.PenCycle, ToolKind.Text));
    }

    [Theory]
    [InlineData(ToolKind.Pen)]
    [InlineData(ToolKind.Highlighter)]
    [InlineData(ToolKind.Text)]
    public void IsActive_PenButton_ActiveForWholeGroup(ToolKind tool)
    {
        var state = new AppState { ActiveTool = tool };

        bool active = ToolbarStateMap.IsActive(state, ToolbarButtonId.Pen, menuCollapsed: false);

        Assert.True(active);
    }

    [Fact]
    public void IsActive_PenButton_InactiveForOtherTool()
    {
        var state = new AppState { ActiveTool = ToolKind.Eraser };

        bool active = ToolbarStateMap.IsActive(state, ToolbarButtonId.Pen, menuCollapsed: false);

        Assert.False(active);
    }

    /// <summary>
    /// X7/R9 회귀 감시: <c>ToolbarButtonId.Select</c>를 enum에만 추가하고 <c>IsActive</c> 분기를
    /// 빼먹으면 <c>_ =&gt; false</c> 폴백으로 버튼이 **영원히 비활성**으로 보이는 무증상 회귀가 된다.
    /// </summary>
    [Fact]
    public void IsActive_SelectButtonWithSelectTool_ReturnsTrue()
    {
        var state = new AppState { ActiveTool = ToolKind.Select };

        bool active = ToolbarStateMap.IsActive(state, ToolbarButtonId.Select, menuCollapsed: false);

        Assert.True(active);
    }

    [Theory]
    [InlineData(ToolKind.None)]
    [InlineData(ToolKind.Pen)]
    [InlineData(ToolKind.Eraser)]
    [InlineData(ToolKind.Rectangle)]
    public void IsActive_SelectButtonWithOtherTool_ReturnsFalse(ToolKind tool)
    {
        var state = new AppState { ActiveTool = tool };

        bool active = ToolbarStateMap.IsActive(state, ToolbarButtonId.Select, menuCollapsed: false);

        Assert.False(active);
    }

    [Fact]
    public void IsActive_VisibilityButton_ReflectsMenuCollapsedFlag()
    {
        var state = new AppState();

        Assert.True(ToolbarStateMap.IsActive(state, ToolbarButtonId.Visibility, menuCollapsed: true));
        Assert.False(ToolbarStateMap.IsActive(state, ToolbarButtonId.Visibility, menuCollapsed: false));
    }

    [Fact]
    public void IconFor_PenButton_ReflectsSelectedTool()
    {
        var state = new AppState { ActiveTool = ToolKind.Highlighter };
        var fallback = Icons.Pen;

        var icon = ToolbarStateMap.IconFor(state, ToolbarButtonId.Pen, menuCollapsed: false, fallback);

        Assert.Equal(Icons.Highlight, icon);
    }

    [Fact]
    public void IconFor_ShapesButton_ReflectsSelectedShape()
    {
        var state = new AppState { ActiveTool = ToolKind.Rectangle };
        var fallback = Icons.Shapes;

        var icon = ToolbarStateMap.IconFor(state, ToolbarButtonId.Shapes, menuCollapsed: false, fallback);

        Assert.Equal(Icons.Square, icon);
    }

    [Fact]
    public void IconFor_ShapesButton_ReflectsTable()
    {
        var state = new AppState { ActiveTool = ToolKind.Table };
        var fallback = Icons.Shapes;

        var icon = ToolbarStateMap.IconFor(state, ToolbarButtonId.Shapes, menuCollapsed: false, fallback);

        Assert.Equal(Icons.Table, icon);
    }

    [Fact]
    public void IconFor_VisibilityButton_TogglesEyeGlyph()
    {
        var state = new AppState();
        var fallback = Icons.Eye;

        var expanded = ToolbarStateMap.IconFor(state, ToolbarButtonId.Visibility, menuCollapsed: false, fallback);
        var collapsed = ToolbarStateMap.IconFor(state, ToolbarButtonId.Visibility, menuCollapsed: true, fallback);

        Assert.Equal(Icons.Eye, expanded);
        Assert.Equal(Icons.EyeOff, collapsed);
    }

    [Fact]
    public void BadgeGroupFor_PenButton_FollowsActiveSubTool()
    {
        var state = new AppState { ActiveTool = ToolKind.Highlighter };

        var group = ToolbarStateMap.BadgeGroupFor(state, ToolbarButtonId.Pen, ToolStyleGroup.Pen);

        Assert.Equal(ToolStyleGroup.Highlighter, group);
    }

    [Fact]
    public void BadgeGroupFor_NonPenButton_ReturnsFallback()
    {
        var state = new AppState { ActiveTool = ToolKind.Highlighter };

        var group = ToolbarStateMap.BadgeGroupFor(state, ToolbarButtonId.Shapes, ToolStyleGroup.Shape);

        Assert.Equal(ToolStyleGroup.Shape, group);
    }

    [Fact]
    public void NextToolByWheel_ZeroDelta_ReturnsSameTool()
    {
        Assert.Equal(ToolKind.Pen, ToolbarStateMap.NextToolByWheel(ToolKind.Pen, 0));
    }

    [Fact]
    public void NextToolByWheel_ScrollDown_CyclesInOrder()
    {
        var current = ToolKind.Select;
        var expectedOrder = ToolbarStateMap.WheelToolCycle;

        for (int i = 1; i < expectedOrder.Length; i++)
        {
            current = ToolbarStateMap.NextToolByWheel(current, -120);
            Assert.Equal(expectedOrder[i], current);
        }

        // 마지막에서 다시 첫 번째로 순환
        current = ToolbarStateMap.NextToolByWheel(current, -120);
        Assert.Equal(ToolKind.Select, current);
    }

    [Fact]
    public void NextToolByWheel_ScrollUp_CyclesInReverseOrder()
    {
        var current = ToolKind.Select;

        // 첫 번째에서 위로 스크롤 시 마지막 도구(Text)로 이동
        current = ToolbarStateMap.NextToolByWheel(current, 120);
        Assert.Equal(ToolKind.Text, current);

        current = ToolbarStateMap.NextToolByWheel(current, 120);
        Assert.Equal(ToolKind.Table, current);

        current = ToolbarStateMap.NextToolByWheel(current, 120);
        Assert.Equal(ToolKind.Ellipse, current);
    }

    [Fact]
    public void NextToolByWheel_FromNone_StartsAtFirstOrLast()
    {
        Assert.Equal(ToolKind.Select, ToolbarStateMap.NextToolByWheel(ToolKind.None, -120));
        Assert.Equal(ToolKind.Text, ToolbarStateMap.NextToolByWheel(ToolKind.None, 120));
    }

    [Fact]
    public void NextInCycle_WithDelta_CyclesForwardAndBackward()
    {
        Assert.Equal(ToolKind.Arrow, ToolbarStateMap.NextInCycle(ToolbarStateMap.ShapeCycle, ToolKind.Line, -120));
        Assert.Equal(ToolKind.Table, ToolbarStateMap.NextInCycle(ToolbarStateMap.ShapeCycle, ToolKind.Line, 120));
        Assert.Equal(ToolKind.Line, ToolbarStateMap.NextInCycle(ToolbarStateMap.ShapeCycle, ToolKind.Line, 0));
    }

    [Fact]
    public void NextQuickColorSlotByWheel_CyclesSlots()
    {
        Assert.Equal(1, ToolbarStateMap.NextQuickColorSlotByWheel(0, -120, 6));
        Assert.Equal(5, ToolbarStateMap.NextQuickColorSlotByWheel(0, 120, 6));
        Assert.Equal(0, ToolbarStateMap.NextQuickColorSlotByWheel(0, 0, 6));
    }

    // ----- 36단계: 어댑터에 인라인이던 순수 판정의 특성화 (추출 전 값을 그대로 적었다 — 보존이지 승인이 아니다) -----

    /// <summary>미리보기 원 지름 표 (ToolbarParts.UpdatePreviewDot에 있던 8/11/14/18/22 그대로). 단계가 늘면 이 표가 빨갛다.</summary>
    [Theory]
    [MemberData(nameof(ThicknessScaleTests.AllSteps), MemberType = typeof(ThicknessScaleTests))]
    public void PreviewDotDiameter_EveryStep_MatchesTable(ThicknessStep step)
    {
        double expected = step switch
        {
            ThicknessStep.XSmall => 8,
            ThicknessStep.Small => 11,
            ThicknessStep.Medium => 14,
            ThicknessStep.Large => 18,
            ThicknessStep.XLarge => 22,
            _ => throw new Xunit.Sdk.XunitException($"새 단계 {step}의 미리보기 점 지름을 이 표에 적으세요."),
        };

        Assert.Equal(expected, ToolbarStateMap.PreviewDotDiameter(step));
    }

    /// <summary>굵기 플라이아웃 점 지름 표 (ToolbarFlyouts.BuildThicknessFlyout의 6/10/14/18/22 그대로).</summary>
    [Theory]
    [MemberData(nameof(ThicknessScaleTests.AllSteps), MemberType = typeof(ThicknessScaleTests))]
    public void FlyoutThicknessDotDiameter_EveryStep_MatchesTable(ThicknessStep step)
    {
        double expected = step switch
        {
            ThicknessStep.XSmall => 6,
            ThicknessStep.Small => 10,
            ThicknessStep.Medium => 14,
            ThicknessStep.Large => 18,
            ThicknessStep.XLarge => 22,
            _ => throw new Xunit.Sdk.XunitException($"새 단계 {step}의 플라이아웃 점 지름을 이 표에 적으세요."),
        };

        Assert.Equal(expected, ToolbarStateMap.FlyoutThicknessDotDiameter(step));
    }

    /// <summary>
    /// 세 굵기 표는 합치지 않는다 (f70c3fb의 원칙): 미리보기 점·플라이아웃 점·ThicknessScale(펜 px)은 목적이 다른 양이다.
    /// 작은 두 단계에서 두 점 표가 갈라지는 것이 그 증거 — 하나로 합치면 이 단언이 빨갛다.
    /// </summary>
    [Fact]
    public void PreviewDot_AndFlyoutDot_AreDifferentTables()
    {
        Assert.NotEqual(ToolbarStateMap.PreviewDotDiameter(ThicknessStep.XSmall), ToolbarStateMap.FlyoutThicknessDotDiameter(ThicknessStep.XSmall));
        Assert.NotEqual(ToolbarStateMap.PreviewDotDiameter(ThicknessStep.Small), ToolbarStateMap.FlyoutThicknessDotDiameter(ThicknessStep.Small));
        Assert.NotEqual(ToolbarStateMap.PreviewDotDiameter(ThicknessStep.XSmall), ThicknessScale.PenPixels(ThicknessStep.XSmall));
    }

    /// <summary>보드 배지 (사용자 조타 14차): 없음이면 숨김, 블랙보드만 검정 (ToolbarParts.RefreshButton의 두 삼항 그대로).</summary>
    [Theory]
    [InlineData(BoardMode.None, false, false)]
    [InlineData(BoardMode.White, true, false)]
    [InlineData(BoardMode.Black, true, true)]
    public void BoardBadge_VisibilityAndColor_FollowBoard(BoardMode board, bool visible, bool black)
    {
        Assert.Equal(visible, ToolbarStateMap.BoardBadgeVisible(board));
        Assert.Equal(black, ToolbarStateMap.BoardBadgeIsBlack(board));
    }

    [Fact]
    public void QuickSwatchBorderThickness_SameAsCurrentColor_Is2()
    {
        Assert.Equal(2, ToolbarStateMap.QuickSwatchBorderThickness(Colors.Red, Colors.Red));
    }

    [Fact]
    public void QuickSwatchBorderThickness_OtherColor_Is0()
    {
        Assert.Equal(0, ToolbarStateMap.QuickSwatchBorderThickness(Colors.Red, Colors.Blue));
    }

    /// <summary>같은 도구 재선택 시 해제 (Epic Pen 동작: 도구 없음 = 포인터 모드) — 스트립 버튼과 플라이아웃 항목이 같은 판정을 쓴다.</summary>
    [Theory]
    [InlineData(ToolKind.Pen, ToolKind.Pen, ToolKind.None)]
    [InlineData(ToolKind.Select, ToolKind.Select, ToolKind.None)]
    [InlineData(ToolKind.Pen, ToolKind.Eraser, ToolKind.Eraser)]
    [InlineData(ToolKind.None, ToolKind.Select, ToolKind.Select)]
    public void ToggleTool_SameToolReleases_OtherToolSelects(ToolKind current, ToolKind requested, ToolKind expected)
    {
        Assert.Equal(expected, ToolbarStateMap.ToggleTool(current, requested));
    }

    [Fact]
    public void CurrentQuickColorSlot_Found_ReturnsIndex()
    {
        Color[] quick = [Colors.Red, Colors.Green, Colors.Blue];

        Assert.Equal(2, ToolbarStateMap.CurrentQuickColorSlot(quick, Colors.Blue));
    }

    /// <summary>어느 칸에도 없으면 0 — 확장 팔레트 색을 쓰는 중 휠을 돌리면 첫 칸부터 순환한다 (오늘 동작).</summary>
    [Fact]
    public void CurrentQuickColorSlot_NotFound_ReturnsZero()
    {
        Color[] quick = [Colors.Red, Colors.Green, Colors.Blue];

        Assert.Equal(0, ToolbarStateMap.CurrentQuickColorSlot(quick, Colors.Yellow));
    }

    [Fact]
    public void CurrentQuickColorSlot_DuplicateColor_ReturnsFirstMatch()
    {
        Color[] quick = [Colors.Red, Colors.Blue, Colors.Blue];

        Assert.Equal(1, ToolbarStateMap.CurrentQuickColorSlot(quick, Colors.Blue));
    }
}
