using System.Reflection;
using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 히트 우선순위 사다리(SEL-7)의 순수 증인. 컨트롤러에서 내려온 4단 사다리가
/// <see cref="SelectionGesturePlanner.Plan"/> 하나로 옮겨졌으므로, 예전에는 창과 캔버스가 있어야
/// 물어볼 수 있던 질문들을 값으로 물어본다.
///
/// 서피스 경계는 실제 모니터 크기를 흉내 낸다 — <c>Rect.Empty</c>도 <c>(0,0,0,0)</c>도 아닌 값이라야
/// 회전 핸들 클램프(<see cref="TransformMath.ClampRotateHandle"/>)가 렌더와 같은 위치를 낸다 (R5).
/// </summary>
public class SelectionGesturePlannerTests
{
    private static readonly Rect Surface = new(0, 0, 1920, 1080);

    private static StrokeElement Stroke(double x, double y, double w, double h) =>
        new([new Point(x, y), new Point(x + w, y + h)], Colors.Red, 2, isHighlighter: false);

    /// <summary>선택집합 대역: 참조 동일성으로만 판정한다 (<c>SelectionModel.Contains</c>와 같은 의미).</summary>
    private static Func<AnnotationElement, bool> SelectedAmong(params AnnotationElement[] selected) =>
        element => Array.IndexOf(selected, element) >= 0;

    // ---- 1) 핸들 히트가 가장 먼저다 (SEL-7) ----

    /// <summary>
    /// 회전 핸들은 프레임 <b>바깥</b> 24px 위에 놓인다. 사다리 순서를 뒤집으면 이 점이 빈 곳으로
    /// 떨어져 회전 핸들이 영영 잡히지 않는다 (SEL-7의 원래 결함).
    /// </summary>
    [Fact]
    public void Plan_RotateHandleAboveFrame_BeatsEmptyArea()
    {
        var a = Stroke(100, 100, 50, 50);
        var b = Stroke(200, 200, 50, 50);
        var frame = SelectionGroup.Frame([a, b])!.Value;
        var handle = SelectionGroup.RotateHandle(frame);

        // 그 점에는 요소도 없고 프레임도 그를 품지 않는다 — 오직 핸들 분기만이 이 점을 잡는다.
        Assert.False(frame.Contains(handle));
        Assert.Null(SelectionGeometry.HitForSelect([a, b], handle, SelectionGestureRules.SelectHitTolerancePixels));

        var plan = SelectionGesturePlanner.Plan(
            [a, b], [a, b], selectionCount: 2, SelectedAmong(a, b), handle, shift: false, Surface);

        Assert.Equal(SelectionDragKind.GroupRotate, plan.Kind);
        Assert.Equal(GroupHandleKind.Rotate, plan.GroupHandle);
        Assert.True(plan.Captures);
    }

    /// <summary>
    /// SEL-LIM-5: 모니터에 걸친 선택 중 이 서피스가 <b>1개만</b> 소유하면 핸들은 그려지지도 잡히지도
    /// 않는다. 술어를 <c>owned.Count &gt;= 2</c>로 다시 유도하면 이 경우가 요소별 경로로 새어
    /// "보이지만 잡히지 않는 핸들"이 돌아온다 (AGENTS.md의 기록된 회귀).
    /// </summary>
    [Fact]
    public void Plan_SingleOwnedOfCrossMonitorPair_ReturnsNoHandle()
    {
        var mine = Stroke(100, 100, 50, 50);
        var handle = TransformMath.RotateHandleWorld(mine.TransformState, mine.LocalBounds);

        // 같은 점, 같은 소유 목록 — 다른 것은 전역 선택집합 개수뿐이다.
        var alone = SelectionGesturePlanner.Plan(
            [mine], [mine], selectionCount: 1, SelectedAmong(mine), handle, shift: false, Surface);
        var spanning = SelectionGesturePlanner.Plan(
            [mine], [mine], selectionCount: 2, SelectedAmong(mine), handle, shift: false, Surface);

        Assert.Equal(SelectionDragKind.Rotate, alone.Kind);
        Assert.Equal(HandleKind.Rotate, alone.Handle);

        Assert.Equal(SelectionDragKind.Marquee, spanning.Kind);
        Assert.Null(spanning.Handle);
        Assert.Null(spanning.GroupHandle);
    }

