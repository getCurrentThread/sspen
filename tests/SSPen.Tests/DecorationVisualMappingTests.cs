using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using SSPen.Annotation;
using Xunit;

using static SSPen.Tests.StaThread;

namespace SSPen.Tests;

/// <summary>
/// 장식 프리미티브 → 시각물 매핑(<see cref="AnnotationVisualFactory.BuildDecoration"/>)의 증인 (64단계, A2-6).
/// "무엇을 그릴지"(<see cref="SurfaceDecorationPlanner"/>)는 43단계부터 헤드리스로 잠겨 있었지만 "어떻게 그릴지"는 창 안의 스위치라
/// 증인이 없었다 — 플래너에 새 프리미티브를 더하면 컴파일은 통과하고 첫 <c>RedrawDecorations</c>(디스패처 콜백 안)에서야 던졌다.
/// 여기서는 어셈블리의 비추상 하위 타입을 <b>리플렉션으로 전수</b> 모아 샘플 목록과 비교하므로, 새 타입은 이 파일에 샘플을 적기 전까지 빨갛다.
/// <c>Shape</c>는 시각 트리 객체라 본문은 STA에서 돈다.
/// </summary>
public class DecorationVisualMappingTests
{
    /// <summary>어셈블리에 있는 모든 비추상 <see cref="DecorationPrimitive"/> 하위 타입 (SEL-LIM-6 트립와이어도 이 목록을 쓴다).</summary>
    internal static Type[] ConcretePrimitiveTypes() =>
        [.. typeof(DecorationPrimitive).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DecorationPrimitive).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)];

    /// <summary>프리미티브 샘플과 기대 시각물 타입. <see cref="HandlePrimitive"/>는 <c>Rotate</c> 값으로 두 모양이 된다.</summary>
    private static (DecorationPrimitive Primitive, Type Visual)[] Samples() =>
    [
        (new MarqueePrimitive(new Rect(10, 20, 30, 40)), typeof(Rectangle)),
        (new OutlinePrimitive([new Point(0, 0), new Point(10, 0), new Point(10, 10), new Point(0, 10)]), typeof(Polygon)),
        (new HandlePrimitive(new Point(50, 60)), typeof(Rectangle)),
        (new HandlePrimitive(new Point(50, 60), Rotate: true), typeof(Ellipse)),
        (new RotateStemPrimitive(new Point(5, 5), new Point(5, 30)), typeof(Line)),
    ];

    [Fact]
    public void BuildDecoration_EveryConcretePrimitiveType_HasAnArm()
    {
        var samples = Samples();

        Assert.Equal(
            ConcretePrimitiveTypes().Select(t => t.FullName),
            samples.Select(s => s.Primitive.GetType()).Distinct().OrderBy(t => t.FullName, StringComparer.Ordinal).Select(t => t.FullName));

        RunSta(() =>
        {
            foreach (var (primitive, visual) in samples)
            {
                var shape = AnnotationVisualFactory.BuildDecoration(primitive);

                Assert.IsType(visual, shape);
                // 장식은 UI다 — 히트는 컨트롤러의 좌표 계산이 하고 시각물은 입력을 받지 않는다 (SEL-10).
                Assert.False(shape.IsHitTestVisible);
            }
        });
    }

    /// <summary>Rotate는 모양만 가른다: 회전 = 원, 크기 = 사각형. 크기와 위치(중심 − size/2)는 같다 — "그려진 것 == 잡히는 것".</summary>
    [Fact]
    public void BuildDecoration_RotateFlag_SelectsEllipse_ElseRectangle()
    {
        RunSta(() =>
        {
            var center = new Point(100, 200);
            double size = TransformMath.HandleScreenSize;

            var rotate = AnnotationVisualFactory.BuildDecoration(new HandlePrimitive(center, Rotate: true));
            var sizeHandle = AnnotationVisualFactory.BuildDecoration(new HandlePrimitive(center));

            Assert.IsType<Ellipse>(rotate);
            Assert.IsType<Rectangle>(sizeHandle);
            foreach (var shape in new Shape[] { rotate, sizeHandle })
            {
                Assert.Equal(size, shape.Width);
                Assert.Equal(size, shape.Height);
                Assert.Equal(center.X - size / 2, Canvas.GetLeft(shape));
                Assert.Equal(center.Y - size / 2, Canvas.GetTop(shape));
            }
        });
    }
}
