using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

using static SSPen.Tests.TestGeometry;

namespace SSPen.Tests;

/// <summary>
/// 선택집합 수명과 해제 트리거 (SEL-6, SEL-B-4, f11, f12).
/// 핵심 계약 둘:
/// (1) 해제는 <c>ActiveToolChanged</c> **전용 이벤트**만 트리거한다 — 색·굵기·보드 변경은 아니다.
/// (2) 억제 스코프는 이관 구간에만 적용되고, 진짜 제거는 여전히 선택집합에서 떨어진다 (LD-5 양방향).
///
/// 억제 스코프 레드팀(C1)은 리팩터링 19단계에서 SelectionRedTeamTests로부터 글자 그대로 옮겨 LD-5 절 뒤에
/// 두었다. 헬퍼 <c>NewStroke</c>는 <see cref="TestGeometry"/>로 승격했다.
/// </summary>
public class SelectionModelTests
{

    // ---- 기본 조작 ----

    [Fact]
    public void Set_ReplacesSelectionAndRaisesChangedOnce()
    {
        var selection = new SelectionModel();
        var a = NewStroke();
        int raised = 0;
        selection.SelectionChanged += () => raised++;

        selection.Set([a]);

        Assert.Equal([a], selection.Elements);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Set_WithIdenticalContent_DoesNotRaiseChanged()
    {
        var selection = new SelectionModel();
        var a = NewStroke();
        selection.Set([a]);
        int raised = 0;
        selection.SelectionChanged += () => raised++;

        selection.Set([a]);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Toggle_ShiftClickOnNewElement_AddsToSelection()
    {
        var selection = new SelectionModel();
        var a = NewStroke();
        var b = NewStroke();
        selection.Set([a]);

        selection.Toggle(b);

        Assert.Equal([a, b], selection.Elements);
    }

    [Fact]
    public void Toggle_OnSelectedElement_RemovesFromSelection()
    {
        var selection = new SelectionModel();
        var a = NewStroke();
        var b = NewStroke();
        selection.Set([a, b]);

        selection.Toggle(a);

        Assert.Equal([b], selection.Elements);
    }

    [Fact]
    public void Add_AlreadySelectedElement_IsNoOp()
    {
        var selection = new SelectionModel();
        var a = NewStroke();
        selection.Set([a]);
        int raised = 0;
        selection.SelectionChanged += () => raised++;

        selection.Add(a);

        Assert.Single(selection.Elements);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Clear_ClickOnEmptySpace_EmptiesSelection()
    {
        var selection = new SelectionModel();
        selection.Set([NewStroke(), NewStroke()]);

        selection.Clear();

        Assert.Empty(selection.Elements);
    }

    [Fact]
    public void Clear_WhenAlreadyEmpty_DoesNotRaiseChanged()
    {
        var selection = new SelectionModel();
        int raised = 0;
        selection.SelectionChanged += () => raised++;

        selection.Clear();

        Assert.Equal(0, raised);
    }

    // ---- 해제 트리거: ActiveToolChanged 전용 (SEL-B-4) ----

    [Fact]
    public void ActiveToolTransition_ToAnyOtherTool_ClearsSelection()
    {
        var state = new AppState { ActiveTool = ToolKind.Select };
        var selection = new SelectionModel();
        selection.AttachTo(state);
        selection.Set([NewStroke()]);

        state.ActiveTool = ToolKind.Pen;

        Assert.Empty(selection.Elements);
    }

    [Fact]
    public void ActiveToolTransition_ToNone_ClearsSelection()
    {
        var state = new AppState { ActiveTool = ToolKind.Select };
        var selection = new SelectionModel();
        selection.AttachTo(state);
        selection.Set([NewStroke()]);

        state.ActiveTool = ToolKind.None;

        Assert.Empty(selection.Elements);
    }

    [Fact]
    public void ColorChange_DoesNotClearSelection()
    {
        // SEL-AC-17 / R6: AppState.Changed에 구독했다면 여기서 선택이 날아간다.
        var state = new AppState { ActiveTool = ToolKind.Pen };
        var selection = new SelectionModel();
        selection.AttachTo(state);
        var element = NewStroke();
        selection.Set([element]);

        state.SetColor(ToolStyleGroup.Pen, Colors.Magenta);

        Assert.Contains(element, selection.Elements);
    }

    [Fact]
    public void ThicknessChange_DoesNotClearSelection()
    {
        var state = new AppState { ActiveTool = ToolKind.Pen };
        var selection = new SelectionModel();
        selection.AttachTo(state);
        var element = NewStroke();
        selection.Set([element]);

        state.SetThickness(ToolStyleGroup.Pen, ThicknessStep.XLarge);

        Assert.Contains(element, selection.Elements);
    }

    [Fact]
    public void BoardChange_DoesNotClearSelection()
    {
        var state = new AppState { ActiveTool = ToolKind.Select };
        var selection = new SelectionModel();
        selection.AttachTo(state);
        var element = NewStroke();
        selection.Set([element]);

        state.ToggleBoard(BoardMode.White);
        state.SurfacesVisible = false;
        state.HaloActive = true;

        Assert.Contains(element, selection.Elements);
    }

    [Fact]
    public void ClickThrough_WhileSelectToolActive_ClearsSelection()
    {
        // ARCH-02/CRIT-01: ClickThrough setter가 백킹 필드를 직접 건드리면 여기서 선택이 살아남아
        // 조작 불가능한 고아 장식이 된다. SetActiveTool 단일 헬퍼가 이를 구조적으로 막는다.
        var state = new AppState { ActiveTool = ToolKind.Select };
        var selection = new SelectionModel();
        selection.AttachTo(state);
        selection.Set([NewStroke()]);

        state.ClickThrough = true;

        Assert.Equal(ToolKind.None, state.ActiveTool);
        Assert.Empty(selection.Elements);
    }

    // ---- LD-5 억제 스코프: 양방향 ----

    [Fact]
    public void Transfer_RemoveThenAdd_KeepsSelection()
    {
        // 이관 절차(Remove → 보정 → Add)를 억제 스코프로 감싸면 선택이 살아남는다 (SEL-AC-5).
        var source = new AnnotationDocument("M1");
        var target = new AnnotationDocument("M2");
        var selection = new SelectionModel();
        selection.AttachTo(source);
        selection.AttachTo(target);

        var element = NewStroke();
        source.Add(element);
        selection.Set([element]);

        using (selection.SuppressInvalidation())
        {
            source.Remove(element);
            element.TransformState = TransformMath.Translate(element.TransformState, new Vector(1920, 0));
            target.Add(element);
        }

        Assert.True(selection.Contains(element));
        Assert.Contains(element, target.Elements);
    }

    [Fact]
    public void ElementRemovedFromDocument_DropsFromSelection()
    {
        // LD-5 **반대 방향**: 억제가 과잉 적용되지 않음을 증명한다.
        // 지우개·undo-of-Add·페이드 소멸은 스코프 밖이므로 여전히 선택집합에서 떨어져야 한다 (R17).
        var document = new AnnotationDocument("M1");
        var selection = new SelectionModel();
        selection.AttachTo(document);

        var element = NewStroke();
        document.Add(element);
        selection.Set([element]);

        document.Remove(element);

        Assert.False(selection.Contains(element), "진짜 제거는 선택집합에서 떨어져야 한다.");
        Assert.Empty(selection.Elements);
    }

    [Fact]
    public void SuppressInvalidation_AfterScopeEnds_ResumesDropping()
    {
        var document = new AnnotationDocument("M1");
        var selection = new SelectionModel();
        selection.AttachTo(document);
        var element = NewStroke();
        document.Add(element);
        selection.Set([element]);

        using (selection.SuppressInvalidation())
        {
            // 스코프 안에서는 억제된다.
        }

        document.Remove(element);

        Assert.False(selection.Contains(element));
    }

    [Fact]
    public void SuppressInvalidation_Nested_RequiresAllScopesToEnd()
    {
        var document = new AnnotationDocument("M1");
        var selection = new SelectionModel();
        selection.AttachTo(document);
        var kept = NewStroke();
        var dropped = NewStroke();
        document.Add(kept);
        document.Add(dropped);
        selection.Set([kept, dropped]);

        var outer = selection.SuppressInvalidation();
        var inner = selection.SuppressInvalidation();
        inner.Dispose();
        document.Remove(kept); // 아직 바깥 스코프가 살아 있다.
        outer.Dispose();
        document.Remove(dropped);

        Assert.True(selection.Contains(kept));
        Assert.False(selection.Contains(dropped));
    }

    [Fact]
    public void SuppressInvalidation_DisposedTwice_DoesNotUnbalanceDepth()
    {
        var document = new AnnotationDocument("M1");
        var selection = new SelectionModel();
        selection.AttachTo(document);
        var element = NewStroke();
        document.Add(element);
        selection.Set([element]);

        var scope = selection.SuppressInvalidation();
        scope.Dispose();
        scope.Dispose();

        document.Remove(element);

        Assert.False(selection.Contains(element), "이중 Dispose가 억제 깊이를 음수로 만들면 안 된다.");
    }

    // ---- 레드팀 C1. 억제 스코프 양방향 — 이관 도중 다른 요소의 진짜 제거는 억제되지 않는다 (리팩터링 19단계, SelectionRedTeamTests에서 이동) ----

    [Fact]
    public void SuppressInvalidation_DuringTransferScope_DoesNotShieldUnrelatedRealRemoval()
    {
        // 스코프가 "이관 중인 그 요소"뿐 아니라 "그 시점의 모든 제거"를 억제하는 설계이므로,
        // 스코프 안에서 벌어지는 무관한 진짜 삭제(지우개)도 억제된다 — 이것이 R22가 지목한
        // "과잉 적용"의 실제 공격 표면이다. 프로덕션 이관 절차는 단일 Remove/Add쌍만 스코프
        // 안에 두므로 안전하지만, 이 테스트는 그 경계가 얼마나 좁은지 정량 확인한다.
        var doc = new AnnotationDocument("M1");
        var selection = new SelectionModel();
        selection.AttachTo(doc);

        var transferring = NewStroke(new Point(0, 0), new Point(10, 10));
        var unrelated = NewStroke(new Point(20, 20), new Point(30, 30));
        doc.Add(transferring);
        doc.Add(unrelated);
        selection.Set([transferring, unrelated]);

        using (selection.SuppressInvalidation())
        {
            doc.Remove(transferring); // "이관"으로 의도된 제거
            doc.Remove(unrelated);    // 스코프 안에서 벌어진 무관한 진짜 삭제 (지우개 시뮬레이션)
        }

        // 현재 구현 계약: 스코프는 깊이 기반이라 스코프 안의 모든 제거를 억제한다.
        // 이것이 계획서가 명시한 "적용 지점은 이관 2곳뿐" 원칙과 정확히 부합하는지 확인 —
        // 만약 프로덕션 코드가 이 스코프 안에 무관한 Remove를 끼워 넣으면 R22가 현실화된다.
        Assert.True(selection.Contains(unrelated),
            "스코프가 무관한 제거까지 억제함을 확인 — 프로덕션은 스코프를 좁게(단일 이관 쌍) 유지해야 한다 (R22 경계 문서화).");
    }

    [Fact]
    public void ElementRemovedFromDocument_EraserDuringActiveSelection_DropsOnlyErasedElement()
    {
        var doc = new AnnotationDocument("M1");
        var selection = new SelectionModel();
        selection.AttachTo(doc);

        var kept = NewStroke(new Point(0, 0), new Point(10, 10));
        var erased = NewStroke(new Point(20, 20), new Point(30, 30));
        doc.Add(kept);
        doc.Add(erased);
        selection.Set([kept, erased]);

        doc.Remove(erased); // 스코프 밖 — 진짜 지우개 삭제.

        Assert.True(selection.Contains(kept));
        Assert.False(selection.Contains(erased));
    }
}
