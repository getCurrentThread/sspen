using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 도구 그룹별 색·굵기 개별 보유 검증 (사용자 조타: 펜/형광펜/도형 개별 스타일, 기본 개별·설정으로 동기화).
/// </summary>
public class ToolStyleTests
{
    private static readonly Color Red = (Color)ColorConverter.ConvertFromString("#E74C3C");
    private static readonly Color Black = (Color)ColorConverter.ConvertFromString("#000000");

    // ---- SEL-5 / f12: 선택 도구는 어떤 ToolStyleGroup에도 속하지 않는다 ----

    [Fact]
    public void SetColor_WhileSelectToolActive_LeavesAllGroupsUnchanged()
    {
        var state = new AppState { ActiveTool = ToolKind.Select };
        var pen = state.ColorOf(ToolStyleGroup.Pen);
        var highlighter = state.ColorOf(ToolStyleGroup.Highlighter);
        var shape = state.ColorOf(ToolStyleGroup.Shape);

        state.SetColor(ToolStyleGroup.Pen, Black);
        state.CurrentColor = Black;

        Assert.Equal(pen, state.ColorOf(ToolStyleGroup.Pen));
        Assert.Equal(highlighter, state.ColorOf(ToolStyleGroup.Highlighter));
        Assert.Equal(shape, state.ColorOf(ToolStyleGroup.Shape));
    }

    [Fact]
    public void SetThickness_WhileSelectToolActive_LeavesAllGroupsUnchanged()
    {
        var state = new AppState { ActiveTool = ToolKind.Select };
        var pen = state.ThicknessOf(ToolStyleGroup.Pen);
        var highlighter = state.ThicknessOf(ToolStyleGroup.Highlighter);
        var shape = state.ThicknessOf(ToolStyleGroup.Shape);

        state.SetThickness(ToolStyleGroup.Pen, ThicknessStep.XLarge);

        Assert.Equal(pen, state.ThicknessOf(ToolStyleGroup.Pen));
        Assert.Equal(highlighter, state.ThicknessOf(ToolStyleGroup.Highlighter));
        Assert.Equal(shape, state.ThicknessOf(ToolStyleGroup.Shape));
    }

    [Fact]
    public void StepThickness_WhileSelectToolActive_IsNoOp()
    {
        // 휠 굵기 조정은 Thickness 프로퍼티 → SetThickness 경유라 함께 막힌다 (입력 계층 추가 코드 불필요).
        var state = new AppState { ActiveTool = ToolKind.Select };
        var before = state.ThicknessOf(ToolStyleGroup.Pen);

        state.StepThickness(+1);
        state.StepThickness(-1);

        Assert.Equal(before, state.ThicknessOf(ToolStyleGroup.Pen));
    }

    [Fact]
    public void ActiveStyleGroup_WhileSelectToolActive_ReturnsPen()
    {
        // SEL-B-2 / f12-a: 쓰기만 차단하고 **읽기는 그대로** — 강조 커서 후광이 펜 색으로 표시된다.
        var state = new AppState { ActiveTool = ToolKind.Select };

        Assert.Equal(ToolStyleGroup.Pen, state.ActiveStyleGroup);
        Assert.Equal(state.ColorOf(ToolStyleGroup.Pen), state.CurrentColor);
    }

    [Fact]
    public void SetColor_WhileSelectToolActiveThenPen_ResumesNormally()
    {
        var state = new AppState { ActiveTool = ToolKind.Select };
        state.SetColor(ToolStyleGroup.Pen, Black);
        Assert.NotEqual(Black, state.ColorOf(ToolStyleGroup.Pen));

        state.ActiveTool = ToolKind.Pen;
        state.SetColor(ToolStyleGroup.Pen, Black);

        Assert.Equal(Black, state.ColorOf(ToolStyleGroup.Pen));
    }

    // ---- CRIT-19 / ARCH-22: 한 논리적 변경 = Changed 정확히 1회 ----

    [Fact]
    public void ClickThrough_Enable_RaisesChangedOnce()
    {
        var state = new AppState { ActiveTool = ToolKind.Pen };
        int raised = 0;
        state.Changed += () => raised++;

        state.ClickThrough = true;

        Assert.Equal(1, raised);
        Assert.Equal(ToolKind.None, state.ActiveTool);
        Assert.True(state.ClickThrough);
    }

    [Fact]
    public void ClickThrough_Disable_RaisesChangedOnce()
    {
        // ARCH-22 (a): 해제 경로는 SetActiveTool을 타지 않는다 — setter가 직접 발화해야 한다.
        // 빠뜨리면 툴바 버튼 강조가 실제 상태와 어긋난 채 남는다.
        var state = new AppState { ClickThrough = true };
        int raised = 0;
        state.Changed += () => raised++;

        state.ClickThrough = false;

        Assert.Equal(1, raised);
        Assert.False(state.ClickThrough);
    }

