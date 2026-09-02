using System.Windows;

namespace SSPen.Annotation;

/// <summary>
/// 표 격자의 순수 기하 (29단계, ARCH-19/R5). 렌더(<see cref="AnnotationVisualFactory.CreateTableGeometry"/>)와
/// 히트테스트(<see cref="TableElement"/>)가 <b>같은 분할선 목록</b>을 쓴다 — 두 벌로 계산하면 "그려진 선 ≠ 맞는 선"이
/// 표현 가능해진다 (<see cref="ShapeGeometry"/>의 ARrowHead와 같은 이유).
///
/// 외곽 4변은 여기 없다: 렌더는 닫힌 figure(Miter 모서리)로, 히트는 Rect 4변으로 각자 조립한다 — 외곽을 열린 선분 4개로
/// 공유하면 모서리가 Flat 캡 노치로 바뀌는 시각 회귀가 난다 (심사 지적). 내부 분할선만 공유한다.
/// </summary>
public static class TableGeometry
{
    /// <summary>드래그 시작·끝점(어느 방향이든)을 정규화한 사각형.</summary>
    public static Rect Normalize(Point start, Point end) => new(start, end);

    /// <summary>
    /// 내부 분할선 — 가로 (rows−1)개 다음 세로 (columns−1)개, 각각 균등 간격. 순서는 렌더의 figure 순서다.
    /// 0 이하는 1로 취급한다 (0 방어 — 정책 클램프는 <see cref="TableGridLimits"/>가, 요소 불변식은 <see cref="TableElement"/>
    /// 생성자가 각자 소유한다).
    /// </summary>
    public static IReadOnlyList<(Point A, Point B)> Dividers(Rect bounds, int rows, int columns)
    {
        int rowCount = Math.Max(1, rows);
        int columnCount = Math.Max(1, columns);
        var lines = new List<(Point A, Point B)>(rowCount + columnCount - 2);

        double rowHeight = bounds.Height / rowCount;
        for (int r = 1; r < rowCount; r++)
        {
            double y = bounds.Top + r * rowHeight;
            lines.Add((new Point(bounds.Left, y), new Point(bounds.Right, y)));
        }

        double columnWidth = bounds.Width / columnCount;
        for (int c = 1; c < columnCount; c++)
        {
            double x = bounds.Left + c * columnWidth;
            lines.Add((new Point(x, bounds.Top), new Point(x, bounds.Bottom)));
        }
        return lines;
    }
}
