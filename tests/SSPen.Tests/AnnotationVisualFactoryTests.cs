using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

using static SSPen.Tests.StaThread;
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

    private static StrokeElement MakeStroke(Point a, Point b) =>
        new([a, b], Colors.Red, thickness: 4, isHighlighter: false);

    private static TextElement MakeText(Point origin) =>
        new(origin, "가나다", Colors.Black, fontSize: 20, measuredSize: new Size(60, 24));
}