    [Fact]
    public void ClickThrough_EnableWhileNoToolActive_RaisesChangedOnce()
    {
        // ARCH-22 (b): ActiveTool이 이미 None이라 전이가 없으므로 setter가 직접 발화해야 한다.
        var state = new AppState();
        Assert.Equal(ToolKind.None, state.ActiveTool);
        int raised = 0;
        state.Changed += () => raised++;

        state.ClickThrough = true;

        Assert.Equal(1, raised);
        Assert.True(state.ClickThrough);
    }

    [Fact]
    public void ClickThrough_SetToSameValue_RaisesNothing()
    {
        var state = new AppState { ClickThrough = true };
        int raised = 0;
        state.Changed += () => raised++;

        state.ClickThrough = true;

        Assert.Equal(0, raised);
    }

    [Fact]
    public void ActiveToolChanged_FiresOnlyOnRealTransition_WithPreviousAndCurrent()
    {
        var state = new AppState();
        var transitions = new List<(ToolKind Previous, ToolKind Current)>();
        state.ActiveToolChanged += (previous, current) => transitions.Add((previous, current));

        state.ActiveTool = ToolKind.Pen;
        state.ActiveTool = ToolKind.Pen;   // 전이 아님
        state.ActiveTool = ToolKind.Select;
        state.SetColor(ToolStyleGroup.Pen, Black); // 스타일 변경은 전이 아님

        Assert.Equal(
            [(ToolKind.None, ToolKind.Pen), (ToolKind.Pen, ToolKind.Select)],
            transitions);
    }

    [Fact]
    public void SelectTool_DoesNotBelongToAnyStyleGroupRotation()
    {
        // f12: 선택 도구는 도형·펜 그룹 어느 쪽에도 매핑되지 않고 포괄 폴백으로 펜을 반환한다.
        var state = new AppState { ActiveTool = ToolKind.Select };

        Assert.Equal(ToolStyleGroup.Pen, state.ActiveStyleGroup);
        Assert.True(state.IsInteractive, "선택 도구도 None이 아니므로 서피스가 입력을 받는다.");
    }

    [Theory]
    [InlineData(BoardMode.White)]
    [InlineData(BoardMode.Black)]
    public void NextBoard_TurnsOnPreferredBoard_WhenOff(BoardMode preferred)
    {
        // 사용자 요청 17차: 꺼져 있으면 **설정한 기본색**으로 켜진다 (화이트 고정 아님).
        Assert.Equal(preferred, AppState.NextBoard(BoardMode.None, preferred));
    }

    [Theory]
    [InlineData(BoardMode.White)]
    [InlineData(BoardMode.Black)]
    public void NextBoard_TurnsOff_WhenAnyBoardIsOn(BoardMode active)
    {
        // 사용자 요청 15차: 보드가 켜져 있으면 색과 무관하게 바로 꺼진다.
        // 기본색이 무엇이든(여기서는 반대색) 끄는 동작이 우선한다.
        var opposite = active == BoardMode.White ? BoardMode.Black : BoardMode.White;
        Assert.Equal(BoardMode.None, AppState.NextBoard(active, opposite));
    }

    [Fact]
    public void DefaultBoard_RejectsNone_SoBoardButtonAlwaysTurnsSomethingOn()
    {
        // None이 기본값으로 들어가면 보드 버튼이 아무것도 켜지 않는 죽은 버튼이 된다.
        var state = new AppState { DefaultBoard = BoardMode.Black };

        state.DefaultBoard = BoardMode.None;

        Assert.Equal(BoardMode.Black, state.DefaultBoard);
    }

    [Fact]
    public void ToggleBoard_SameMode_TurnsOff_OtherMode_Switches()
    {
        var state = new AppState();
        state.ToggleBoard(BoardMode.White);
        Assert.Equal(BoardMode.White, state.Board);

        state.ToggleBoard(BoardMode.Black);
        Assert.Equal(BoardMode.Black, state.Board);

        state.ToggleBoard(BoardMode.Black);
        Assert.Equal(BoardMode.None, state.Board);
    }

    [Fact]
    public void IndividualMode_PenColorChange_DoesNotTouchOtherGroups()
    {
        var state = new AppState();
        var highlighterBefore = state.ColorOf(ToolStyleGroup.Highlighter);
        var shapeBefore = state.ColorOf(ToolStyleGroup.Shape);

        state.ActiveTool = ToolKind.Pen;
        state.CurrentColor = Black;

        Assert.Equal(Black, state.ColorOf(ToolStyleGroup.Pen));
        Assert.Equal(highlighterBefore, state.ColorOf(ToolStyleGroup.Highlighter));
        Assert.Equal(shapeBefore, state.ColorOf(ToolStyleGroup.Shape));
    }

