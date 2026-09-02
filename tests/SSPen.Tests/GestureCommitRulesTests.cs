using System.Windows;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 4단계: 도형/텍스트 제스처의 순수 판정 (D3, ARCH-2, Round 13).
///
/// <b>정직한 표기</b>: <c>ResolveEnd_PreviewAndCommit_AgreeForEveryShapeKind</c>는
/// '미리보기 == 커밋'을 직접 관측하지 못한다 — 두 호출부는 <c>MouseEventArgs</c>와 private 메서드 뒤에 있다.
/// 이 Theory가 잠그는 것은 <c>ResolveEnd</c>의 계약(4 Kind × shift 2 = 8칸이 <c>SnapAngle</c>/<c>NormalizeSquare</c>
/// 직접 호출과 일치)이고, '판정이 하나뿐'이라는 사실은 22단계 이후 <c>ShapeKind</c> 분기가
/// <c>ShapeGestureRules.ResolveEnd</c> 한 곳에만 있다는 리뷰 게이트(grep <c>ShapeKind.Line or ShapeKind.Arrow</c>)가 지킨다.
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

        var expected = !shift ? raw
            : kind is ShapeKind.Line or ShapeKind.Arrow
                ? ShiftConstraints.SnapAngle(start, raw)
                : ShiftConstraints.NormalizeSquare(start, raw);

        AssertPoint(expected, ShapeGestureRules.ResolveEnd(kind, start, raw, shift));
    }

    /// <summary>
    /// 22단계: 도형별 분기가 <c>ResolveEnd</c>로 올라왔다 — 열거형 전수. <c>ShapeKind</c>에 멤버가 늘면 이 Theory에
    /// 행이 자동으로 따라오고, 새 멤버가 <c>_ =&gt; raw</c> 폴백으로 떨어지면 여기서 빨갛다 (분기를 결정하라는 신호).
    /// 옛 <c>ShiftConstraintTests.Apply_RoutesByShapeKind</c>의 단언(선==화살표, 사각형==타원, 100/30→100/100)도 포함한다.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllShapeKinds))]
    public void ResolveEnd_EveryShapeKind_WithShift_MatchesSnapOrSquare(ShapeKind kind)
    {
        var start = new Point(0, 0);
        var end = new Point(100, 30);

        var resolved = ShapeGestureRules.ResolveEnd(kind, start, end, shift: true);

        var expected = kind is ShapeKind.Line or ShapeKind.Arrow
            ? ShiftConstraints.SnapAngle(start, end)
            : ShiftConstraints.NormalizeSquare(start, end);
        AssertPoint(expected, resolved);
        if (kind is ShapeKind.Rectangle or ShapeKind.Ellipse)
        {
            AssertPoint(new Point(100, 100), resolved);
        }
    }

    public static TheoryData<ShapeKind> AllShapeKinds()
    {
        var data = new TheoryData<ShapeKind>();
        foreach (var kind in Enum.GetValues<ShapeKind>())
        {
            data.Add(kind);
        }
        return data;
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
