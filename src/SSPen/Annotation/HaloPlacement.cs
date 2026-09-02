using System.Windows;
using SSPen.Interop;

namespace SSPen.Annotation;

/// <summary>
/// 강조 커서 후광의 표시 판정과 배치 (42단계, ARCH-3). 공유 렌더 틱이 물리 커서 좌표를 모든 서피스에 뿌리면, 각 서피스는
/// "내 작업 영역 안인가"(AGENTS L17: <c>WorkArea</c>이지 <c>Bounds</c>가 아니다 — 작업 표시줄 띠에서는 후광이 꺼진다)와
/// "로컬 좌표 어디인가"(물리→논리 변환은 <see cref="CoordinateSpace"/> 하나)를 판정한다. 두 함수로 나눈 이유:
/// 창은 보일 때만 DPI를 조회한다 (오늘의 호출 순서 보존).
/// </summary>
public static class HaloPlacement
{
    /// <summary>후광 지름 (논리 px).</summary>
    public const double Diameter = 40;

    /// <summary>이 서피스에 후광을 그리는가 — 켜져 있고, 서피스가 보이고, 물리 커서가 작업 영역 안일 때.</summary>
    public static bool IsVisible(bool haloActive, bool surfacesVisible, PhysicalRect workArea, int physicalX, int physicalY) =>
        haloActive && surfacesVisible && workArea.Contains(physicalX, physicalY);

    /// <summary>후광 타원의 좌상단 로컬 좌표 — 작업 영역 원점 기준으로 옮긴 뒤 DPI로 나누고 반지름만큼 되돌린다.</summary>
    public static Point TopLeft(PhysicalRect workArea, int physicalX, int physicalY, double dpiScale)
    {
        var local = CoordinateSpace.ToLogical(physicalX - workArea.X, physicalY - workArea.Y, dpiScale);
        return new Point(local.X - Diameter / 2, local.Y - Diameter / 2);
    }
}
