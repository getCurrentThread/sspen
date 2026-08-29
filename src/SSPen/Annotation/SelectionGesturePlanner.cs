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
