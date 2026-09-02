using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using SSPen.Annotation;
using Xunit;
using static SSPen.Tests.StaThread;

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
