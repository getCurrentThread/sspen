using System.Windows;

namespace SSPen.Annotation;

/// <summary>
/// 선택 도구 드래그 상태 (SEL-7). 마퀴 / 이동 / 단일 요소 크기·회전 / 그룹 등방 스케일·회전이
/// 서로 배타적인 다섯 갈래로 갈린다.
/// </summary>
public enum SelectionDragKind
{
    None,
    Marquee,
    Move,

    /// <summary>단일 선택 전용: 요소 로컬 축 기준 <b>비등방</b> 크기 조절 (8핸들).</summary>
    Scale,

    /// <summary>단일 선택 전용: 요소 자기 중심 회전.</summary>
    Rotate,

    /// <summary>다중 선택: 그룹 프레임 대각 앵커 기준 <b>등방</b> 확대/축소 (R1).</summary>
    GroupScale,

    /// <summary>다중 선택: 그룹 프레임 중심 기준 회전 (R1).</summary>
    GroupRotate,
}

/// <summary>
/// 마우스 다운 <b>한 번의 판정 결과</b> (SEL-7). 히트 우선순위 사다리가 내린 결론을 값 하나로 들고,
/// <c>SurfaceInputController.BeginSelectGesture</c>는 이 값을 <b>고정된 순서</b>로 옮겨 적기만 한다.
///
/// 위치 인자가 아니라 init 속성인 이유: 필드가 열둘이고 네 갈래(핸들 / 프레임 내부 이동 / 요소 히트 /
/// 빈 곳)가 저마다 <b>다른 부분집합</b>만 채우므로, 위치 인자 열둘은 조용히 어긋난 생성을 쉽게 만든다.
/// </summary>
public readonly record struct GesturePlan
{
    /// <summary>이 마우스 다운이 시작하는 드래그 종류. 토글(<see cref="ToggleHit"/>)일 때만 <c>None</c>이다.</summary>
    public SelectionDragKind Kind { get; init; }

    /// <summary>잡힌 요소별 8핸들 (<see cref="SelectionDragKind.Scale"/>/<see cref="SelectionDragKind.Rotate"/>에서만).</summary>
    public HandleKind? Handle { get; init; }

    /// <summary>잡힌 그룹 핸들 (<see cref="SelectionDragKind.GroupScale"/>/<see cref="SelectionDragKind.GroupRotate"/>에서만).</summary>
    public GroupHandleKind? GroupHandle { get; init; }

    /// <summary>
    /// 요소별 핸들 드래그의 대상. 그 외 갈래에서는 null이며, 그대로
    /// <see cref="DragBaseStates.Snapshot"/>의 핸들 대상 인자로 흐른다.
    /// </summary>
    public AnnotationElement? Target { get; init; }

    /// <summary>
    /// 제스처 내내 <b>동결</b>될 그룹 프레임 (R1) — 배율·피벗 계산의 기준값이다. 살아있는 경계로
    /// 매 프레임 재계산하면 회전 중 피벗이 표류하고 잡은 핸들이 커서 밑에서 빠져나간다.
    ///
    /// 각도는 여기에 <b>싣지 않는다</b> — 실으면 <see cref="SelectionGroup.ScaleFactor"/>/
    /// <c>AnchorCorner</c>/휠 경로로 새어 나간다. 그려지는 프레임의 각도는 창으로만 흐른다 (SEL-LIM-6).
    /// 그래서 <see cref="DrawnFrame"/>과 <b>타입이 다르다</b> — 두 필드를 한 타입으로 합치는 순간
    /// 위 세 경로가 각도를 보게 된다.
    /// </summary>
    public Rect? FrozenBasis { get; init; }

    /// <summary>
    /// 창에 밀어 넣을 <b>그려지는</b> 프레임. 그려지는 프레임을 미는 것은 <b>회전뿐</b>이다.
    /// 회전은 축 정렬 합집합을 부풀려 잡은 핸들이 커서 밑에서 빠져나가지만, 등방 스케일은 축 정렬
    /// 사상이라 살아있는 합집합이 그대로 정답이다. 오히려 동결하면 구성원 배율이 바닥에 클램프될 때
    /// 이상적 사상과 실제 합집합이 어긋나 마우스 업에서 프레임이 튄다.
    /// (<see cref="FrozenBasis"/> 자체는 배율·피벗의 기준이므로 두 경우 모두 동결한다.)
    /// 밀어 넣는 것은 <b>크기뿐</b>이고, 각도는 <c>SurfaceInputController.UpdateSelectGesture</c>가
    /// 매 프레임 갱신한다.
    ///
    /// 이 값은 <see cref="SelectionGroup.GestureFrame"/> <b>한 번의 호출</b>에서만 나온다 —
    /// 판정을 적용부로 내리면 회전 여부에 헤드리스 증인이 사라진다 (R1, SEL-LIM-6).
    /// </summary>
    public GroupFrame? DrawnFrame { get; init; }

    /// <summary>
    /// Shift 토글 대상. Shift 토글은 이동을 시작하지 않는다 —
    /// 캡처도 스냅샷도 하지 않고 즉시 끝난다 (SEL-AC-3).
    /// </summary>
    public AnnotationElement? ToggleHit { get; init; }

    /// <summary>
    /// 단일 선택으로 교체할 요소. 반드시 스냅샷 <b>전</b>에 적용한다 —
    /// <see cref="DragBaseStates.Snapshot"/>이 <c>selection.Elements</c>를 훑으므로, 뒤집으면 방금 고른
    /// 요소의 시작 상태가 비어 이동이 조용히 무동작이 된다 (SEL-AC-9).
    ///
    /// 이미 선택된 요소를 집었을 때는 null이다 — 무조건 <c>Set</c>하면 프레임 바깥 허용 오차 안에서
    /// 구성원 하나를 클릭했을 때 다중 선택이 통째로 무너진다 (R6).
    /// </summary>
    public AnnotationElement? SelectHit { get; init; }

    /// <summary>선택집합을 비울 것인가 (빈 곳 + Shift 아님).</summary>
    public bool ClearSelection { get; init; }

    /// <summary>
    /// 빈 곳 제스처를 시작할 때 선택이 있었는가 (R5: 제자리 클릭이면 업에서 클릭 통과로 전환).
    /// Shift+빈 곳은 <b>누적 의도</b>다 — 기존 선택을 유지한 채 마퀴로 더하려는 것이므로
    /// 제자리에서 뗐다고 해제·클릭 통과로 넘어가면 안 된다 (R2).
    ///
    /// 선택을 비우기 <b>전</b>의 개수로 판정한다. 플래너가 인자 <c>selectionCount</c>로 계산하므로
    /// 적용부에서는 <c>Clear</c>와의 순서가 문제되지 않는다.
    /// </summary>
    public bool HadSelectionOnPress { get; init; }

    /// <summary>마퀴 미리보기를 시작할 것인가. <see cref="HadSelectionOnPress"/> 걸쇠도 이 갈래에서만 세운다.</summary>
    public bool StartsMarquee { get; init; }

    /// <summary>마우스 캡처를 잡을 것인가. 토글(<see cref="ToggleHit"/>)만 false다.</summary>
    public bool Captures { get; init; }
}

