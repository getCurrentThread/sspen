using System.Windows;

namespace SSPen.Annotation;

/// <summary>
/// 도형의 순수 기하 (21단계, ARCH-16). 렌더(<see cref="AnnotationVisualFactory"/>)와 모델 경계
/// (<see cref="ShapeElement"/>의 <c>ModelBounds</c>)가 <b>같은 함수</b>를 부른다 — 화살촉 날개점을 두 벌로
/// 계산하면 마퀴가 촉을 놓치거나 선택 프레임이 촉을 자르는 상태가 표현 가능해진다.
///
/// 시각 팩토리가 아니라 모델 옆에 있는 이유: 모델(<c>AnnotationElements.cs</c>)이 뷰 팩토리를 부르는 것은
/// 저장소의 유일한 모델→뷰 방향 역전이었다. 기하를 여기로 내리면 팩토리와 모델이 함께 이 파일을 보고
/// 의존은 아래로만 흐른다.
/// </summary>
public static class ShapeGeometry
{
    /// <summary>화살촉 두 날개점을 계산하는 순수 기하 함수. 길이 0이면 끝점을 두 번 돌려준다.</summary>
    public static (Point, Point) ArrowHead(Point start, Point end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < double.Epsilon)
        {
            return (end, end);
        }
        double headLength = Math.Clamp(length * 0.25, 8, 24);
        double angle = Math.Atan2(dy, dx);
        const double spread = Math.PI / 7; // ≈25도
        return (
            new Point(end.X - headLength * Math.Cos(angle - spread), end.Y - headLength * Math.Sin(angle - spread)),
            new Point(end.X - headLength * Math.Cos(angle + spread), end.Y - headLength * Math.Sin(angle + spread)));
    }
}
