using System.Windows;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 4단계: 도형/텍스트 제스처의 순수 판정 (D3, ARCH-2, Round 13).
///
/// <b>정직한 표기</b>: <c>ResolveEnd_PreviewAndCommit_AgreeForEveryShapeKind</c>는
/// '미리보기 == 커밋'을 직접 관측하지 못한다 — 두 호출부는 <c>MouseEventArgs</c>와 private 메서드 뒤에 있다.
/// 이 Theory가 잠그는 것은 <c>ResolveEnd</c>의 계약(4 Kind × shift 2 = 8칸이 <c>ShiftConstraints.Apply</c>와 일치)이고,
/// '판정이 하나뿐'이라는 사실은 <c>ShiftConstraints.Apply</c>가 <c>GestureCommitRules.cs</c> 한 곳에서만
/// 호출된다는 리뷰 게이트가 지킨다.
/// </summary>
public class GestureCommitRulesTests
{
    private const double Tolerance = 1e-9;

    private static void AssertPoint(Point expected, Point actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
    }

    [Theory]
    [InlineData(ShapeKind.Line, false)]
    [InlineData(ShapeKind.Line, true)]
    [InlineData(ShapeKind.Arrow, false)]
    [InlineData(ShapeKind.Arrow, true)]
    [InlineData(ShapeKind.Rectangle, false)]
    [InlineData(ShapeKind.Rectangle, true)]
    [InlineData(ShapeKind.Ellipse, false)]
    [InlineData(ShapeKind.Ellipse, true)]
    public void ResolveEnd_PreviewAndCommit_AgreeForEveryShapeKind(ShapeKind kind, bool shift)
    {
        var start = new Point(40, 25);
        var raw = new Point(163, 71);

        var expected = shift ? ShiftConstraints.Apply(kind, start, raw) : raw;

        AssertPoint(expected, ShapeGestureRules.ResolveEnd(kind, start, raw, shift));
    }

    [Fact]
    public void ResolveEnd_ShiftOnRectangle_NormalizesToSquare()
    {
        var start = new Point(10, 10);
        // |dx| = 100, |dy| = 30 → 큰 쪽(100)으로 정사각형이 되어야 한다.
        var resolved = ShapeGestureRules.ResolveEnd(ShapeKind.Rectangle, start, new Point(110, 40), shift: true);

        AssertPoint(new Point(110, 110), resolved);
    }

    [Fact]
    public void ResolveEnd_NoShift_ReturnsRaw()
    {
        var raw = new Point(-17.5, 903.25);

        // 수식키가 없으면 원시 종점이 그대로 나와야 한다 — 제약이 몰래 걸리면 안 된다.
        AssertPoint(raw, ShapeGestureRules.ResolveEnd(ShapeKind.Ellipse, new Point(4, 4), raw, shift: false));
    }

    [Fact]
    public void ShouldCommit_JustUnderThreshold_IsFalse()
    {
        // 축 정렬이라 거리 계산에 부동소수 오차가 끼지 않는다: 정확히 2.999px.
        Assert.False(ShapeGestureRules.ShouldCommit(new Point(0, 0), new Point(2.999, 0)));
    }

    [Fact]
    public void ShouldCommit_AtThreshold_IsTrue()
    {
        // 임계값 '정확히'는 커밋 쪽이다. 옛 코드의 `< 3 → return`을 `!(>= 3)`으로 옮긴 부호 뒤집힘을 잠근다.
        Assert.True(ShapeGestureRules.ShouldCommit(new Point(0, 0), new Point(SelectionGestureRules.ClickThresholdPixels, 0)));
    }

    [Fact]
    public void ShouldCommit_ThresholdMatchesClickThreshold()
    {
        // 도형 커밋 임계와 선택의 '제자리 클릭' 임계는 같은 상수여야 한다.
        // 어느 한쪽이 리터럴을 따로 갖게 되면 이 경계 훑기에서 즉시 갈라진다.
        double[] distances = [0, 1, 2.5, 2.999, 3, 3.001, 10];
        foreach (double d in distances)
        {
            var start = new Point(7, 7);
            var end = new Point(7 + d, 7);
            Assert.Equal(!SelectionGestureRules.IsStationaryClick(start, end), ShapeGestureRules.ShouldCommit(start, end));
        }
    }

    [Fact]
    public void ProducesElement_WhitespaceOnly_IsFalse()
    {
        Assert.False(TextCommitRules.ProducesElement(null));
        Assert.False(TextCommitRules.ProducesElement(""));
        Assert.False(TextCommitRules.ProducesElement("   \t\r\n "));
    }

    [Fact]
    public void ProducesElement_SingleHangulSyllable_IsTrue()
    {
        // 한 글자짜리 한글도 요소가 되어야 한다 (IME 확정 직후 커밋되는 정상 흐름).
        Assert.True(TextCommitRules.ProducesElement("가"));
        Assert.True(TextCommitRules.ProducesElement("  나  "));
    }

    [Fact]
    public void FloorMeasured_TinyGlyph_IsAtLeastEightByEight()
    {
        var floored = TextCommitRules.FloorMeasured(new Size(1.25, 0));

        Assert.Equal(TextCommitRules.MinMeasuredExtent, floored.Width, Tolerance);
        Assert.Equal(TextCommitRules.MinMeasuredExtent, floored.Height, Tolerance);
    }

    [Fact]
    public void FloorMeasured_LargeMeasurement_IsUnchanged()
    {
        var measured = new Size(213.5, 27.25);
        var floored = TextCommitRules.FloorMeasured(measured);

        Assert.Equal(measured.Width, floored.Width, Tolerance);
        Assert.Equal(measured.Height, floored.Height, Tolerance);
    }
}
