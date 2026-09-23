using System.Reflection;
using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 요소 모델 불변성 계약 (SEL-AC-13, f2). 색·굵기·기하·텍스트 내용은 **어떤 방법으로도** 바뀌지 않는다.
/// get-only 유지가 이 규칙을 컴파일 타임에 강제하며, 이 반사 증인이 그 계약을 런타임에 고정한다.
/// </summary>
public class AnnotationElementTests
{
    /// <summary>
    /// 검사 대상 12개를 **명시 열거**한다. 새 속성을 자동 포함시키지 않는 이유: 자동 열거는
    /// 허용 목록이 조용히 늘어나는 것을 잡지 못한다.
    /// </summary>
    private static readonly (Type Type, string Property)[] ImmutableProperties =
    [
        (typeof(AnnotationElement), nameof(AnnotationElement.Id)),
        (typeof(AnnotationElement), nameof(AnnotationElement.Color)),
        (typeof(AnnotationElement), nameof(AnnotationElement.Thickness)),
        (typeof(StrokeElement), nameof(StrokeElement.Points)),
        (typeof(StrokeElement), nameof(StrokeElement.IsHighlighter)),
        (typeof(ShapeElement), nameof(ShapeElement.Kind)),
        (typeof(ShapeElement), nameof(ShapeElement.Start)),
        (typeof(ShapeElement), nameof(ShapeElement.End)),
        (typeof(TableElement), nameof(TableElement.Start)),
        (typeof(TableElement), nameof(TableElement.End)),
        (typeof(TableElement), nameof(TableElement.Rows)),
        (typeof(TableElement), nameof(TableElement.Columns)),
        (typeof(TextElement), nameof(TextElement.Origin)),
        (typeof(TextElement), nameof(TextElement.Text)),
        (typeof(TextElement), nameof(TextElement.FontSize)),
        (typeof(TextElement), nameof(TextElement.MeasuredSize)),
    ];

    /// <summary>
    /// **의도적 mutable 허용 목록**. 이 둘만 쓰기 가능하며, 목록이 늘어나면 이 테스트가 실패해
    /// 리뷰에서 잡힌다. <c>IsFading</c>은 페이딩 잉크 수명, <c>TransformState</c>는 기하 변형(f6)이다.
    /// </summary>
    private static readonly string[] IntentionallyMutable =
    [
        nameof(AnnotationElement.IsFading),
        nameof(AnnotationElement.TransformState),
    ];