    /// <summary>
    /// 2개 이상 선택이 그룹 핸들을 빗나가면 <b>요소별 핸들로 내려가지 않는다</b> (SEL-7).
    /// 배타성은 두 겹으로 지켜진다 — 사다리의 <c>else if</c>와 두 분기의 개수 술어
    /// (<c>&gt;= MinGroupCount</c> / <c>== 1</c>) 다. 이 테스트가 지키는 것은 관측 가능한 계약,
    /// 즉 "그 클릭은 프레임 내부 이동이지 크기 조절이 아니다"이다.
    /// </summary>
    [Fact]
    public void Plan_MultiSelectionMissingGroupHandle_DoesNotHitPerElementHandle()
    {
        var a = Stroke(100, 100, 50, 50);
        var b = Stroke(200, 200, 50, 50);
        // a의 요소별 우상단 핸들 중심. 그룹 프레임(100,100)-(250,250)의 어떤 핸들과도 멀다.
        var perElement = new Point(a.LocalBounds.Right, a.LocalBounds.Top);
        Assert.NotNull(TransformMath.HitHandle(a.TransformState, a.LocalBounds, perElement, Surface));
        Assert.Null(SelectionGroup.HitHandle(SelectionGroup.Frame([a, b])!.Value, perElement, Surface));

        var plan = SelectionGesturePlanner.Plan(
            [a, b], [a, b], selectionCount: 2, SelectedAmong(a, b), perElement, shift: false, Surface);

        Assert.Equal(SelectionDragKind.Move, plan.Kind);
        Assert.Null(plan.Handle);
        Assert.Null(plan.Target);
    }

    // ---- R1: 동결 기준과 그려지는 프레임은 서로 다른 값이다 ----

    /// <summary>
    /// 등방 스케일은 <b>기준만</b> 동결하고 그려지는 프레임은 밀지 않는다. <c>rotating: true</c>로
    /// 한 글자만 바꾸면 마우스 업에서 프레임이 튀는 결함이 전 테스트 초록인 채 돌아온다 (R1).
    /// </summary>
    [Fact]
    public void Plan_GroupCornerHandle_FreezesBasisButNotDrawnFrame()
    {
        var a = Stroke(100, 100, 50, 50);
        var b = Stroke(200, 200, 50, 50);
        var frame = SelectionGroup.Frame([a, b])!.Value;
        var corner = SelectionGroup.CornerCenter(frame, GroupHandleKind.TopLeft);

        var plan = SelectionGesturePlanner.Plan(
            [a, b], [a, b], selectionCount: 2, SelectedAmong(a, b), corner, shift: false, Surface);

        Assert.Equal(SelectionDragKind.GroupScale, plan.Kind);
        Assert.Equal(GroupHandleKind.TopLeft, plan.GroupHandle);
        Assert.Equal(frame, plan.FrozenBasis!.Value);
        Assert.Null(plan.DrawnFrame);
    }

    /// <summary>회전은 기준과 그려지는 프레임을 <b>둘 다</b> 동결한다 (크기만 — 각도는 매 프레임 갱신).</summary>
    [Fact]
    public void Plan_GroupRotateHandle_FreezesBoth()
    {
        var a = Stroke(100, 100, 50, 50);
        var b = Stroke(200, 200, 50, 50);
        var frame = SelectionGroup.Frame([a, b])!.Value;

        var plan = SelectionGesturePlanner.Plan(
            [a, b], [a, b], selectionCount: 2, SelectedAmong(a, b),
            SelectionGroup.RotateHandle(frame), shift: false, Surface);

        Assert.Equal(SelectionDragKind.GroupRotate, plan.Kind);
        Assert.Equal(frame, plan.FrozenBasis!.Value);
        Assert.NotNull(plan.DrawnFrame);
    }

