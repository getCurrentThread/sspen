using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 서피스 입력의 두 라우팅 표 (D4 / ARCH-2 / R7 / SEL-5 / SEL-LIM-5 / WI-16).
/// 순수 함수라 WPF 비주얼 트리 없이 전수 검증한다.
/// </summary>
public class SurfaceInputRouterTests
{
    public static TheoryData<ToolKind> AllTools()
    {
        var data = new TheoryData<ToolKind>();
        foreach (var tool in Enum.GetValues<ToolKind>())
        {
            data.Add(tool);
        }
        return data;
    }

    public static TheoryData<ToolKind> NonSelectTools()
    {
        var data = new TheoryData<ToolKind>();
        foreach (var tool in Enum.GetValues<ToolKind>())
        {
            if (tool != ToolKind.Select)
            {
                data.Add(tool);
            }
        }
        return data;
    }

    public static TheoryData<SurfaceGesture> AllGestures()
    {
        var data = new TheoryData<SurfaceGesture>();
        foreach (var gesture in Enum.GetValues<SurfaceGesture>())
        {
            data.Add(gesture);
        }
        return data;
    }

    // ---- RouteDown ----

    [Theory]
    [MemberData(nameof(AllTools))]
    public void RouteDown_NotInteractive_IsIgnored(ToolKind tool)
    {
        // D4: 인터랙티브 가드가 첫째다 — 도구·텍스트 상태와 무관하게 Ignore.
        foreach (var textEditing in new[] { false, true })
        {
            foreach (var overEditor in new[] { false, true })
            {
                Assert.Equal(
                    SurfaceGesture.Ignore,
                    SurfaceInputRouter.RouteDown(tool, interactive: false, textEditing, overEditor));
            }
        }
        Assert.False(SurfaceInputRouter.MarksHandled(SurfaceGesture.Ignore));
    }

    [Theory]
    [MemberData(nameof(AllTools))]
    public void RouteDown_TextBoxOpenAndClickOutside_CommitsTextAndLeavesHandledUnset(ToolKind tool)
    {
        // ARCH-2: 텍스트 바깥 클릭 선점은 도구 switch보다 **먼저**다 — 어떤 도구여도 CommitTextOnly.
        var gesture = SurfaceInputRouter.RouteDown(
            tool, interactive: true, textEditing: true, overActiveEditor: false);

        Assert.Equal(SurfaceGesture.CommitTextOnly, gesture);
        // 그리고 그 클릭은 소비되지 않는다 — 확정과 동시에 아래로 흘러간다.
        Assert.False(SurfaceInputRouter.MarksHandled(gesture));
    }

    [Theory]
    [MemberData(nameof(AllTools))]
    public void RouteDown_ClickInsideTextBox_FallsThroughToTool(ToolKind tool)
    {
        // 편집 중이어도 커서가 상자 위면 선점하지 않는다 (ARCH-2).
        var inside = SurfaceInputRouter.RouteDown(
            tool, interactive: true, textEditing: true, overActiveEditor: true);
        var noEditor = SurfaceInputRouter.RouteDown(
            tool, interactive: true, textEditing: false, overActiveEditor: false);

        Assert.NotEqual(SurfaceGesture.CommitTextOnly, inside);
        Assert.Equal(noEditor, inside);
    }

    [Theory]
    [MemberData(nameof(AllTools))]
    public void RouteDown_EveryToolKind_MapsToExactlyOneGesture(ToolKind tool)
    {
        // 전역성 가드: ToolKind가 늘어도 표의 행이 따라와야 한다.
        var gesture = SurfaceInputRouter.RouteDown(
            tool, interactive: true, textEditing: false, overActiveEditor: false);

        var expected = tool switch
        {
            ToolKind.None => SurfaceGesture.SwallowOnly,
            ToolKind.Pen => SurfaceGesture.StartStroke,
            ToolKind.Highlighter => SurfaceGesture.StartStroke,
            ToolKind.Eraser => SurfaceGesture.EraseAndDrag,
            ToolKind.Line => SurfaceGesture.StartLine,
            ToolKind.Arrow => SurfaceGesture.StartArrow,
            ToolKind.Rectangle => SurfaceGesture.StartRectangle,
            ToolKind.Ellipse => SurfaceGesture.StartEllipse,
            ToolKind.Text => SurfaceGesture.BeginTextEdit,
            ToolKind.Select => SurfaceGesture.BeginSelect,
            _ => throw new InvalidOperationException($"표에 없는 도구: {tool}"),
        };
        Assert.Equal(expected, gesture);
    }

