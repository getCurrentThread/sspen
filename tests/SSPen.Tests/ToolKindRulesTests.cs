using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="ToolKindRules"/>의 증인 (28단계, SEL-B-2, f12). 두 분류표를 <c>ToolKind</c> 전수로 잠근다 — 도구가 늘면
/// 행이 자동으로 따라오고, 기대 표에 없는 새 멤버는 여기서 빨갛다 (표 도구 추가 때 두 표를 따로 고쳐야 했던 결함 클래스).
/// </summary>
public class ToolKindRulesTests
{
    [Theory]
    [MemberData(nameof(AllToolKinds))]
    public void StyleGroupOf_EveryToolKind_IsClassified(ToolKind tool)
    {
        var expected = tool switch
        {
            ToolKind.Highlighter => ToolStyleGroup.Highlighter,
            ToolKind.Line or ToolKind.Arrow or ToolKind.Rectangle or ToolKind.Ellipse or ToolKind.Table or ToolKind.Text => ToolStyleGroup.Shape,
            ToolKind.None or ToolKind.Pen or ToolKind.Eraser or ToolKind.Select => ToolStyleGroup.Pen,
            _ => throw new Xunit.Sdk.XunitException($"새 도구 {tool}의 스타일 그룹을 이 표에 적으세요."),
        };

        Assert.Equal(expected, ToolKindRules.StyleGroupOf(tool));
    }

    [Theory]
    [MemberData(nameof(AllToolKinds))]
    public void FadingAppliesTo_EveryToolKind_MatchesTable(ToolKind tool)
    {
        bool expected = tool switch
        {
            ToolKind.None or ToolKind.Eraser or ToolKind.Select => false,
            ToolKind.Pen or ToolKind.Highlighter or ToolKind.Text
                or ToolKind.Line or ToolKind.Arrow or ToolKind.Rectangle or ToolKind.Ellipse or ToolKind.Table => true,
            _ => throw new Xunit.Sdk.XunitException($"새 도구 {tool}의 페이딩 가능 여부를 이 표에 적으세요."),
        };

        Assert.Equal(expected, ToolKindRules.FadingAppliesTo(tool));
    }

    /// <summary>AppState.ActiveStyleGroup은 이 표에 위임한다 — 두 벌이 생기지 않는다.</summary>
    [Theory]
    [MemberData(nameof(AllToolKinds))]
    public void AppState_ActiveStyleGroup_DelegatesToRules(ToolKind tool)
    {
        var state = new AppState { ActiveTool = tool };

        Assert.Equal(ToolKindRules.StyleGroupOf(tool), state.ActiveStyleGroup);
    }

    /// <summary>단일 판정 지점 (AGENTS "Fading ink is a toggle"): AppState에 복제본이 남아 있지 않다.</summary>
    [Fact]
    public void FadingAppliesTo_NotOnAppState_ByReflection()
    {
        Assert.Null(typeof(AppState).GetMethod("FadingAppliesTo"));
        Assert.NotNull(typeof(ToolKindRules).GetMethod("FadingAppliesTo"));
    }

    /// <summary>SEL-4: Select는 열거 말단에 있다 — 순서에 기대는 코드(툴바 순환·라우터 표)를 위한 트립와이어.</summary>
    [Fact]
    public void ToolKind_SelectIsLastMember_ByReflection() =>
        Assert.Equal(ToolKind.Select, Enum.GetValues<ToolKind>()[^1]);

    public static TheoryData<ToolKind> AllToolKinds()
    {
        var data = new TheoryData<ToolKind>();
        foreach (var tool in Enum.GetValues<ToolKind>())
        {
            data.Add(tool);
        }
        return data;
    }
}