    /// <summary>
    /// 마우스 다운 시점의 그려지는 프레임은 <b>기준과 같은 크기·각도 0</b>이다.
    /// 0이 아니면 커서가 아직 움직이지 않았는데 가이드가 이미 돌아 있게 된다 (SEL-LIM-6).
    /// </summary>
    [Fact]
    public void Plan_GroupRotateHandle_DrawnFrameBoundsEqualFrozenBasis_AndAngleIsZero()
    {
        var a = Stroke(100, 100, 50, 50);
        var b = Stroke(200, 200, 50, 50);
        var frame = SelectionGroup.Frame([a, b])!.Value;

        var plan = SelectionGesturePlanner.Plan(
            [a, b], [a, b], selectionCount: 2, SelectedAmong(a, b),
            SelectionGroup.RotateHandle(frame), shift: false, Surface);

        Assert.NotNull(plan.DrawnFrame);
        Assert.NotNull(plan.FrozenBasis);
        Assert.Equal(plan.FrozenBasis!.Value, plan.DrawnFrame!.Value.Bounds);
        Assert.Equal(0d, plan.DrawnFrame.Value.AngleDegrees);
    }

    /// <summary>
    /// SEL-LIM-6 트립와이어: <see cref="GesturePlan.FrozenBasis"/>는 <b>각도 없는</b> <c>Rect?</c>여야 한다.
    /// 바로 옆 <see cref="GesturePlan.DrawnFrame"/>이 <c>GroupFrame?</c>이라 "왜 타입이 다르냐"며
    /// 합치고 싶어지는데, 합치면 각도가 <see cref="SelectionGroup.ScaleFactor"/>/<c>AnchorCorner</c>/
    /// 휠 경로로 새어 나간다. <c>SelectionGroupTests.Frame_ReturnType_IsAngleFreeRect_ByReflection</c>와
    /// 같은 모양의 감시선이다.
    /// </summary>
    [Fact]
    public void GesturePlan_FrozenBasisType_IsAngleFreeRect_ByReflection()
    {
        var property = typeof(GesturePlan).GetProperty(
            nameof(GesturePlan.FrozenBasis), BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(property);
        Assert.Equal(typeof(Rect?), property!.PropertyType);
    }

    // ---- 2) 프레임 내부 클릭 → 이동 (R6) ----

    /// <summary>
    /// 그룹 프레임 <b>안쪽 빈 자리</b>는 이동이다 — 요소가 없으므로 3)번 분기가 잡을 수 없고,
    /// 2)번 분기가 없으면 그대로 마퀴가 되어 선택이 날아간다. 2)번의 유일한 강한 증인이다.
    /// </summary>
    [Fact]
    public void Plan_GroupFrameInteriorEmptySpot_IsMoveNotMarquee()
    {
        var a = Stroke(400, 400, 50, 50);
        var b = Stroke(600, 600, 50, 50);
        var gap = new Point(500, 550);
        Assert.True(SelectionGroup.Frame([a, b])!.Value.Contains(gap));
        Assert.Null(SelectionGeometry.HitForSelect([a, b], gap, SelectionGestureRules.SelectHitTolerancePixels));

        var plan = SelectionGesturePlanner.Plan(
            [a, b], [a, b], selectionCount: 2, SelectedAmong(a, b), gap, shift: false, Surface);

        Assert.Equal(SelectionDragKind.Move, plan.Kind);
        Assert.Null(plan.SelectHit);
        Assert.True(plan.Captures);
    }

    /// <summary>
    /// 회전한 단일 선택도 안쪽을 잡아 옮길 수 있다 (R6) — 잉크 실선을 다시 겨냥할 필요가 없다.
    /// 커서는 잉크에서 멀리 떨어진 로컬 프레임 내부에 있다.
    /// </summary>
    [Fact]
    public void Plan_RotatedSingleSelection_InsideObbAwayFromInk_IsMove()
    {
        var a = Stroke(100, 100, 50, 50);
        a.TransformState = a.TransformState with { AngleDegrees = 45 };
        // 회전 후 잉크는 중심을 지나는 수직선이 된다. 그 옆 15px는 여전히 OBB 내부다.
        var inside = new Point(140, 125);
        Assert.True(SelectionGeometry.ContainsInFrame(a, inside));
        Assert.False(a.HitTest(inside, SelectionGestureRules.SelectHitTolerancePixels));

        var plan = SelectionGesturePlanner.Plan(
            [a], [a], selectionCount: 1, SelectedAmong(a), inside, shift: false, Surface);

        Assert.Equal(SelectionDragKind.Move, plan.Kind);
        Assert.Null(plan.SelectHit);
    }

    /// <summary>
    /// 프레임 안이라도 <b>선택되지 않은 다른 요소</b> 위라면 3)번에 양보한다 (R6) —
    /// 안 그러면 큰 선택 하나가 그 안의 모든 요소를 영구히 가린다.
    /// </summary>
    [Fact]
    public void Plan_UnselectedElementInsideFrame_SelectsInsteadOfMoving()
    {
        var big = Stroke(100, 100, 50, 50);
        var small = Stroke(120, 120, 4, 4);
        var pos = new Point(122, 122);
        Assert.True(SelectionGeometry.ContainsInFrame(big, pos));

        var plan = SelectionGesturePlanner.Plan(
            [big, small], [big], selectionCount: 1, SelectedAmong(big), pos, shift: false, Surface);

        Assert.Equal(SelectionDragKind.Move, plan.Kind);
        Assert.Same(small, plan.SelectHit);
    }

    /// <summary>Shift는 토글 의도이므로 프레임 내부 이동 분기를 건너뛴다 (SEL-AC-3).</summary>
    [Fact]
    public void Plan_ShiftClickInsideGroupFrame_TogglesInsteadOfMoving()
    {
        var a = Stroke(100, 100, 50, 50);
        var b = Stroke(200, 200, 50, 50);
        var onInk = new Point(125, 125);

        var plan = SelectionGesturePlanner.Plan(
            [a, b], [a, b], selectionCount: 2, SelectedAmong(a, b), onInk, shift: true, Surface);

        Assert.Equal(SelectionDragKind.None, plan.Kind);
        Assert.Same(a, plan.ToggleHit);
        Assert.False(plan.Captures);
    }

    // ---- 3) 요소 히트 ----

    /// <summary>이미 선택된 요소를 집으면 <see cref="GesturePlan.SelectHit"/>은 null이다 (R6).</summary>
    [Fact]
    public void Plan_HitIsSelectedElementInsideFrame_ReturnsMove()
    {
        var a = Stroke(100, 100, 50, 50);

        var plan = SelectionGesturePlanner.Plan(
            [a], [a], selectionCount: 1, SelectedAmong(a), new Point(125, 125), shift: false, Surface);

        Assert.Equal(SelectionDragKind.Move, plan.Kind);
        Assert.Null(plan.SelectHit);
    }

    /// <summary>선택이 없을 때 요소를 집으면 그 요소로 <b>교체</b>한 뒤 이동을 준비한다.</summary>
    [Fact]
    public void Plan_HitIsUnselectedElementInsideFrame_ReturnsSelect()
    {
        var a = Stroke(100, 100, 50, 50);

        var plan = SelectionGesturePlanner.Plan(
            [a], [], selectionCount: 0, SelectedAmong(), new Point(125, 125), shift: false, Surface);

        Assert.Equal(SelectionDragKind.Move, plan.Kind);
        Assert.Same(a, plan.SelectHit);
        Assert.True(plan.Captures);
    }

    /// <summary>
    /// R6: 프레임 <b>바깥</b> 허용 오차 안에서 구성원 하나를 집어도 다중 선택은 무너지지 않는다.
    /// <c>SelectHit</c>을 무조건 채우면 여기서 선택이 1개로 접힌다.
    /// </summary>
    [Fact]
    public void Plan_ClickOnAlreadySelectedMemberOutsideFrame_DoesNotCollapseSelection()
    {
        var a = new StrokeElement(
            [new Point(100, 100), new Point(200, 100)], Colors.Red, 2, isHighlighter: false);
        var b = Stroke(300, 300, 50, 50);
        var frame = SelectionGroup.Frame([a, b])!.Value;
        var justOutside = new Point(150, 96);
        Assert.False(frame.Contains(justOutside));
        Assert.Null(SelectionGroup.HitHandle(frame, justOutside, Surface));

        var plan = SelectionGesturePlanner.Plan(
            [a, b], [a, b], selectionCount: 2, SelectedAmong(a, b), justOutside, shift: false, Surface);

        Assert.Equal(SelectionDragKind.Move, plan.Kind);
        Assert.Null(plan.SelectHit);
    }

    /// <summary>Shift+요소는 토글만 한다 — 캡처도, 드래그 종류도 없다 (적용부의 스냅샷 게이트가 Kind다).</summary>
    [Fact]
    public void Plan_ShiftHitOnElement_ProducesToggleWithoutCaptureOrSnapshot()
    {
        var a = Stroke(100, 100, 50, 50);

        var plan = SelectionGesturePlanner.Plan(
            [a], [a], selectionCount: 1, SelectedAmong(a), new Point(125, 125), shift: true, Surface);

        Assert.Same(a, plan.ToggleHit);
        Assert.Equal(SelectionDragKind.None, plan.Kind);
        Assert.False(plan.Captures);
        Assert.False(plan.StartsMarquee);
        Assert.Null(plan.SelectHit);
    }

    // ---- 4) 빈 곳 ----

    /// <summary>
    /// 빈 곳은 마퀴를 시작하고 선택을 비운다. 클릭 통과 전환은 <b>계획에 없다</b> —
    /// 다운에서 켜면 IsInteractive가 떨어져 막 시작한 마퀴가 얼어붙으므로, 판정은 마우스 업이 맡는다 (R2/R5).
    /// </summary>
    [Fact]
    public void Plan_EmptyArea_StartsMarqueeAndNeverEngagesClickThrough()
    {
        var a = Stroke(100, 100, 50, 50);

        var plan = SelectionGesturePlanner.Plan(
            [a], [a], selectionCount: 1, SelectedAmong(a), new Point(900, 900), shift: false, Surface);

        Assert.Equal(SelectionDragKind.Marquee, plan.Kind);
        Assert.True(plan.StartsMarquee);
        Assert.True(plan.ClearSelection);
        Assert.True(plan.HadSelectionOnPress); // 업에서 클릭 통과를 판정할 걸쇠일 뿐이다
        Assert.True(plan.Captures);
    }

    /// <summary>
    /// Shift+빈 곳은 <b>누적 의도</b>다 — 선택을 비우지도, 클릭 통과 걸쇠를 세우지도 않는다 (R2/R5).
    /// 걸쇠를 무조건 세우면 Shift로 더하려다 제자리에서 뗀 순간 도구가 해제된다.
    /// </summary>
    [Fact]
    public void Plan_ShiftEmptyArea_DoesNotLatchHadSelection()
    {
        var a = Stroke(100, 100, 50, 50);
        var b = Stroke(200, 200, 50, 50);

        var plan = SelectionGesturePlanner.Plan(
            [a, b], [a, b], selectionCount: 2, SelectedAmong(a, b), new Point(900, 900), shift: true, Surface);

        Assert.Equal(SelectionDragKind.Marquee, plan.Kind);
        Assert.False(plan.ClearSelection);
        Assert.False(plan.HadSelectionOnPress);
        Assert.True(plan.StartsMarquee);
    }

    /// <summary>
    /// 선택이 빈 상태의 첫 빈 곳 클릭. 2)번 분기의 <c>owned.Count &gt; 0</c>이 먼저 끊지 않으면
    /// <c>IsInsideSelectionFrame</c>이 <c>owned[0]</c>을 읽어 즉시 <see cref="ArgumentOutOfRangeException"/>이다 —
    /// 모든 필드를 먼저 계산하고 나중에 고르는 구현으로 바꾸면 여기서 터진다 (R6).
    /// </summary>
    [Fact]
    public void Plan_EmptyArea_NoSelection_DoesNotIndexOwnedZero()
    {
        var plan = SelectionGesturePlanner.Plan(
            [], [], selectionCount: 0, SelectedAmong(), new Point(900, 900), shift: false, Surface);

        Assert.Equal(SelectionDragKind.Marquee, plan.Kind);
        Assert.False(plan.HadSelectionOnPress); // 선택이 없었으므로 클릭 통과로 넘어가지 않는다
        Assert.True(plan.ClearSelection);
    }
}
