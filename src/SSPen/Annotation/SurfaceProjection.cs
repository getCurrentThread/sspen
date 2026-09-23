using System.Windows;
using SSPen.Interop;

namespace SSPen.Annotation;

/// <summary>
/// 서피스 → 이관 후보(<see cref="TransferSurface"/>) 투사의 <b>단일 소유자</b> (32단계, AGENTS "Surfaces are placed on WorkArea").
/// 서피스가 실제로 덮는 사각형은 작업 영역(<c>WorkArea</c>, rcWork)이지 모니터 경계(<c>Bounds</c>, rcMonitor)가 아니다 —
/// 둘을 섞으면 모니터 간 선택 이관의 드롭 판정과 좌표 재기준이 조용히 어긋난다 (사용자 요청 18차, CRIT-06).
/// 이 함수가 창 타입이 아니라 <see cref="MonitorSurfaceInfo"/>와 DPI 배율을 받는 이유: 통합 증인(MonitorTransferTests)이
/// 같은 함수를 부를 수 있어야 프로덕션과 테스트가 다른 사각형을 쓰는 드리프트(F1이 정정한 것)가 구조적으로 사라진다.
/// 이관 판정의 입력 쪽(드롭 지점, <see cref="DropPointToVirtual"/>)도 같은 이유로 여기 있다 (64단계, A2-4).
/// </summary>
public static class SurfaceProjection
{
    public static TransferSurface ToTransferSurface(AnnotationDocument document, MonitorSurfaceInfo monitor, double dpiScale) =>
        new(document, monitor.WorkArea, dpiScale);

    /// <summary>
    /// 이관 드롭 지점: 서피스 논리 좌표 → 가상 스크린 물리 좌표 (64단계, A2-4). <see cref="ToTransferSurface"/>와 <b>같은 사각형</b>
    /// (<c>WorkArea</c>)의 원점을 쓴다 — 여기서 <c>Bounds</c>를 쓰면 작업 표시줄 높이만큼 어긋난 지점으로 대상 모니터를 고른다
    /// (AGENTS L17, CRIT-06). 순서는 "DPI를 곱해 반올림한 뒤 정수 원점을 더한다"이다 — 창의 private 변환이던 시절과 같다.
    /// </summary>
    public static (int X, int Y) DropPointToVirtual(MonitorSurfaceInfo monitor, Point logical, double dpiScale)
    {
        var (x, y) = CoordinateSpace.ToPhysical(logical, dpiScale);
        return (monitor.WorkArea.X + x, monitor.WorkArea.Y + y);
    }
}
