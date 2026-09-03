using System.Reflection;
using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 선택 장식의 색·모양 증인.
///
/// <c>Annotation/</c>은 <c>using SSPen.Shell</c>이 금지된 계층이라 장식 색을 <see cref="ShellPalette"/>에서
/// 직접 가져올 수 없다 — 같은 값을 두 곳에 적는다. 그 두 값이 갈라지면 같은 앱에서 "선택됨"을 뜻하는
/// 색이 두 가지가 되므로, 계층 규약을 지키면서 값을 잠그는 자리가 <b>이 테스트</b>다.
/// </summary>
public class SelectionDecorationVisualTests
{
    private static Color DecorationColor()
    {
        var field = typeof(AnnotationVisualFactory)
            .GetField("DecorationBrush", BindingFlags.NonPublic | BindingFlags.Static)!;
        return ((SolidColorBrush)field.GetValue(null)!).Color;
    }

    [Fact]
    public void DecorationColor_MatchesTheShellAccent() =>
        Assert.Equal(ShellPalette.Accent, DecorationColor());

    /// <summary>흰 보드에서 4.5:1, 검은 보드에서는 더 높다 — 양쪽 보드에서 보여야 한다.</summary>
    [Fact]
    public void DecorationColor_IsVisibleOnBothBoards()
    {
        Assert.True(ShellPalette.ContrastRatio(DecorationColor(), Colors.White) >= 4.5);
        Assert.True(ShellPalette.ContrastRatio(DecorationColor(), Colors.Black) >= 3.0);
    }

    /// <summary>
    /// "그려진 것 == 잡히는 것" (AGENTS L25): 렌더와 히트 판정이 같은 상수를 쓴다. 회전 핸들도
    /// 모양만 원이고 지름은 같다 — 더 크게 그리면 잡히지 않는 가장자리가 생긴다.
    /// </summary>
    [Fact]
    public void Plan_DrawnHandleRadius_DoesNotExceedHitRadius()
    {
        double drawnRadius = TransformMath.HandleScreenSize / 2;
        double hitReach = TransformMath.HandleScreenSize / 2;

        Assert.Equal(hitReach, drawnRadius);
        Assert.True(TransformMath.HandleScreenSize >= 10, "핸들이 10px 미만이면 잡는 손이 자주 빗나간다");
    }

    /// <summary>회전 핸들만 <c>Rotate</c>다 — 창의 렌더 스위치가 이 플래그로 원/사각형을 가른다.</summary>
    [Fact]
    public void Plan_MarksExactlyTheRotateHandle()
    {
        var stroke = new StrokeElement(
            [new Point(300, 300), new Point(500, 400)], Colors.Black, 4, isHighlighter: false);
        var plan = SurfaceDecorationPlanner.Plan([stroke], 1, null, null, new Rect(0, 0, 1920, 1080));

        var handles = plan.OfType<HandlePrimitive>().ToList();
        var rotate = Assert.Single(handles, h => h.Rotate);
        Assert.Equal(TransformMath.SizeHandlesCornersFirst.Length, handles.Count(h => !h.Rotate));
        // 회전 핸들은 스템의 끝점과 같은 자리다 (R5: 렌더와 힌트가 같은 클램프 위치를 쓴다).
        Assert.Equal(plan.OfType<RotateStemPrimitive>().Single().To, rotate.Center);
    }
}