    [Fact]
    public void RouteDown_ToolNone_SwallowsWithoutGesture()
    {
        // ToolKind.None은 도구 분기가 없어도 이벤트를 **삼킨다** — 오늘 switch 아래에서 참이 반환된다.
        // Ignore로 접으면 서피스가 오늘 삼키는 클릭을 흘려보낸다 (D4).
        var gesture = SurfaceInputRouter.RouteDown(
            ToolKind.None, interactive: true, textEditing: false, overActiveEditor: false);

        Assert.Equal(SurfaceGesture.SwallowOnly, gesture);
        Assert.True(SurfaceInputRouter.MarksHandled(gesture));
        Assert.NotEqual(SurfaceGesture.Ignore, gesture);
    }

    // ---- MarksHandled ----

    [Fact]
    public void MarksHandled_IgnoreAndCommitTextOnly_AreFalse()
    {
        Assert.False(SurfaceInputRouter.MarksHandled(SurfaceGesture.Ignore));
        Assert.False(SurfaceInputRouter.MarksHandled(SurfaceGesture.CommitTextOnly));
    }

    [Theory]
    [MemberData(nameof(AllGestures))]
    public void MarksHandled_EveryOtherGesture_IsTrue(SurfaceGesture gesture)
    {
        bool expected = gesture is not (SurfaceGesture.Ignore or SurfaceGesture.CommitTextOnly);
        Assert.Equal(expected, SurfaceInputRouter.MarksHandled(gesture));
    }

    // ---- RouteWheel ----

    [Theory]
    [MemberData(nameof(AllTools))]
    public void RouteWheel_NotInteractive_IsIgnored(ToolKind tool)
    {
        foreach (var dragActive in new[] { false, true })
        {
            foreach (var wheelAdjusts in new[] { false, true })
            {
                Assert.Equal(
                    WheelVerdict.Ignore,
                    SurfaceInputRouter.RouteWheel(tool, interactive: false, dragActive, wheelAdjusts));
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RouteWheel_SelectTool_NeverReturnsStepThickness(bool dragActive, bool wheelAdjustsPenSize)
    {
        // R7/SEL-5: 선택 도구에서 휠은 굵기로 가지 않는다 — SEL-5가 스타일 쓰기를 막아 무동작이었다.
        var verdict = SurfaceInputRouter.RouteWheel(
            ToolKind.Select, interactive: true, dragActive, wheelAdjustsPenSize);

        Assert.NotEqual(WheelVerdict.StepThickness, verdict);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RouteWheel_DragActive_SwallowsOnly(bool wheelAdjustsPenSize)
    {
        // R7: 드래그 중 휠은 삼키기만 한다 (두 세션이 갈리면 한 드래그가 실행취소 2번이 된다).
        Assert.Equal(
            WheelVerdict.SwallowOnly,
            SurfaceInputRouter.RouteWheel(
                ToolKind.Select, interactive: true, dragActive: true, wheelAdjustsPenSize));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RouteWheel_SelectToolNoDrag_ReturnsScaleSelection(bool wheelAdjustsPenSize)
    {
        // ScaleSelection은 **후보**이지 Handled가 아니다 — SEL-LIM-5 게이트는 호출부가 소유한다.
        // 그 증인: 소유 선택이 선택집합 전체가 아니면 오늘 서피스는 휠을 소비하지 않는다.
        Assert.Equal(
            WheelVerdict.ScaleSelection,
            SurfaceInputRouter.RouteWheel(
                ToolKind.Select, interactive: true, dragActive: false, wheelAdjustsPenSize));

        Assert.False(SelectionGroup.HandlesGrabbable(ownedCount: 1, selectionCount: 3));
        Assert.True(SelectionGroup.HandlesGrabbable(ownedCount: 3, selectionCount: 3));
    }

    [Theory]
    [MemberData(nameof(NonSelectTools))]
    public void RouteWheel_NonSelectToolWithSettingOff_IsIgnored(ToolKind tool)
    {
        foreach (var dragActive in new[] { false, true })
        {
            Assert.Equal(
                WheelVerdict.Ignore,
                SurfaceInputRouter.RouteWheel(
                    tool, interactive: true, dragActive, wheelAdjustsPenSize: false));
        }
    }

    [Theory]
    [MemberData(nameof(NonSelectTools))]
    public void RouteWheel_NonSelectToolWithSettingOn_StepsThickness(ToolKind tool)
    {
        // WI-16: 비선택 도구에서는 설정이 켜져 있으면 굵기 조정이다 (드래그 여부와 무관).
        foreach (var dragActive in new[] { false, true })
        {
            Assert.Equal(
                WheelVerdict.StepThickness,
                SurfaceInputRouter.RouteWheel(
                    tool, interactive: true, dragActive, wheelAdjustsPenSize: true));
        }
    }
}
