using System.Windows;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="SurfacePresentationRules"/>의 증인 (44단계, ARCH-1/D4/R8). 진리표 전수, 중단 행의 "후광·보드 손대지 않음",
/// 커서 표(ToolKind 전수 × 스타일러스 뒤집기), 그리고 "클릭 통과·배경·히트가 Interactive 하나에서만 나온다"는 형태 트립와이어.
/// 창 수준(exstyle·배경·커서가 실제로 함께 움직임)은 통합 SurfacePresentationTests가 본다.
/// </summary>
public class SurfacePresentationRulesTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void Resolve_NotSuspended_InteractiveAndHaloFollowState(bool interactive, bool haloActive, bool visible)
    {
        var p = SurfacePresentationRules.Resolve(suspended: false, visible, interactive, haloActive);

        Assert.Equal(visible ? Visibility.Visible : Visibility.Hidden, p.Visibility);
        Assert.Equal(interactive, p.Interactive);
        Assert.Equal(!haloActive, p.CollapseHalo);
        Assert.True(p.ApplyBoard);
    }

    /// <summary>중단 행: 보이되 입력 없음, 후광·보드는 건드리지 않는다 — 다른 입력과 무관 (오늘의 조기 반환 의미).</summary>
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Resolve_Suspended_IsVisibleButInert_HaloAndBoardUntouched(bool surfacesVisible, bool interactive, bool haloActive)
    {
        var p = SurfacePresentationRules.Resolve(suspended: true, surfacesVisible, interactive, haloActive);

        Assert.Equal(surfacesVisible ? Visibility.Visible : Visibility.Hidden, p.Visibility);
        Assert.False(p.Interactive);
        Assert.False(p.CollapseHalo);
        Assert.False(p.ApplyBoard);
    }

    /// <summary>ARCH-1 형태 트립와이어: 클릭 통과/배경/히트를 따로 싣는 필드가 없다 — 창이 Interactive 하나에서 셋을 유도한다.</summary>
    [Fact]
    public void SurfacePresentation_HasNoSeparateClickThroughOrBackgroundField_ByReflection()
    {
        var names = typeof(SurfacePresentation).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();

        Assert.Equal(["ApplyBoard", "CollapseHalo", "Interactive", "Visibility"], names);
    }

    [Theory]
    [MemberData(nameof(AllTools))]
    public void HoverCursor_EveryTool_MatchesTable(ToolKind tool)
    {
        var expected = tool switch
        {
            ToolKind.Pen or ToolKind.Highlighter => SurfaceCursorKind.Pen,
            ToolKind.Text => SurfaceCursorKind.IBeam,
            ToolKind.Eraser => SurfaceCursorKind.Eraser,
            ToolKind.Select => SurfaceCursorKind.Arrow,
            ToolKind.None or ToolKind.Line or ToolKind.Arrow or ToolKind.Rectangle or ToolKind.Ellipse or ToolKind.Table => SurfaceCursorKind.Cross,
            _ => throw new Xunit.Sdk.XunitException($"새 도구 {tool}의 커서를 이 표에 적으세요."),
        };

        Assert.Equal(expected, SurfacePresentationRules.HoverCursor(tool, stylusInverted: false));
    }

    /// <summary>R8: 펜 뒤집기는 도구와 무관하게 지우개 커서다.</summary>
    [Theory]
    [MemberData(nameof(AllTools))]
    public void HoverCursor_StylusInverted_IsEraserForEveryTool(ToolKind tool) =>
        Assert.Equal(SurfaceCursorKind.Eraser, SurfacePresentationRules.HoverCursor(tool, stylusInverted: true));

    public static TheoryData<ToolKind> AllTools()
    {
        var data = new TheoryData<ToolKind>();
        foreach (var tool in Enum.GetValues<ToolKind>())
        {
            data.Add(tool);
        }
        return data;
    }
}
