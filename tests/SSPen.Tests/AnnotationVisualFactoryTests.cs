using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

using static SSPen.Tests.StaThread;
using static SSPen.Tests.TestGeometry;
namespace SSPen.Tests;

/// <summary>
/// 시각물 변환 행렬의 단일 소유 지점 검증 (ARCH-21/R23).
///
/// 계약: <c>RenderTransform</c> 속성만 읽고 measure/arrange를 유발하지 않는다.
/// 실제 레이아웃 검증은 통합 스위트(<c>DecorationRenderTests</c>)가 맡는다.
///
/// <c>UIElement</c> 생성 자체가 <c>InputManager</c>를 초기화하므로 그 한 건만 STA 쓰레드에서 돌린다.
/// STA는 상호작용 데스크톱을 요구하지 않으므로 이 스위트는 여전히 헤드리스 안전하다
/// (창을 띄우지도, <c>Application</c>을 만들지도 않는다).
/// </summary>
public class AnnotationVisualFactoryTests
{
    private const double Tolerance = 1e-9;

    /// <summary>
    /// 텍스트 특례: <c>TextBlock</c>은 자기 좌표계 (0,0)에서 시작하므로 모델 공간으로 올리려면
    /// <c>T(Origin)</c>이 앞에 붙어야 한다. 이 항이 빠지면 변형 피벗이 어긋난다 (ARCH-07).
    /// </summary>
    [Fact]
    public void RenderMatrixFor_TextElement_IncludesOriginTerm()
    {
        var text = MakeText(new Point(120, 80));

        var matrix = AnnotationVisualFactory.RenderMatrixFor(text);

        // 변형이 항등일 때 텍스트 로컬 원점 (0,0)은 모델 원점 Origin으로 사상돼야 한다.
        var mapped = matrix.Transform(new Point(0, 0));
        Assert.Equal(120, mapped.X, Tolerance);
        Assert.Equal(80, mapped.Y, Tolerance);
    }

    /// <summary>획·도형은 이미 절대 모델 좌표라 원점 항이 붙으면 두 번 오프셋된다.</summary>
    [Fact]
    public void RenderMatrixFor_StrokeElement_OmitsOriginTerm()
    {
        var stroke = MakeStroke(new Point(120, 80), new Point(160, 80));

        var matrix = AnnotationVisualFactory.RenderMatrixFor(stroke);

        Assert.Equal(Matrix.Identity, matrix);
        var mapped = matrix.Transform(new Point(120, 80));
        Assert.Equal(120, mapped.X, Tolerance);
        Assert.Equal(80, mapped.Y, Tolerance);
    }

    /// <summary>
    /// <c>BuildVisual</c>이 <c>ApplyRenderTransform</c>을 경유해 변형을 실제로 심는지 (R15).
    /// 이것이 없으면 모델은 옳은데 화면만 정지하는 무증상 결함이 된다.
    /// </summary>
    [Fact]
    public void BuildVisual_ElementWithTransform_SetsRenderTransform()
    {
        var stroke = MakeStroke(new Point(0, 0), new Point(100, 0));
        stroke.TransformState = ElementTransformState.Identity with { Translation = new Vector(40, 25) };

        RunSta(() =>
        {
            var visual = AnnotationVisualFactory.BuildVisual(stroke);

            var transform = Assert.IsType<MatrixTransform>(visual.RenderTransform);
            Assert.Equal(AnnotationVisualFactory.RenderMatrixFor(stroke), transform.Matrix);
            var mapped = transform.Matrix.Transform(new Point(0, 0));
            Assert.Equal(40, mapped.X, Tolerance);
            Assert.Equal(25, mapped.Y, Tolerance);
        });
    }

    // ---- 표 드래그 HUD 배지 (26단계): 텍스트는 인자, 위치는 앵커 + 오프셋 ----

    [Fact]
    public void BuildTableBadge_TextIsArgument_AndSitsAtAnchorPlusOffset()
    {
        RunSta(() =>
        {
            var badge = AnnotationVisualFactory.BuildTableBadge("2 × 3 표", new Point(10, 20));

            Assert.Equal("2 × 3 표", ((System.Windows.Controls.TextBlock)badge.Child).Text);
            Assert.False(badge.IsHitTestVisible);
            Assert.Equal(10 + AnnotationVisualFactory.TableBadgeOffset, System.Windows.Controls.Canvas.GetLeft(badge));
            Assert.Equal(20 + AnnotationVisualFactory.TableBadgeOffset, System.Windows.Controls.Canvas.GetTop(badge));

            AnnotationVisualFactory.UpdateTableBadge(badge, "4 × 1 표", new Point(100, 200));

            Assert.Equal("4 × 1 표", ((System.Windows.Controls.TextBlock)badge.Child).Text);
            Assert.Equal(100 + AnnotationVisualFactory.TableBadgeOffset, System.Windows.Controls.Canvas.GetLeft(badge));
            Assert.Equal(200 + AnnotationVisualFactory.TableBadgeOffset, System.Windows.Controls.Canvas.GetTop(badge));
        });
    }

