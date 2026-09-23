using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using SSPen.Annotation;
using Xunit;
using static SSPen.Tests.StaThread;
using static SSPen.Tests.TestGeometry;

namespace SSPen.Tests;

/// <summary>
/// <see cref="TableGeometry"/>의 증인 (29단계, ARCH-19/R5). 분할선 목록의 개수·간격·순서와, 그 목록이 렌더와 히트테스트
/// 양쪽에서 같은 선이라는 사실(분할선 중점마다 <see cref="TableElement.HitTest"/>가 맞는다, 렌더 figure 수 = 1 + 분할선 수,
/// 미리보기와 커밋이 같은 지오메트리)을 고정한다. Geometry는 MTA에서 만들어지지만 Path 비교는 STA다.
/// </summary>
public class TableGeometryTests
{
    private const double Tolerance = 1e-9;

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(2, 3, 3)]
    [InlineData(3, 3, 4)]
    [InlineData(4, 1, 3)]
    [InlineData(10, 10, 18)]
    public void Dividers_Count_IsRowsPlusColumnsMinusTwo(int rows, int columns, int expected) =>
        Assert.Equal(expected, TableGeometry.Dividers(new Rect(0, 0, 100, 60), rows, columns).Count);

    [Fact]
    public void Dividers_EquallySpaced_HorizontalThenVertical()
    {
        var lines = TableGeometry.Dividers(new Rect(10, 20, 90, 60), rows: 3, columns: 2);

        Assert.Equal(3, lines.Count);
        AssertLine(lines[0], new Point(10, 40), new Point(100, 40));
        AssertLine(lines[1], new Point(10, 60), new Point(100, 60));
        AssertLine(lines[2], new Point(55, 20), new Point(55, 80));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-3, 5)]
    public void Dividers_ZeroOrNegative_TreatedAsOne(int rows, int columns)
    {
        var lines = TableGeometry.Dividers(new Rect(0, 0, 100, 60), rows, columns);

        Assert.Equal(Math.Max(1, columns) - 1, lines.Count);
    }

    [Fact]
    public void Normalize_ReversedDrag_IsSameRect()
    {
        var forward = TableGeometry.Normalize(new Point(10, 20), new Point(100, 80));
        var backward = TableGeometry.Normalize(new Point(100, 80), new Point(10, 20));

        Assert.Equal(forward, backward);
        Assert.Equal(new Rect(10, 20, 90, 60), forward);
    }

    /// <summary>렌더와 히트가 같은 선: 분할선 중점마다 요소가 맞고, 셀 한가운데는 맞지 않는다.</summary>
    [Fact]
    public void Dividers_MidpointOfEverySegment_HitsTableElement()
    {
        var table = new TableElement(new Point(0, 0), new Point(120, 60), rows: 3, columns: 4, Colors.Black, 2);

        foreach (var (a, b) in TableGeometry.Dividers(table.Bounds, table.Rows, table.Columns))
        {
            var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            Assert.True(table.HitTest(mid, tolerance: 0.5), $"분할선 중점 {mid}");
        }
        // 첫 셀의 한가운데 (15, 10)은 어떤 선에서도 10px 이상 떨어져 있다.
        Assert.False(table.HitTest(new Point(15, 10), tolerance: 2));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(5, 5)]
    public void CreateTableGeometry_FigureCount_IsOnePlusDividers(int rows, int columns)
    {
        var geometry = AnnotationVisualFactory.CreateTableGeometry(new Point(0, 0), new Point(200, 100), rows, columns);

        var figures = PathGeometry.CreateFromGeometry(geometry).Figures;
        Assert.Equal(1 + (rows - 1) + (columns - 1), figures.Count);
        Assert.True(figures[0].IsClosed); // 외곽은 닫힌 figure — Miter 모서리
        Assert.All(figures.Skip(1), f => Assert.False(f.IsClosed));
    }

    /// <summary>
    /// 폭이나 높이가 0인 표도 그려진 것 == 맞는 것 (89단계, A4-2). 커밋 판정은 드래그 길이 3px뿐이라 정확히 수평·수직으로
    /// 끈 표도 확정되고, 렌더는 선 전체를 그린다. 예전 히트는 표 분기에만 있던 퇴화 가드가 시작점까지의 거리만 재서
    /// 선의 나머지를 놓쳤다 — 그려진 모든 꼭짓점(외곽 4점 + 분할선 끝점)이 요소에 맞아야 한다. Geometry라 MTA에서 돈다.
    /// <c>GetFlattenedPathGeometry</c>는 쓰지 않는다: 넓이 0인 지오메트리에서는 figure를 전부 버려 빈 목록이 나온다(실측) —
    /// 증인이 공허해진다. 표는 직선뿐이라 평탄화가 필요 없으므로 <c>PathGeometry.CreateFromGeometry</c>로 figure를 그대로 편다.
    /// </summary>
    [Theory]
    [InlineData(10, 20, 210, 20)]  // 높이 0
    [InlineData(210, 20, 10, 20)]  // 높이 0, 역방향 드래그
    [InlineData(10, 20, 10, 120)]  // 폭 0
    [InlineData(10, 120, 10, 20)]  // 폭 0, 역방향 드래그
    public void CreateTableGeometry_Degenerate_EveryDrawnVertexHitsTableElement(double x1, double y1, double x2, double y2)
    {
        var start = new Point(x1, y1);
        var end = new Point(x2, y2);
        var table = new TableElement(start, end, rows: 3, columns: 4, Colors.Black, 2);

        var drawn = PathGeometry.CreateFromGeometry(
            AnnotationVisualFactory.CreateTableGeometry(start, end, table.Rows, table.Columns));

        Assert.Equal(1 + (table.Rows - 1) + (table.Columns - 1), drawn.Figures.Count);
        var vertices = FlattenedVertices(drawn).ToList();
        Assert.All(vertices, v => Assert.True(table.HitTest(v, tolerance: 0.5), $"꼭짓점 {v}"));
    }

    /// <summary>미리보기(드래그 중)와 커밋(요소 시각물)이 같은 CreateTableGeometry를 쓴다 — 획의 '미리보기와 커밋이 같은 Create' 규약과 동형.</summary>
    [Fact]
    public void TablePreviewAndCommit_UseSameGeometry()
    {
        RunSta(() =>
        {
            var start = new Point(10, 20);
            var end = new Point(130, 80);

            var preview = AnnotationVisualFactory.CreateTableVisual(Colors.Red, 3);
            AnnotationVisualFactory.UpdateTableVisual(preview, start, end, 3, 4);
            var committed = (Path)AnnotationVisualFactory.BuildVisual(new TableElement(start, end, 3, 4, Colors.Red, 3));

            var previewFigures = PathGeometry.CreateFromGeometry(((Path)preview).Data).Figures;
            var committedFigures = PathGeometry.CreateFromGeometry(committed.Data).Figures;
            Assert.Equal(previewFigures.Count, committedFigures.Count);
            Assert.Equal(((Path)preview).Data.Bounds, committed.Data.Bounds);
        });
    }

    private static void AssertLine((Point A, Point B) line, Point a, Point b)
    {
        Assert.Equal(a.X, line.A.X, Tolerance);
        Assert.Equal(a.Y, line.A.Y, Tolerance);
        Assert.Equal(b.X, line.B.X, Tolerance);
        Assert.Equal(b.Y, line.B.Y, Tolerance);
    }
}
