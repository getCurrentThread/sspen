using SSPen.Annotation;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 툴바 버튼↔상태 매핑 검증 (god file 분할, ARCH-11 후속): 도형/펜 그룹 재클릭 로테이션,
/// IsActive 매핑, IconFor 글리프 반영.
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
}