    [Fact]
    public void IndividualMode_ThicknessFollowsActiveGroup()
    {
        var state = new AppState();
        state.ActiveTool = ToolKind.Highlighter;
        state.Thickness = ThicknessStep.XLarge;

        state.ActiveTool = ToolKind.Line;
        state.Thickness = ThicknessStep.XSmall;

        Assert.Equal(ThicknessStep.XLarge, state.ThicknessOf(ToolStyleGroup.Highlighter));
        Assert.Equal(ThicknessStep.XSmall, state.ThicknessOf(ToolStyleGroup.Shape));
        Assert.Equal(ThicknessStep.Medium, state.ThicknessOf(ToolStyleGroup.Pen));
    }

    [Fact]
    public void ActiveStyleGroup_MapsToolsToGroups()
    {
        var state = new AppState();
        Assert.Equal(ToolStyleGroup.Pen, state.ActiveStyleGroup); // 도구 없음 → 펜

        state.ActiveTool = ToolKind.Eraser;
        Assert.Equal(ToolStyleGroup.Pen, state.ActiveStyleGroup);

        state.ActiveTool = ToolKind.Highlighter;
        Assert.Equal(ToolStyleGroup.Highlighter, state.ActiveStyleGroup);

        foreach (var shapeTool in (ToolKind[])[ToolKind.Line, ToolKind.Arrow, ToolKind.Rectangle, ToolKind.Ellipse, ToolKind.Text])
        {
            state.ActiveTool = shapeTool;
            Assert.Equal(ToolStyleGroup.Shape, state.ActiveStyleGroup);
        }
    }

    [Fact]
    public void ClickThrough_IsExclusiveSelection_NotToggle()
    {
        var state = new AppState();

        // 클릭 통과 선택 → 무장된 도구 해제.
        state.ActiveTool = ToolKind.Pen;
        state.ClickThrough = true;
        Assert.Equal(ToolKind.None, state.ActiveTool);
        Assert.True(state.ClickThrough);

        // 도구 선택 → 클릭 통과 해제.
        state.ActiveTool = ToolKind.Ellipse;
        Assert.False(state.ClickThrough);
        Assert.Equal(ToolKind.Ellipse, state.ActiveTool);
    }

    [Fact]
    public void SyncMode_EnablingUnifiesToActiveGroupStyle()
    {
        var state = new AppState();
        state.ActiveTool = ToolKind.Pen;
        state.CurrentColor = Black;
        state.Thickness = ThicknessStep.Large;

        state.SyncToolStyles = true; // 켜는 순간 활성(펜) 스타일로 통일

        Assert.Equal(Black, state.ColorOf(ToolStyleGroup.Highlighter));
        Assert.Equal(Black, state.ColorOf(ToolStyleGroup.Shape));
        Assert.Equal(ThicknessStep.Large, state.ThicknessOf(ToolStyleGroup.Highlighter));
        Assert.Equal(ThicknessStep.Large, state.ThicknessOf(ToolStyleGroup.Shape));
    }

    [Fact]
    public void SyncMode_ColorChange_PropagatesToAllGroups()
    {
        var state = new AppState { SyncToolStyles = true };
        state.ActiveTool = ToolKind.Highlighter;
        state.CurrentColor = Red;

        Assert.Equal(Red, state.ColorOf(ToolStyleGroup.Pen));
        Assert.Equal(Red, state.ColorOf(ToolStyleGroup.Highlighter));
        Assert.Equal(Red, state.ColorOf(ToolStyleGroup.Shape));
    }

    [Fact]
    public void StepThickness_ClampsAtFiveStepBounds()
    {
        var state = new AppState();
        state.ActiveTool = ToolKind.Pen;

        for (int i = 0; i < 10; i++)
        {
            state.StepThickness(+1);
        }
        Assert.Equal(ThicknessStep.XLarge, state.Thickness);

        for (int i = 0; i < 10; i++)
        {
            state.StepThickness(-1);
        }
        Assert.Equal(ThicknessStep.XSmall, state.Thickness);
    }

    [Fact]
    public void StrokeWidths_UseOwnGroupSteps()
    {
        var state = new AppState();
        state.SetThickness(ToolStyleGroup.Pen, ThicknessStep.XSmall);
        state.SetThickness(ToolStyleGroup.Highlighter, ThicknessStep.XLarge);
        state.SetThickness(ToolStyleGroup.Shape, ThicknessStep.Large);

        Assert.Equal(2, state.PenThickness);
        Assert.Equal(48, state.HighlighterThickness); // 16 * 3
        Assert.Equal(10, state.ShapeThickness);
        Assert.Equal(36, state.TextFontSize); // 도형 그룹 연동
    }

    [Fact]
    public void ChangedEvent_FiresOncePerStyleMutation()
    {
        var state = new AppState();
        int fired = 0;
        state.Changed += () => fired++;

        state.SetColor(ToolStyleGroup.Pen, Black);
        Assert.Equal(1, fired);

        state.SetColor(ToolStyleGroup.Pen, Black); // 동일 값 → 미발생
        Assert.Equal(1, fired);
    }
}