    [Fact]
    public void ImmutableProperties_HaveNoPublicSetters_ByReflection()
    {
        Assert.Equal(16, ImmutableProperties.Length);

        foreach (var (type, name) in ImmutableProperties)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property);
            Assert.True(
                property!.SetMethod is null || !property.SetMethod.IsPublic,
                $"{type.Name}.{name}에 public setter가 생겼다 — f2(스타일·내용 편집 금지) 위반.");
        }
    }

    [Fact]
    public void MutableProperties_AreExactlyTheIntentionalAllowList()
    {
        var writable = typeof(AnnotationElement)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(IntentionallyMutable.OrderBy(n => n, StringComparer.Ordinal).ToArray(), writable);
    }

    [Fact]
    public void NewElement_StartsWithIdentityTransform()
    {
        var stroke = new StrokeElement(
            [new Point(0, 0), new Point(10, 0)], Colors.Black, 2, isHighlighter: false);

        Assert.Equal(ElementTransformState.Identity, stroke.TransformState);
        Assert.Equal(1, stroke.TransformState.MeanScale, 9);
    }

    [Fact]
    public void LocalBounds_ThinHorizontalStroke_IsNeverDegenerate()
    {
        // 수평 획은 ModelBounds 높이가 정확히 0이다 (R16).
        var stroke = new StrokeElement(
            [new Point(0, 10), new Point(100, 10)], Colors.Black, 6, isHighlighter: false);

        var bounds = stroke.LocalBounds;

        Assert.Equal(100, bounds.Width, 9);
        Assert.Equal(6, bounds.Height, 9);
        Assert.Equal(10, bounds.Y + bounds.Height / 2, 9);
    }

    [Fact]
    public void LocalBounds_SinglePointStroke_IsNeverDegenerate()
    {
        var dot = new StrokeElement([new Point(5, 5)], Colors.Black, 4, isHighlighter: false);

        var bounds = dot.LocalBounds;

        Assert.Equal(4, bounds.Width, 9);
        Assert.Equal(4, bounds.Height, 9);
    }

    [Fact]
    public void LocalBounds_Arrow_EnclosesArrowHeadWings()
    {
        // ARCH-16: 날개 두 점이 시작점→끝점 사각형 밖으로 나가므로 Bounds만 쓰면 촉을 놓친다.
        var arrow = new ShapeElement(ShapeKind.Arrow, new Point(0, 0), new Point(100, 0), Colors.Red, 2);
        var (wing1, wing2) = ShapeGeometry.ArrowHead(arrow.Start, arrow.End);

        var bounds = arrow.LocalBounds;

        Assert.True(bounds.Contains(wing1), "날개점 1이 로컬 경계 밖이다.");
        Assert.True(bounds.Contains(wing2), "날개점 2가 로컬 경계 밖이다.");
        Assert.True(bounds.Height > arrow.Bounds.Height, "화살촉 때문에 경계가 세로로 넓어져야 한다.");
    }

    [Fact]
    public void LocalBounds_NonArrowShape_MatchesPlainBounds()
    {
        var rect = new ShapeElement(ShapeKind.Rectangle, new Point(0, 0), new Point(100, 50), Colors.Red, 2);

        Assert.Equal(new Rect(0, 0, 100, 50), rect.LocalBounds);
    }

    [Fact]
    public void TableElement_BoundsAndHitTest_MatchesGridLines()
    {
        var table = new TableElement(new Point(0, 0), new Point(100, 100), 2, 2, Colors.Black, 2);

        Assert.Equal(new Rect(0, 0, 100, 100), table.LocalBounds);
        // 외곽선 위
        Assert.True(table.HitTest(new Point(50, 0), tolerance: 2));
        // 내부 가로 분할선 (y=50)
        Assert.True(table.HitTest(new Point(30, 50), tolerance: 2));
        // 내부 세로 분할선 (x=50)
        Assert.True(table.HitTest(new Point(50, 30), tolerance: 2));
        // 셀 내부 빈 공간 (x=25, y=25) -> 거리가 약 25라 false
        Assert.False(table.HitTest(new Point(25, 25), tolerance: 2));
    }

    /// <summary>
    /// 89단계(A4-2): 정확히 수평으로 끈 표는 높이 0으로 확정되고(커밋 판정은 드래그 길이 3px뿐) 화면에는 폭 전체의 선이
    /// 그려진다. 예전에는 표 분기에만 있던 퇴화 가드가 시작점까지의 거리(여기서 50)를 돌려줘 지우개가 시작점 근처에서만 맞았다.
    /// </summary>
    [Fact]
    public void TableElement_ZeroHeight_HitsAlongDrawnLine()
    {
        var table = new TableElement(new Point(0, 0), new Point(100, 0), 3, 3, Colors.Black, 2);

        Assert.True(table.HitTest(new Point(50, 0), tolerance: 2));
    }

    /// <summary>89단계(A4-2): 정확히 수직으로 끈(폭 0) 표도 그려진 선 전체에서 맞는다.</summary>
    [Fact]
    public void TableElement_ZeroWidth_HitsAlongDrawnLine()
    {
        var table = new TableElement(new Point(0, 0), new Point(0, 100), 3, 3, Colors.Black, 2);

        Assert.True(table.HitTest(new Point(0, 50), tolerance: 2));
    }

    /// <summary>
    /// 사각형 외곽 거리는 한 벌이다 (89단계, A4-2 — <c>AnnotationElement.DistanceToRectOutline</c>). 분할선이 없는 1×1 표와
    /// 같은 사각형의 Rectangle 도형은 어느 점에서든 화면 거리가 비트 단위로 같아야 한다. 정상 크기와 영길이(Start==End)
    /// 행은 수정 전후 값이 같다는 특성화이고, 폭·높이 0 행은 표 분기에만 있던 퇴화 가드의 드리프트를 잡는다.
    /// </summary>
    [Theory]
    [InlineData(10, 20, 210, 120)]  // 정상 크기
    [InlineData(210, 120, 10, 20)]  // 정상 크기, 역방향 드래그
    [InlineData(10, 20, 210, 20)]   // 높이 0
    [InlineData(210, 20, 10, 20)]   // 높이 0, 역방향 드래그
    [InlineData(10, 20, 10, 120)]   // 폭 0
    [InlineData(60, 70, 60, 70)]    // 영길이 (Start == End)
    public void RectangleShapeAndOneCellTable_SameRect_ScreenDistanceAgree(double x1, double y1, double x2, double y2)
    {
        var start = new Point(x1, y1);
        var end = new Point(x2, y2);
        var rectangle = new ShapeElement(ShapeKind.Rectangle, start, end, Colors.Black, 2);
        var table = new TableElement(start, end, rows: 1, columns: 1, Colors.Black, 2);

        var samples = SamplePoints().ToList();

        Assert.Equal(20, samples.Count);
        foreach (var p in samples)
        {
            Assert.Equal(rectangle.ScreenDistanceTo(p), table.ScreenDistanceTo(p));
        }
    }

    /// <summary>외곽 안·밖·위와 꼭짓점을 섞은 결정적 샘플 20개 (x 5개 × y 4개).</summary>
    private static IEnumerable<Point> SamplePoints()
    {
        double[] xs = [-10, 10, 110, 210, 230];
        double[] ys = [0, 20, 70, 120];
        return from x in xs from y in ys select new Point(x, y);
    }
}
