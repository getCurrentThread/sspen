using System.Windows;

namespace SSPen.Annotation;

/// <summary>
/// Shift 제약 (Round 13): 선·화살표 = 15도 단위 스냅, 사각형 = 정사각형, 타원 = 정원.
/// 음수 방향 드래그를 포함한 임의 드래그에서 동작해야 한다.
/// </summary>
public static class ShiftConstraints
{
    /// <summary>
    /// 각도(도)를 가장 가까운 <paramref name="stepDegrees"/> 배수로 스냅 (X1: 회전 변형이 필요로 하는 각도 함수).
    /// 스냅 규칙의 **단일 소유 지점**이며 <see cref="SnapAngle"/>도 이것을 경유한다.
    /// </summary>
    public static double SnapDegrees(double degrees, double stepDegrees = 15)
    {
        if (stepDegrees <= 0)
        {
            return degrees;
        }
        // MidpointRounding.AwayFromZero가 반드시 필요하다: 기본값은 은행가 반올림이라
        // 정확히 반 칸(7.5도)에서 7.5/15 = 0.5 → 짝수인 0으로 내려가 **0도로 스냅**된다.
        // 사용자는 다음 칸(15도)으로 붙는 것을 기대하며, 22.5·37.5 등 모든 반 칸이 같은 문제를 갖는다.
        return Math.Round(degrees / stepDegrees, MidpointRounding.AwayFromZero) * stepDegrees;
    }

    /// <summary>벡터 각도를 가장 가까운 stepDegrees 배수로 스냅. 크기는 유지.</summary>
    public static Point SnapAngle(Point start, Point end, double stepDegrees = 15)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < double.Epsilon)
        {
            return end;
        }
        double degrees = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        double snapped = SnapDegrees(degrees, stepDegrees) * Math.PI / 180.0;
        return new Point(start.X + length * Math.Cos(snapped), start.Y + length * Math.Sin(snapped));
    }

    /// <summary>
    /// |dx| == |dy|가 되도록 끝점을 정규화 (정사각형/정원). 두 축 중 큰 쪽 크기를 사용하고
    /// 드래그 방향 부호를 유지한다.
    /// </summary>
    public static Point NormalizeSquare(Point start, Point end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double side = Math.Max(Math.Abs(dx), Math.Abs(dy));
        double signX = dx < 0 ? -1 : 1;
        double signY = dy < 0 ? -1 : 1;
        return new Point(start.X + signX * side, start.Y + signY * side);
    }

    /// <summary>도구 종류에 맞는 제약 적용.</summary>
    public static Point Apply(ShapeKind kind, Point start, Point end) => kind switch
    {
        ShapeKind.Line or ShapeKind.Arrow => SnapAngle(start, end),
        ShapeKind.Rectangle or ShapeKind.Ellipse => NormalizeSquare(start, end),
        _ => end,
    };
}