/// <summary>
/// 히트 우선순위 (SEL-7): 핸들 → 요소 → 빈 곳. 핸들이 먼저인 이유는 핸들이 요소 경계
/// <b>바깥</b>에도 놓이기 때문이다 — 순서를 뒤집으면 빈 곳 분기가 회전 핸들을 가로채 잡힌 적이 없게 된다.
///
/// 선택집합·문서·창을 만지지 않는 순수 판정이라 헤드리스 유닛 테스트 대상이다.
/// </summary>
public static class SelectionGesturePlanner
{
    /// <summary>
    /// 마우스 다운 한 번을 <see cref="GesturePlan"/>으로 옮긴다. 사다리는 <b>한 줄기 직선</b>이어야 한다 —
    /// 갈래마다 별도 해석 함수로 쪼개면 아래 if / else if 배타성이 독립된 두 if로 무너져,
    /// 그룹 핸들을 빗나간 2개 이상 선택이 <c>owned[0]</c>의 요소별 핸들을 잡게 된다 (SEL-7).
    ///
    /// 전제: 호출부는 이 함수 <b>전에</b> 제스처 프레임을 null로 밀어 둔 상태여야 한다
    /// (<c>SurfaceInputController.BeginSelectGesture</c> 머리). 그래야 아래 핸들 히트, R6 내부 판정,
    /// 휠 고정점이 전부 화면에 그려진 것과 같은 축 정렬 프레임을 본다.
    /// 순수 함수라 이 전제를 스스로 강제할 수 없다 (SEL-LIM-6).
    /// </summary>
    /// <param name="docElements">이 서피스 문서의 요소 (요소 히트 대상).</param>
    /// <param name="owned">이 서피스가 소유한 선택 요소 (<see cref="SelectionGroup.OwnedBy"/>).</param>
    /// <param name="selectionCount">전역 선택집합 개수 — 소유 개수와 함께 SEL-LIM-5 술어의 입력이다.</param>
    /// <param name="isSelected">요소가 선택집합에 있는가 (<c>SelectionModel.Contains</c> 메서드 그룹).</param>
    /// <param name="pos">서피스 논리 좌표의 커서.</param>
    /// <param name="shift">D3: 어댑터가 <c>KeyboardState</c>로 이벤트당 한 번 읽어 흘려준 값.</param>
    /// <param name="surfaceBounds">회전 핸들 클램프용 서피스 논리 경계 — 렌더와 같은 값이어야 한다 (R5).</param>
    public static GesturePlan Plan(
        IReadOnlyList<AnnotationElement> docElements,
        IReadOnlyList<AnnotationElement> owned,
        int selectionCount,
        Func<AnnotationElement, bool> isSelected,
        Point pos,
        bool shift,
        Rect surfaceBounds)
    {
        // 모니터에 걸친 선택은 이동만 허용한다 (SEL-LIM-5): 서피스마다 원점과 DPI가 달라
        // 두 문서의 논리 경계를 합친 그룹 프레임은 서로소인 좌표계의 합이라 의미가 없다.
        // 술어는 렌더(ContentSurfaceWindow.RedrawDecorations)와 **같은 함수**를 쓴다 — 이름을 따로
        // 두면 "그리는 조건"과 "잡히는 조건"이 다시 갈라져 보이지만 잡히지 않는 핸들이 생긴다.
        bool grabbable = SelectionGroup.HandlesGrabbable(owned.Count, selectionCount);

        // 1) 핸들 히트 — 핸들이 프레임 **바깥**에도 놓이므로 반드시 가장 먼저다 (SEL-7).
        if (grabbable && owned.Count >= SelectionGroup.MinGroupCount)
        {
            if (SelectionGroup.Frame(owned) is { } frame
                && SelectionGroup.HitHandle(frame, pos, surfaceBounds) is { } groupHandle)
            {
                var kind = groupHandle == GroupHandleKind.Rotate
                    ? SelectionDragKind.GroupRotate
                    : SelectionDragKind.GroupScale;
                return new GesturePlan
                {
                    Kind = kind,
                    GroupHandle = groupHandle,
                    FrozenBasis = frame,
                    DrawnFrame = SelectionGroup.GestureFrame(
                        frame, kind == SelectionDragKind.GroupRotate, deltaDegrees: 0),
                    Captures = true,
                };
            }
        }
        else if (grabbable && owned.Count == 1)
        {
            // **else if**가 계약이다: 독립된 두 if로 풀면 그룹 핸들을 빗나간 2개 이상 선택이 여기로
            // 떨어져 owned[0]의 요소별 핸들을 잡는다 (SEL-7).
            var candidate = owned[0];
            if (TransformMath.HitHandle(candidate.TransformState, candidate.LocalBounds, pos, surfaceBounds)
                is { } handle)
            {
                return new GesturePlan
                {
                    Kind = handle == HandleKind.Rotate ? SelectionDragKind.Rotate : SelectionDragKind.Scale,
                    Handle = handle,
                    Target = candidate,
                    Captures = true,
                };
            }
        }

        // 커서 밑의 요소를 **먼저** 구한다 — 아래 두 분기가 같은 값을 봐야 우선순위가 일관된다.
        var hit = SelectionGeometry.HitForSelect(docElements, pos, SelectionGestureRules.SelectHitTolerancePixels);

        // 2) 선택 프레임 **내부** 클릭 → 이동 (R6). 이미 선택한 것을 옮기려고 잉크 실선을 다시
        //    정확히 겨냥할 필요가 없어진다. Shift는 토글 의도이므로 이 분기를 건너뛴다.
        //    단, 프레임 안이라도 **선택되지 않은 다른 요소** 위라면 3번에 양보한다 — 안 그러면
        //    큰 선택 하나가 그 프레임 안의 모든 요소를 영구히 가려 다시는 고를 수 없다.
        //    항의 순서도 계약이다: owned.Count > 0이 먼저 끊어야 IsInsideSelectionFrame의
        //    owned[0] 접근이 범위 안에 남는다 (선택이 빈 상태로 빈 곳을 처음 클릭할 때).
        if (!shift
            && owned.Count > 0
            && SelectionGestureRules.ShouldMoveFromFrameInterior(
                IsInsideSelectionFrame(owned, pos), hit is not null, hit is not null && isSelected(hit)))
        {
            return new GesturePlan { Kind = SelectionDragKind.Move, Captures = true };
        }

        // 3) 요소 히트 — Shift는 토글(SEL-AC-3), 그 외는 단일 선택 후 이동 준비.
        //    R6: 잉크 정확 히트 우선 → 없으면 경계 상자 내부 중 면적 최소.
        if (hit is not null)
        {
            if (shift)
            {
                return new GesturePlan { Kind = SelectionDragKind.None, ToggleHit = hit };
            }
            return new GesturePlan
            {
                Kind = SelectionDragKind.Move,
                SelectHit = isSelected(hit) ? null : hit,
                Captures = true,
            };
        }

        // 4) 빈 곳 — Shift가 아니면 해제하고 마퀴를 시작한다.
        //    R5의 클릭 통과 전환은 **여기서 하지 않는다**: 지금 켜면 IsInteractive가 false로 떨어져
        //    막 시작한 마퀴가 그대로 얼어붙는다. 판정은 마우스 업(EndSelectGesture)이 맡는다.
        // Shift+빈 곳은 **누적 의도**다 — 기존 선택을 유지한 채 마퀴로 더하려는 것이므로
        // 제자리에서 뗐다고 해제·클릭 통과로 넘어가면 안 된다.
        return new GesturePlan
        {
            Kind = SelectionDragKind.Marquee,
            ClearSelection = !shift,
            HadSelectionOnPress = !shift && selectionCount > 0,
            StartsMarquee = true,
            Captures = true,
        };
    }

