namespace SSPen.Annotation;

/// <summary>
/// 서피스 마우스 다운 한 건이 무엇이 되는가 (ARCH-2 / D4). 좌표·버튼·수식키가 아니라
/// <b>도구와 게이트 상태만</b>으로 정해지는 부분을 표로 뽑은 것이며, 각 판정이
/// <c>Handled</c>를 세우는지는 <see cref="SurfaceInputRouter.MarksHandled"/>가 소유한다.
/// </summary>
public enum SurfaceGesture
{
    /// <summary>비인터랙티브 — 아무것도 하지 않고 <c>Handled</c>도 <b>세우지 않는다</b> (D4).</summary>
    Ignore,

    /// <summary>
    /// 텍스트 편집 중 바깥 클릭 → 확정 (Round 13).
    /// 이 클릭은 <b>소비되지 않는다</b> — 오늘도 <c>CommitText()</c> 뒤에 Handled를 세우지 않고
    /// 반환하므로, 확정과 동시에 클릭이 아래로 흘러가는 것이 기존 동작이다 (ARCH-2).
    /// </summary>
    CommitTextOnly,

    /// <summary>
    /// 도구 분기가 하나도 없지만 이벤트는 <b>삼킨다</b> (<see cref="ToolKind.None"/>).
    /// 오늘 <c>switch</c>에 <c>None</c> arm이 없어도 그 아래에서 참이 반환되므로,
    /// 이 행을 <see cref="Ignore"/>로 접으면 서피스가 오늘 삼키는 클릭을 흘려보낸다.
    /// 프로덕션에서는 도달 불가다 — <c>AppState.IsInteractive</c>가 <c>ActiveTool != None</c>을
    /// 포함하기 때문이다. 표를 전역 함수로 유지하기 위한 행이지 승인이 아니다 (D4).
    /// </summary>
    SwallowOnly,

    StartStroke,
    StartLine,
    StartArrow,
    StartRectangle,
    StartEllipse,
    BeginTextEdit,

    /// <summary>지우개: 클릭 삭제 + 드래그 래치 + 마우스 캡처.</summary>
    EraseAndDrag,

    BeginSelect,
}

/// <summary>
/// 서피스 휠 한 노치의 중재 결과 (R7 / WI-16).
/// <b>판정이 곧 <c>Handled</c>는 아니다</b> — <see cref="ScaleSelection"/>만 예외이며 이유는 그 멤버에 적었다.
/// </summary>
public enum WheelVerdict
{
    /// <summary>비인터랙티브이거나, 선택 도구가 아니면서 휠 굵기 조정이 꺼져 있다 — <c>Handled</c> 미대입.</summary>
    Ignore,

    /// <summary>
    /// 드래그 중 휠은 <b>삼키기만</b> 한다. 두 세션이 같은 요소를 동시에 잡으면 시작 상태
    /// 스냅샷이 둘로 갈라져, 마우스 업이 항목 1을 싣고 450ms 뒤 유휴 타이머가 항목 2를
    /// 더 실어 한 번의 드래그가 실행취소 2번이 된다 (그중 하나는 아무 일도 하지 않는 유령 스텝) (R7).
    /// </summary>
    SwallowOnly,

    /// <summary>
    /// 선택 크기 조절 <b>후보</b>. 이 판정만으로 <c>Handled</c>를 세우면 안 된다 —
    /// 모니터에 걸친 선택(SEL-LIM-5)은 호출부의 <see cref="SelectionGroup.HandlesGrabbable"/>
    /// 게이트에서 걸러지고, 그때 오늘 서피스는 휠을 <b>소비하지 않는다</b>.
    /// 게이트를 이 표로 끌어오지 않는 이유는 <see cref="SurfaceInputRouter.RouteWheel"/> 문서에 있다.
    /// </summary>
    ScaleSelection,

    /// <summary>마우스 휠로 펜 크기 조정 (WI-16 설정 연동) — 이 판정만 그 자체로 <c>Handled</c>다.</summary>
    StepThickness,
}

