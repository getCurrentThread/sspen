using System.Windows;
using SSPen.Annotation;
using Xunit;
using static SSPen.Tests.StaThread;

namespace SSPen.Tests;

/// <summary>
/// <see cref="StrokeGeometry"/>의 증인 (31단계, R8). 필압 정책(기본 0.5, 클램프 0.05..1.0)이 한 곳에만 있고 지오메트리 폭이
/// 필압에 반응함을 고정한다. 돌연변이 검증: 누적기의 클램프 호출을 리터럴로 되돌리거나 한쪽 상수만 바꾸면
/// <c>StrokeAccumulatorTests.TryAppend_ClampsPressureViaStrokeGeometry</c>와 여기 <c>Limits_Today</c>가 함께 빨갛다.
/// Ink 엔진 객체는 STA에서 만든다.
/// </summary>
public class StrokeGeometryTests
{
    private static readonly Point[] Line = [new(0, 0), new(50, 0), new(100, 0)];

    [Fact]
    public void Limits_Today()
    {
        Assert.Equal(0.5f, StrokeGeometry.DefaultPressure);
        Assert.Equal(0.05f, StrokeGeometry.MinPressure);
        Assert.Equal(1.0f, StrokeGeometry.MaxPressure);
    }

    [Theory]
    [InlineData(-1f, 0.05f)]
    [InlineData(0f, 0.05f)]
    [InlineData(0.05f, 0.05f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(1f, 1f)]
    [InlineData(7f, 1f)]
    public void ClampPressure_Edges(float pressure, float expected) => Assert.Equal(expected, StrokeGeometry.ClampPressure(pressure));

    [Fact]
    public void Create_HigherPressure_YieldsWiderBounds()
    {
        RunSta(() =>
        {
            var thin = StrokeGeometry.Create(Line, [0.1f, 0.1f, 0.1f], thickness: 10, isHighlighter: false).Bounds;
            var thick = StrokeGeometry.Create(Line, [1.0f, 1.0f, 1.0f], thickness: 10, isHighlighter: false).Bounds;

            Assert.True(thick.Height > thin.Height, $"thick {thick.Height} vs thin {thin.Height}");
        });
    }

    [Fact]
    public void Create_NullPressures_EqualsExplicitDefault()
    {
        RunSta(() =>
        {
            var implicitDefault = StrokeGeometry.Create(Line, null, thickness: 8, isHighlighter: false).Bounds;
            var explicitDefault = StrokeGeometry.Create(
                Line, [StrokeGeometry.DefaultPressure, StrokeGeometry.DefaultPressure, StrokeGeometry.DefaultPressure], thickness: 8, isHighlighter: false).Bounds;

            Assert.Equal(explicitDefault, implicitDefault);
        });
    }

    [Fact]
    public void Create_OutOfRangePressure_IsClampedNotAmplified()
    {
        RunSta(() =>
        {
            var max = StrokeGeometry.Create(Line, [1f, 1f, 1f], thickness: 10, isHighlighter: false).Bounds;
            var over = StrokeGeometry.Create(Line, [9f, 9f, 9f], thickness: 10, isHighlighter: false).Bounds;

            Assert.Equal(max, over);
        });
    }

    [Fact]
    public void CreateStrokeGeometry_NoLongerOnFactory_ByReflection() =>
        Assert.Null(typeof(AnnotationVisualFactory).GetMethod("CreateStrokeGeometry"));
}
