using SSPen.Interop;

namespace SSPen.Annotation;

/// <summary>
/// 서피스 → 이관 후보(<see cref="TransferSurface"/>) 투사의 <b>단일 소유자</b> (32단계, AGENTS "Surfaces are placed on WorkArea").
/// 서피스가 실제로 덮는 사각형은 작업 영역(<c>WorkArea</c>, rcWork)이지 모니터 경계(<c>Bounds</c>, rcMonitor)가 아니다 —
/// 둘을 섞으면 모니터 간 선택 이관의 드롭 판정과 좌표 재기준이 조용히 어긋난다 (사용자 요청 18차, CRIT-06).
/// 이 함수가 창 타입이 아니라 <see cref="MonitorSurfaceInfo"/>와 DPI 배율을 받는 이유: 통합 증인(MonitorTransferTests)이
/// 같은 함수를 부를 수 있어야 프로덕션과 테스트가 다른 사각형을 쓰는 드리프트(F1이 정정한 것)가 구조적으로 사라진다.
/// </summary>
public static class SurfaceProjection
{
    public static TransferSurface ToTransferSurface(AnnotationDocument document, MonitorSurfaceInfo monitor, double dpiScale) =>
        new(document, monitor.WorkArea, dpiScale);
}