/// <summary>
/// 서피스 입력의 두 라우팅 표 (D4 / R7). 상태 머신이 아니라 <b>순수 표</b>이므로 헤드리스로 전수 검증된다.
///
/// 이 표가 고정하는 것은 <b>순서</b>다: 비인터랙티브 가드가 첫째, 텍스트 바깥 클릭 선점이
/// <b>도구 switch보다 먼저</b>(ARCH-2), 도구 분기가 마지막. 선점이 뒤로 가면 텍스트 편집 중
/// 펜 클릭이 새 획을 시작한 뒤에야 텍스트가 확정된다.
/// </summary>
public static class SurfaceInputRouter
{
    /// <summary>
    /// 마우스 다운 라우팅 (D4 / ARCH-2).
    ///
    /// <paramref name="overActiveEditor"/>는 <c>Point</c>에서 유도할 수 없는 WPF 히트테스트 입력이다
    /// (<c>TextBox.IsMouseOver</c>). 기하로 대체하면 헤드리스에서 <c>ActualWidth</c>가 0이라
    /// 프로덕션 동작이 바뀐다 (ARCH-2). <paramref name="textEditing"/>이 false면 무시된다.
    /// </summary>
    public static SurfaceGesture RouteDown(ToolKind tool, bool interactive, bool textEditing, bool overActiveEditor)
    {
        if (!interactive)
        {
            return SurfaceGesture.Ignore;
        }

        // 텍스트 편집 중 바깥 클릭 → 확정 (Round 13). 도구 switch보다 **먼저** (ARCH-2).
        if (textEditing && !overActiveEditor)
        {
            return SurfaceGesture.CommitTextOnly;
        }

        return tool switch
        {
            ToolKind.Pen or ToolKind.Highlighter => SurfaceGesture.StartStroke,
            ToolKind.Line => SurfaceGesture.StartLine,
            ToolKind.Arrow => SurfaceGesture.StartArrow,
            ToolKind.Rectangle => SurfaceGesture.StartRectangle,
            ToolKind.Ellipse => SurfaceGesture.StartEllipse,
            ToolKind.Text => SurfaceGesture.BeginTextEdit,
            ToolKind.Eraser => SurfaceGesture.EraseAndDrag,
            ToolKind.Select => SurfaceGesture.BeginSelect,
            _ => SurfaceGesture.SwallowOnly,
        };
    }

    /// <summary>
    /// 이 판정이 <c>Handled</c>를 세우는가. <see cref="SurfaceGesture.Ignore"/>와
    /// <see cref="SurfaceGesture.CommitTextOnly"/>만 false다 — 둘 다 오늘 Handled를 세우기 전에
    /// 반환하는 경로이며, 특히 후자를 true로 바꾸면 텍스트 확정 클릭이 조용히 소비된다 (ARCH-2).
    /// </summary>
    public static bool MarksHandled(SurfaceGesture gesture) =>
        gesture is not (SurfaceGesture.Ignore or SurfaceGesture.CommitTextOnly);

    /// <summary>
    /// 휠 중재 (R7 / SEL-5 / WI-16).
    ///
    /// R7: 선택 도구에서 선택집합이 있으면 휠은 <b>선택 크기 조절</b>이다.
    /// 원래 이 자리에서 굵기가 조정될 수 없었다 — SEL-5가 선택 도구의 스타일 쓰기를 차단하므로
    /// StepThickness가 조용히 무동작이었다. 즉 죽어 있던 입력을 되살리는 것이지 뺏는 것이 아니다.
    ///
    /// <b>미리 계산된 <c>grabbable</c>을 인자로 받지 않는다.</b> 받으면 드래그 중에도 매 휠 이벤트마다
    /// 소유 선택 필터가 O(n·m) 참조 동일성 스캔을 돌아 드래그 조기 반환이 사라지고,
    /// 무엇보다 <see cref="SelectionGroup.HandlesGrabbable"/>가 세 호출부의 단일 술어라는 규약
    /// (SEL-LIM-5)이 표 안에 사본을 하나 더 만든다. 게이트는 호출부에 남는다.
    /// </summary>
    public static WheelVerdict RouteWheel(ToolKind tool, bool interactive, bool dragActive, bool wheelAdjustsPenSize)
    {
        if (!interactive)
        {
            return WheelVerdict.Ignore;
        }
        if (tool == ToolKind.Select)
        {
            return dragActive ? WheelVerdict.SwallowOnly : WheelVerdict.ScaleSelection;
        }
        return wheelAdjustsPenSize ? WheelVerdict.StepThickness : WheelVerdict.Ignore;
    }
}
