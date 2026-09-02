using System.Windows;
using System.Windows.Media;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 선택·변형 스위트가 공유하는 기하 헬퍼 (리팩터링 19단계에서 승격).
///
/// 19단계가 SelectionGroupTests와 SelectionRedTeamTests를 "대상 타입 1:1" 파일로 나누면서 한 파일의
/// private 헬퍼가 여러 파일에서 필요해졌다 — 그 헬퍼만 여기로 올렸고, 한 파일만 쓰는 것
/// (<c>GripWorld</c>, <c>RotateAboutPivot</c>, <c>Tol</c>, <c>Center100</c>/<c>NegLeft</c>)은 그 파일에 남겼다.
/// 본문은 원본과 글자 그대로 같고, 각 멤버의 문서에 출처를 적었다.
///
/// 사용: 파일 머리에 <c>using static SSPen.Tests.TestGeometry;</c> 를 두면 호출부가 승격 전과 글자 그대로 같다.
/// 주의: 들여오는 파일이 같은 이름의 private 멤버를 두면 그쪽이 이 클래스의 오버로드 전체를 가린다
/// (단순 이름 조회는 바깥 타입의 멤버를 먼저 찾고 using static은 그다음이다) — 그래서 승격하면서
/// 원본 파일의 private 사본을 지웠다.
/// </summary>
internal static class TestGeometry
{
    /// <summary>SelectionGroupTests 출신: 빨강 2px 획. (x, y)에서 (x + w, y + h)로 가는 두 점.</summary>
    public static StrokeElement Stroke(double x, double y, double w, double h) =>
        new([new Point(x, y), new Point(x + w, y + h)], Colors.Red, 2, isHighlighter: false);

    /// <summary>SelectionModelTests·SelectionOperationsTests 출신: 검정 3px, (0,0)→(10,10) 획.</summary>
    public static StrokeElement NewStroke() =>
        new([new Point(0, 0), new Point(10, 10)], Colors.Black, 3, isHighlighter: false);

    /// <summary>
    /// SelectionRedTeamTests 출신: 검정 3px, 준 점 목록 그대로의 획 (단일 점 획도 만든다).
    /// 인자 없는 호출은 params 확장형보다 일반형이 우선하므로 위 <see cref="NewStroke()"/>로 간다.
    /// </summary>
    public static StrokeElement NewStroke(params Point[] pts) =>
        new(pts, Colors.Black, thickness: 3, isHighlighter: false);

    /// <summary>TransformMathTests·SelectionRedTeamTests 출신: 핸들의 로컬 앵커 점을 상태 행렬로 월드에 올린다.</summary>
    public static Point AnchorWorld(ElementTransformState state, Rect bounds, HandleKind handle)
    {
        var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        return TransformMath.ToMatrix(state, center).Transform(TransformMath.AnchorLocal(bounds, handle));
    }

    /// <summary>
    /// NaN은 범위 어서트를 조용히 통과하므로 좌표 비교 전에 반드시 먼저 배제한다 (R16).
    /// 세 파일(SelectionGroupTests·TransformMathTests·SelectionRedTeamTests)의 사본은 판정이 같았다 —
    /// NaN 선배제 뒤 양끝 포함 ±tolerance (<c>Assert.InRange</c>와 동치). 실패 메시지가 가장 자세한
    /// SelectionGroupTests 판을 글자 그대로 취했다.
    /// </summary>
    public static void AssertPointsEqual(Point expected, Point actual, double tolerance = 1e-7)
    {
        Assert.False(double.IsNaN(actual.X), "X가 NaN이면 범위 어서트가 조용히 통과한다 (R16).");
        Assert.False(double.IsNaN(actual.Y), "Y가 NaN이면 범위 어서트가 조용히 통과한다 (R16).");
        Assert.True(
            Math.Abs(expected.X - actual.X) <= tolerance && Math.Abs(expected.Y - actual.Y) <= tolerance,
            $"기대 {expected} / 실제 {actual} (허용오차 {tolerance})");
    }
}
