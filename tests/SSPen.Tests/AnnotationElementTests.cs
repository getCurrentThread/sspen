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
        Assert.Equal(12, ImmutableProperties.Length);

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
        var (wing1, wing2) = AnnotationVisualFactory.ArrowHead(arrow.Start, arrow.End);

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
}
