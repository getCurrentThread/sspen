using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 5단계: 제스처 시작 시점 스타일 동결 규약 (AGENTS.md 동결 규약, 사용자 요청 17차).
///
/// <b>정직한 표기</b>: 여기서 잠그는 것은 <see cref="GestureStyleSnapshot"/>의 계약뿐이다 —
/// 커밋 경로가 이 스냅샷만 읽는다는 사실은 <c>SurfaceInputController.cs</c>에
/// <c>state.FadingApplies</c>가 0건이라는 리뷰 게이트가 지킨다 (호출부는 WPF 마우스 이벤트 뒤에 있다).
///
/// <see cref="AppState.SetColor"/>/<see cref="AppState.SetThickness"/>는
/// <c>ActiveTool == Select</c>이면 무시되므로(SEL-5) 모든 테스트가 도구를 먼저 세팅한다.
/// </summary>
public class GestureStyleSnapshotTests
{
    private static readonly Color Red = Color.FromRgb(0xE7, 0x4C, 0x3C);
    private static readonly Color Blue = Color.FromRgb(0x34, 0x98, 0xDB);

    [Fact]
    public void ForStroke_Highlighter_UsesHighlighterThickness()
    {
        // 스타일 그룹 동기화는 기본 false이므로 펜/형광펜 굵기를 서로 다르게 둘 수 있다.
        var state = new AppState { ActiveTool = ToolKind.Pen };
        state.SetThickness(ToolStyleGroup.Pen, ThicknessStep.XSmall);
        state.SetThickness(ToolStyleGroup.Highlighter, ThicknessStep.XLarge);
        state.ActiveTool = ToolKind.Highlighter;

        var style = GestureStyleSnapshot.ForStroke(state);

        Assert.True(style.IsHighlighter);
        Assert.Equal(state.HighlighterThickness, style.Thickness);
        Assert.NotEqual(state.PenThickness, style.Thickness);
    }

    [Fact]
    public void ForStroke_PenTool_UsesPenThickness()
    {
        var state = new AppState { ActiveTool = ToolKind.Pen };
        state.SetThickness(ToolStyleGroup.Pen, ThicknessStep.XSmall);
        state.SetThickness(ToolStyleGroup.Highlighter, ThicknessStep.XLarge);

        var style = GestureStyleSnapshot.ForStroke(state);

        Assert.False(style.IsHighlighter);
        Assert.Equal(state.PenThickness, style.Thickness);
        Assert.NotEqual(state.HighlighterThickness, style.Thickness);
    }

    [Fact]
    public void ForStroke_CapturesFadingApplies()
    {
        var state = new AppState { ActiveTool = ToolKind.Pen, FadingInk = true };
        Assert.True(GestureStyleSnapshot.ForStroke(state).IsFading);

        // 지우개는 새 요소를 만들지 않으므로 페이딩 개념이 성립하지 않는다 (FadingAppliesTo).
        state.ActiveTool = ToolKind.Eraser;
        Assert.False(GestureStyleSnapshot.ForStroke(state).IsFading);
    }

    [Fact]
    public void ForStroke_QuickColorChangedAfterSnapshot_IsUnaffected()
    {
        // 동결 규약의 핵심: 스냅샷 뒤 AppState가 바뀌어도 이미 뜬 값은 움직이지 않는다
        // (readonly record struct라 지연 평가가 타입으로 불가능하다).
        var state = new AppState { ActiveTool = ToolKind.Pen };
        state.CurrentColor = Red;
        var style = GestureStyleSnapshot.ForStroke(state);

        state.CurrentColor = Blue;
        state.SetThickness(ToolStyleGroup.Pen, ThicknessStep.XLarge);

        Assert.Equal(Red, style.Color);
        Assert.NotEqual(state.CurrentColor, style.Color);
        Assert.NotEqual(state.PenThickness, style.Thickness);
    }

    [Fact]
    public void ForShape_UsesShapeThickness_NotPenThickness()
    {
        var state = new AppState { ActiveTool = ToolKind.Pen };
        state.SetThickness(ToolStyleGroup.Pen, ThicknessStep.XSmall);
        state.ActiveTool = ToolKind.Rectangle;
        state.SetThickness(ToolStyleGroup.Shape, ThicknessStep.XLarge);

        var style = GestureStyleSnapshot.ForShape(state);

        Assert.Equal(state.ShapeThickness, style.Thickness);
        Assert.NotEqual(state.PenThickness, style.Thickness);
    }

    [Fact]
    public void ForShape_FadingToggledAfterSnapshot_IsUnaffected()
    {
        var state = new AppState { ActiveTool = ToolKind.Rectangle, FadingInk = true };
        var style = GestureStyleSnapshot.ForShape(state);

        state.FadingInk = false;

        Assert.True(style.IsFading);
        Assert.False(state.FadingApplies);
    }

    [Fact]
    public void ForText_UsesTextFontSize_NotShapeThickness()
    {
        // 도형 굵기 2/4/6/10/16과 텍스트 크기 12/16/24/36/48은 같은 double이지만 다른 양이다.
        var state = new AppState { ActiveTool = ToolKind.Text };
        state.SetThickness(ToolStyleGroup.Shape, ThicknessStep.XLarge);

        var style = GestureStyleSnapshot.ForText(state);

        Assert.Equal(state.TextFontSize, style.FontSize);
        Assert.NotEqual(state.ShapeThickness, style.FontSize);
    }

    [Fact]
    public void ForText_QuickColorChangedAfterSnapshot_IsUnaffected()
    {
        var state = new AppState { ActiveTool = ToolKind.Text };
        state.CurrentColor = Red;
        var style = GestureStyleSnapshot.ForText(state);

        state.CurrentColor = Blue;

        Assert.Equal(Red, style.Color);
        Assert.NotEqual(state.CurrentColor, style.Color);
    }
}
