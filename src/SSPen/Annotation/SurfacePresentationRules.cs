using System.Windows;

namespace SSPen.Annotation;

/// <summary>서피스 호버 커서 종류 — WPF <c>Cursor</c> 객체로의 매핑(Win32 커서 생성 포함)은 창이 한다.</summary>
public enum SurfaceCursorKind
{
    Arrow,
    Pen,
    IBeam,
    Eraser,
    Cross,
}

/// <summary>
/// 상태 → 서피스 표현 (44단계). <see cref="Interactive"/> 하나에서 창이 클릭 통과 exstyle·히트테스트 배경·IsHitTestVisible 셋을 유도한다 —
/// 세 값을 따로 실으면 "클릭 통과 아님 + 배경 없음"(ARCH-1: 투명 창은 exstyle과 무관하게 입력이 통과한다)이 표현 가능해진다.
/// 순서(가시성 → 클릭 통과 → 배경 → 히트 → 커서 → 후광 → 보드 → 취소)와 <c>CancelActiveInput</c> 호출 3곳의 위치는 창이 소유한다 — 여기 없다.
/// </summary>
public readonly record struct SurfacePresentation(Visibility Visibility, bool Interactive, bool CollapseHalo, bool ApplyBoard);

/// <summary>
/// 서피스 표현 판정의 순수 진리표 (44단계, ARCH-1, D4, R8). <c>ContentSurfaceWindow.ApplyState</c>의 중단/비중단 분기와
/// 커서 규칙(도구별·스타일러스 뒤집기)을 한 곳에 둔다 — 이전에는 판정이 ApplyState·SetSuspended·CursorFor·UpdateStylusCursor·
/// ResetCursor 다섯 메서드에 흩어져 있고 창 코드에는 헤드리스 증인이 없었다.
/// </summary>
public static class SurfacePresentationRules
{
    /// <summary>
    /// 중단(캡처 세션 등) 중에는 보이되 입력을 받지 않고, 후광·보드는 건드리지 않는다 (오늘의 조기 반환 의미 보존 — 보드 부기를
    /// 중단 중에 갱신하면 캡처 중 보드 핫키가 즉시 슬라이드를 시작한다). 비중단이면 Interactive = <see cref="AppState.IsInteractive"/>.
    /// </summary>
    public static SurfacePresentation Resolve(bool suspended, bool surfacesVisible, bool interactive, bool haloActive)
    {
        var visibility = surfacesVisible ? Visibility.Visible : Visibility.Hidden;
        return suspended
            ? new SurfacePresentation(visibility, Interactive: false, CollapseHalo: false, ApplyBoard: false)
            : new SurfacePresentation(visibility, interactive, CollapseHalo: !haloActive, ApplyBoard: true);
    }

    /// <summary>
    /// 인터랙티브 서피스의 호버 커서: 펜/형광펜 = 펜, 텍스트 = IBeam, 지우개 = 지우개, 선택 = 화살표, 도형 = 십자.
    /// 스타일러스 뒤집기(R8)는 도구와 무관하게 지우개다. 비인터랙티브의 화살표는 창이 결정한다 (이 함수는 도구가 있을 때만 의미).
    /// </summary>
    public static SurfaceCursorKind HoverCursor(ToolKind tool, bool stylusInverted) => stylusInverted
        ? SurfaceCursorKind.Eraser
        : tool switch
        {
            ToolKind.Pen or ToolKind.Highlighter => SurfaceCursorKind.Pen,
            ToolKind.Text => SurfaceCursorKind.IBeam,
            ToolKind.Eraser => SurfaceCursorKind.Eraser,
            ToolKind.Select => SurfaceCursorKind.Arrow,
            _ => SurfaceCursorKind.Cross,
        };
}
