using System.Windows;

namespace SSPen.Interop;

/// <summary>물리 픽셀 사각형 (Win32 좌표계, 음수 원점 허용).</summary>
public readonly record struct PhysicalRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public bool Contains(int px, int py) => px >= X && px < Right && py >= Y && py < Bottom;

    public PhysicalRect Intersect(PhysicalRect other)
    {
        int x1 = Math.Max(X, other.X);
        int y1 = Math.Max(Y, other.Y);
        int x2 = Math.Min(Right, other.Right);
        int y2 = Math.Min(Bottom, other.Bottom);
        return x2 <= x1 || y2 <= y1
            ? new PhysicalRect(x1, y1, 0, 0)
            : new PhysicalRect(x1, y1, x2 - x1, y2 - y1);
    }

    public PhysicalRect Union(PhysicalRect other)
    {
        int x1 = Math.Min(X, other.X);
        int y1 = Math.Min(Y, other.Y);
        int x2 = Math.Max(Right, other.Right);
        int y2 = Math.Max(Bottom, other.Bottom);
        return new PhysicalRect(x1, y1, x2 - x1, y2 - y1);
    }

    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    /// Win32 <c>RECT</c>식 네 변(좌·상·우·하) → 원점+크기 (65단계, A7-8). 모니터 사각형·작업 영역·창 사각형 읽기가
    /// 같은 산술을 따로 적던 것을 여기 한 곳으로 모은다. 음수 원점은 그대로 통과하고, 폭이나 높이가 0 이하인
    /// 퇴화 사각형도 보정하지 않는다 (<see cref="IsEmpty"/>가 판정한다).
    /// </summary>
    public static PhysicalRect FromLtrb(int left, int top, int right, int bottom) =>
        new(left, top, right - left, bottom - top);
}

/// <summary>
/// 물리 픽셀 ↔ WPF 논리 좌표 변환 유틸리티 — 변환은 이 한 곳에서만 일어난다 (플랜 원칙 3, R2/R3).
/// 모든 Win32 경계(BitBlt, 모니터 사각형, SetWindowPos)는 물리 픽셀로 계산하고,
/// WPF 경계에서만 DPI 배율을 적용한다. 100% DPI에서는 항등이지만 PerMonitorV2 정합성을 위해 구조를 유지한다.
/// </summary>
public static class CoordinateSpace
{
    /// <summary>물리 픽셀 점 → 논리 좌표.</summary>
    public static Point ToLogical(int physicalX, int physicalY, double dpiScale)
    {
        Guard(dpiScale);
        return new Point(physicalX / dpiScale, physicalY / dpiScale);
    }

    /// <summary>논리 좌표 → 물리 픽셀 (반올림).</summary>
    public static (int X, int Y) ToPhysical(Point logical, double dpiScale)
    {
        Guard(dpiScale);
        return ((int)Math.Round(logical.X * dpiScale), (int)Math.Round(logical.Y * dpiScale));
    }

    /// <summary>물리 사각형 → 논리 사각형.</summary>
    public static Rect ToLogical(PhysicalRect r, double dpiScale)
    {
        Guard(dpiScale);
        return new Rect(r.X / dpiScale, r.Y / dpiScale, r.Width / dpiScale, r.Height / dpiScale);
    }

    /// <summary>논리 사각형 → 물리 사각형 (반올림).</summary>
    public static PhysicalRect ToPhysical(Rect r, double dpiScale)
    {
        Guard(dpiScale);
        return new PhysicalRect(
            (int)Math.Round(r.X * dpiScale),
            (int)Math.Round(r.Y * dpiScale),
            (int)Math.Round(r.Width * dpiScale),
            (int)Math.Round(r.Height * dpiScale));
    }

    /// <summary>
    /// 창 크기처럼 잘리면 안 되는 논리 길이 → 물리 픽셀: <b>올림</b> (65단계, A1-7).
    /// 곱의 부동소수 잡음을 보정하지 않고 <c>(int)Math.Ceiling(길이 × 배율)</c> 그대로다 — 툴바·토스트 배치가
    /// 옮겨 오기 전에 호출 지점에서 쓰던 식과 비트 단위로 같아야 한다 (<c>CoordinateSpaceTests</c>가 잠근다).
    /// </summary>
    public static int ToPhysicalExtent(double logicalLength, double dpiScale)
    {
        Guard(dpiScale);
        return (int)Math.Ceiling(logicalLength * dpiScale);
    }

    /// <summary>
    /// 여백처럼 가까운 값이면 되는 논리 길이 → 물리 픽셀: <b>반올림</b> (65단계, A1-7).
    /// <see cref="ToPhysical(Point, double)"/>와 같은 기본 <c>Math.Round</c>(중간값은 짝수 쪽 — 은행가 반올림)이며,
    /// 옮겨 오기 전 호출 지점의 식과 비트 단위로 같다.
    /// </summary>
    public static int ToPhysicalLength(double logicalLength, double dpiScale)
    {
        Guard(dpiScale);
        return (int)Math.Round(logicalLength * dpiScale);
    }

    /// <summary>
    /// 서피스 간 **점 사상** (SEL-14): 원본 서피스 논리 점 → 대상 서피스 논리 점.
    /// 화면상 물리 위치를 보존하므로 두 모니터의 DPI가 달라도 사용자가 본 자리에 그대로 놓인다.
    ///
    /// 주의: 이것은 **위치**의 사상이다. 변위(예: <c>ElementTransformState.Translation</c>)을 그대로
    /// 먹이면 원점 오프셋이 중복 가산되어 요소가 엉뚱한 곳으로 간다 —
    /// 변위는 기준점을 더해 위치로 만든 뒤 사상하고 다시 빼야 한다 (ARCH-20).
    /// </summary>
    public static Point Rebase(
        Point sourceLogical,
        PhysicalRect sourceMonitor,
        double sourceDpi,
        PhysicalRect targetMonitor,
        double targetDpi)
    {
        Guard(sourceDpi);
        Guard(targetDpi);
        // 원본 서피스 로컬 → 가상 스크린 물리 → 대상 서피스 로컬.
        double physicalX = sourceMonitor.X + sourceLogical.X * sourceDpi;
        double physicalY = sourceMonitor.Y + sourceLogical.Y * sourceDpi;
        return new Point(
            (physicalX - targetMonitor.X) / targetDpi,
            (physicalY - targetMonitor.Y) / targetDpi);
    }

    /// <summary>여러 물리 사각형의 합집합 (가상 스크린 계산).</summary>
    public static PhysicalRect Union(IEnumerable<PhysicalRect> rects)
    {
        PhysicalRect? acc = null;
        foreach (var r in rects)
        {
            acc = acc is null ? r : acc.Value.Union(r);
        }
        return acc ?? new PhysicalRect(0, 0, 0, 0);
    }

    /// <summary>영역을 경계 사각형 안으로 클램프 (모니터 이음새 처리).</summary>
    public static PhysicalRect Clamp(PhysicalRect region, PhysicalRect bounds) => bounds.Intersect(region);

    private static void Guard(double dpiScale)
    {
        if (dpiScale <= 0 || double.IsNaN(dpiScale) || double.IsInfinity(dpiScale))
        {
            throw new ArgumentOutOfRangeException(nameof(dpiScale), dpiScale, "DPI 배율은 양수여야 합니다.");
        }
    }
}