    /// <summary>
    /// 커서가 선택 표시 안쪽인가 (R6). 다중 선택은 축 정렬 그룹 프레임,
    /// 단일 선택은 회전을 반영한 로컬 프레임(OBB) — <b>화면에 그려진 점선 경계와 같은 영역</b>이어야 한다.
    ///
    /// 이 판정은 <b>마우스 다운에서만</b> 물어보고, 그 시점에는 <c>SurfaceInputController.BeginSelectGesture</c>
    /// 머리에서 제스처 프레임을 null로 지웠으므로 화면에 그려진 것도 같은 축 정렬 합집합이다 — 그래서 회전 각도가
    /// 붙어도 "그려진 점선 경계와 같은 영역"이 유지된다. 각도를 지속 상태로 승격하는 순간 이 등가성이
    /// 깨지므로, 그때는 <see cref="SelectionGeometry.ContainsInFrame"/>처럼 볼록 사각형 판정으로 바꿔야 한다.
    /// (<c>SurfaceInputController.UpdateSelectGesture</c>는 이 판정을 다시 묻지 않는다.)
    /// </summary>
    private static bool IsInsideSelectionFrame(IReadOnlyList<AnnotationElement> owned, Point pos)
    {
        if (owned.Count >= SelectionGroup.MinGroupCount)
        {
            return SelectionGroup.Frame(owned) is { } frame && frame.Contains(pos);
        }
        return SelectionGeometry.ContainsInFrame(owned[0], pos);
    }
}