    // ---- 도형·표 외곽선 Path (88단계, A4-8): 스트로크 스타일은 한 벌 ----

    /// <summary>
    /// 도형과 표의 미리보기 Path가 같은 외곽선 스타일을 쓴다 — 날카로운 모서리(마이터/플랫, 사용자 조타),
    /// 채우기 없음. 한쪽 팩토리만 고치는 드리프트가 생기면 빨간불.
    /// </summary>
    [Fact]
    public void ShapeAndTablePreviewPaths_ShareOutlineStrokeStyle()
    {
        RunSta(() =>
        {
            var color = Color.FromRgb(0x12, 0x34, 0x56);

            var shape = AnnotationVisualFactory.CreateShapeVisual(color, 3);
            var table = AnnotationVisualFactory.CreateTableVisual(color, 3);

            Assert.IsType<System.Windows.Shapes.Path>(shape);
            Assert.IsType<System.Windows.Shapes.Path>(table);
            Assert.Equal(color, Assert.IsType<SolidColorBrush>(shape.Stroke).Color);
            Assert.Equal(color, Assert.IsType<SolidColorBrush>(table.Stroke).Color);
            Assert.Equal(3, shape.StrokeThickness);
            Assert.Equal(shape.StrokeThickness, table.StrokeThickness);
            Assert.Equal(PenLineJoin.Miter, shape.StrokeLineJoin);
            Assert.Equal(shape.StrokeLineJoin, table.StrokeLineJoin);
            Assert.Equal(PenLineCap.Flat, shape.StrokeStartLineCap);
            Assert.Equal(shape.StrokeStartLineCap, table.StrokeStartLineCap);
            Assert.Equal(PenLineCap.Flat, shape.StrokeEndLineCap);
            Assert.Equal(shape.StrokeEndLineCap, table.StrokeEndLineCap);
            Assert.Null(shape.Fill);
            Assert.Null(table.Fill);
        });
    }

    // ---- 도형 지오메트리 (87단계, A4-1): 그려진 것 == 맞는 것 ----

    public static TheoryData<ShapeKind> AllShapeKinds()
    {
        var data = new TheoryData<ShapeKind>();
        foreach (var kind in Enum.GetValues<ShapeKind>())
        {
            data.Add(kind);
        }
        return data;
    }

    /// <summary>
    /// 팩토리가 그리는 지오메트리를 평탄화한 모든 꼭짓점이 요소 히트에 잡힌다 — 화살촉 날개 끝이 빠지면 빨간불.
    /// 타원은 모델이 128샘플 현으로 거리를 재지만 현 오차(r·3e-4 수준)가 굵기/2 안에 든다.
    /// <c>Geometry</c>는 MTA에서 만들 수 있으므로 STA 도우미를 쓰지 않는다.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllShapeKinds))]
    public void CreateShapeGeometry_AllShapeKinds_EveryFlattenedVertexHitsElement(ShapeKind kind)
    {
        var start = new Point(10, 20);
        var end = new Point(210, 120);
        var element = new ShapeElement(kind, start, end, Colors.Red, 2);

        var flattened = AnnotationVisualFactory.CreateShapeGeometry(kind, start, end).GetFlattenedPathGeometry();

        var vertices = FlattenedVertices(flattened).ToList();
        Assert.NotEmpty(vertices);
        Assert.All(vertices, v => Assert.True(element.HitTest(v, tolerance: 0.5), $"{kind} 꼭짓점 {v}"));
    }

    [Fact]
    public void CreateShapeGeometry_UnknownKind_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => AnnotationVisualFactory.CreateShapeGeometry((ShapeKind)99, new Point(0, 0), new Point(10, 10)));
    }

    private static TextElement MakeText(Point origin) =>
        new(origin, "가나다", Colors.Black, fontSize: 20, measuredSize: new Size(60, 24));
}
