using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 표 행·열 한계와 조절 판정의 증인 (24단계, R2/D3). 컨트롤러 수준 동작은 <c>SurfaceTableGestureTests</c>가 고정한다.
/// 돌연변이 검증: <c>TableGridLimits.Max</c>를 9로 바꾸면 <c>Limits_AreOneToTenToday</c>·<c>Clamp_Edges</c>와
/// <c>SurfaceTableGestureTests.Wheel_DuringTableDrag_ClampsRowsAtTen</c>이 함께 빨갛다.
/// </summary>
public class TableGestureRulesTests
{
    [Fact]
    public void Limits_AreOneToTenToday()
    {
        Assert.Equal(1, TableGridLimits.Min);
        Assert.Equal(10, TableGridLimits.Max);
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    [InlineData(10, 10)]
    [InlineData(11, 10)]
    public void Clamp_Edges(int value, int expected) => Assert.Equal(expected, TableGridLimits.Clamp(value));

    [Fact]
    public void AxisForWheel_ShiftIsColumns()
    {
        Assert.Equal(TableAxis.Rows, TableGestureRules.AxisForWheel(shift: false));
        Assert.Equal(TableAxis.Columns, TableGestureRules.AxisForWheel(shift: true));
    }

    /// <summary>열거형 전수 — 축이 늘면 행이 따라오고, 새 축이 다른 축을 건드리면 여기서 빨갛다.</summary>
    [Theory]
    [MemberData(nameof(AllAxes))]
    public void Adjust_EveryAxis_MovesOnlyThatAxis(TableAxis axis)
    {
        var size = new TableSize(3, 3);

        var adjusted = TableGestureRules.Adjust(size, axis, +2);

        Assert.Equal(axis == TableAxis.Rows ? 5 : 3, adjusted.Rows);
        Assert.Equal(axis == TableAxis.Columns ? 5 : 3, adjusted.Columns);
    }

    [Fact]
    public void Adjust_LargeDelta_ClampsToLimits()
    {
        var size = new TableSize(3, 3);

        Assert.Equal(new TableSize(10, 3), TableGestureRules.Adjust(size, TableAxis.Rows, +20));
        Assert.Equal(new TableSize(3, 1), TableGestureRules.Adjust(size, TableAxis.Columns, -20));
    }

    [Fact]
    public void Adjust_ZeroDelta_IsIdentity()
    {
        var size = new TableSize(4, 6);

        Assert.Equal(size, TableGestureRules.Adjust(size, TableAxis.Rows, 0));
        Assert.Equal(size, TableGestureRules.Adjust(size, TableAxis.Columns, 0));
    }

    [Fact]
    public void Adjust_UnknownAxis_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TableGestureRules.Adjust(new TableSize(3, 3), (TableAxis)99, 1));

    public static TheoryData<TableAxis> AllAxes()
    {
        var data = new TheoryData<TableAxis>();
        foreach (var axis in Enum.GetValues<TableAxis>())
        {
            data.Add(axis);
        }
        return data;
    }
}
