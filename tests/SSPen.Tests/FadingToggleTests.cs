using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 페이딩 잉크 토글 (사용자 요청 17차): 도구가 아니라 그리기 도구에 얹히는 속성이다.
/// 이전 구조(ToolKind.FadingPen)에서는 페이딩을 쓰려면 반드시 자유선이어야 했고,
/// 페이딩을 켜는 순간 쓰던 도구가 해제됐다. 여기 단언들이 그 회귀를 막는다.
/// </summary>
public sealed class FadingToggleTests
{
    [Theory]
    [InlineData(ToolKind.Pen)]
    [InlineData(ToolKind.Highlighter)]
    [InlineData(ToolKind.Text)]
    [InlineData(ToolKind.Line)]
    [InlineData(ToolKind.Arrow)]
    [InlineData(ToolKind.Rectangle)]
    [InlineData(ToolKind.Ellipse)]
    public void FadingApplies_ToEveryDrawingTool(ToolKind tool)
    {
        // 핵심 요청: 도형과 펜 도구에서 페이딩을 조합해 쓸 수 있어야 한다.
        var state = new AppState { ActiveTool = tool, FadingInk = true };

        Assert.True(state.FadingApplies);
    }

    [Theory]
    [InlineData(ToolKind.None)]
    [InlineData(ToolKind.Eraser)]
    [InlineData(ToolKind.Select)]
    public void FadingDoesNotApply_ToToolsThatCreateNothing(ToolKind tool)
    {
        // 지우개·선택·도구 없음은 새 요소를 만들지 않으므로 페이딩 개념이 성립하지 않는다.
        var state = new AppState { ActiveTool = tool, FadingInk = true };

        Assert.False(state.FadingApplies);
    }

    [Fact]
    public void FadingDoesNotApply_WhenToggleIsOff()
    {
        var state = new AppState { ActiveTool = ToolKind.Pen, FadingInk = false };

        Assert.False(state.FadingApplies);
    }

    [Fact]
    public void TogglingFading_DoesNotDisarmTheActiveTool()
    {
        // 구조 변경의 핵심: 예전에는 페이딩을 켜면 ActiveTool이 FadingPen으로 덮여
        // 쓰던 도형 도구가 사라졌다. 이제는 도구가 그대로 유지된다.
        var state = new AppState { ActiveTool = ToolKind.Rectangle };

        state.FadingInk = true;

        Assert.Equal(ToolKind.Rectangle, state.ActiveTool);
        Assert.True(state.FadingApplies);
    }

    [Fact]
    public void FadingToggle_SurvivesToolSwitching()
    {
        // 토글은 도구와 독립이므로 도구를 바꿔도 켜진 상태가 유지된다.
        var state = new AppState { ActiveTool = ToolKind.Pen, FadingInk = true };

        state.ActiveTool = ToolKind.Ellipse;

        Assert.True(state.FadingInk);
        Assert.True(state.FadingApplies);
    }

    [Fact]
    public void FadingToggle_StaysOnButStopsApplying_UnderEraser()
    {
        // 지우개로 잠시 전환해도 토글 자체는 꺼지지 않는다 (툴바 버튼도 켜진 채 표시).
        // 다만 그 순간 페이딩이 적용될 대상은 없다.
        var state = new AppState { ActiveTool = ToolKind.Pen, FadingInk = true };

        state.ActiveTool = ToolKind.Eraser;

        Assert.True(state.FadingInk);
        Assert.False(state.FadingApplies);

        state.ActiveTool = ToolKind.Pen;
        Assert.True(state.FadingApplies);
    }

    [Fact]
    public void FadingToggle_RaisesChanged()
    {
        // SettingsBinder가 Changed를 구독해 FadingInkController.Active를 갱신한다.
        // 이벤트가 없으면 토글을 켜도 실제로 페이드가 예약되지 않는다.
        var state = new AppState();
        int changes = 0;
        state.Changed += () => changes++;

        state.FadingInk = true;

        Assert.Equal(1, changes);
    }
}
