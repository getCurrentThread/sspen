using System.Windows;
using System.Windows.Media;

namespace SSPen.Annotation;

/// <summary>
/// 획 시작 시점에 스냅샷하는 스타일 값 (드래그 중 퀵컬러 핫키/휠 굵기 조정이
/// 진행 중인 획/도형/텍스트의 미리보기·커밋 스타일을 어긋나게 하는 버그 수정용).
/// </summary>
public readonly record struct StrokeStyle(Color Color, double Thickness, bool IsHighlighter, bool IsFading);

/// <summary>도형 시작 시점에 동결하는 스타일 (색·굵기·페이딩). <see cref="StrokeStyle"/>의 도형판.</summary>
public readonly record struct ShapeStyle(Color Color, double Thickness, bool IsFading);

/// <summary>표 시작 시점에 동결하는 스타일 (색·굵기·행·열·페이딩).</summary>
public readonly record struct TableStyle(Color Color, double Thickness, int Rows, int Columns, bool IsFading);

/// <summary>
/// 텍스트 편집 시작 시점에 동결하는 스타일. <see cref="ShapeStyle"/>과 합치지 않는다 —
/// 텍스트는 <see cref="AppState.TextFontSize"/>(12/16/24/36/48)를, 도형은
/// <see cref="AppState.ShapeThickness"/>(2/4/6/10/16)를 싣는다. 같은 <c>double</c>이지만 다른 양이다.
/// </summary>
public readonly record struct TextStyle(Color Color, double FontSize, bool IsFading);

/// <summary>
/// 제스처 시작 시점의 스타일 동결 규약의 단일 소유자.
/// 획·도형·텍스트 커밋 경로는 진행 중에 <see cref="AppState"/>를 <b>다시 읽지 않는다</b> —
/// 드래그 중 퀵컬러 핫키·휠 굵기 조정·페이딩 토글이 진행 중 요소를 재분류하면
/// 미리보기와 커밋 결과가 어긋난다.
/// </summary>
public static class GestureStyleSnapshot
{
    /// <summary>
    /// 획 스타일 동결. 형광펜 및 페이딩 판정은 <b>시작 시점의 유효 도구</b>에서 나온다.
    ///
    /// R8: 펜 뒤집기(지우개) 등으로 래치된 <paramref name="effectiveTool"/>을 기준으로 판정한다.
    /// <see cref="AppState.ActiveTool"/>에 뒤집기를 흘리는 것은 금지다 — 선택집합 해제의 유일한
    /// 트리거를 발화시킨다 (SEL-B-4).
    /// </summary>
    public static StrokeStyle ForStroke(AppState state, ToolKind effectiveTool)
    {
        bool highlighter = effectiveTool == ToolKind.Highlighter;
        return new StrokeStyle(
            state.CurrentColor,
            highlighter ? state.HighlighterThickness : state.PenThickness,
            highlighter,
            state.FadingInk && AppState.FadingAppliesTo(effectiveTool));
    }

    public static StrokeStyle ForStroke(AppState state) => ForStroke(state, state.ActiveTool);

    /// <summary>도형 스타일 동결 (색·<see cref="AppState.ShapeThickness"/>·페이딩).</summary>
    public static ShapeStyle ForShape(AppState state, ToolKind effectiveTool) =>
        new(state.CurrentColor, state.ShapeThickness, state.FadingInk && AppState.FadingAppliesTo(effectiveTool));

    public static ShapeStyle ForShape(AppState state) => ForShape(state, state.ActiveTool);

    /// <summary>표 스타일 동결 (색·<see cref="AppState.ShapeThickness"/>·행·열·페이딩).</summary>
    public static TableStyle ForTable(AppState state, ToolKind effectiveTool) =>
        new(state.CurrentColor, state.ShapeThickness, state.TableRows, state.TableColumns, state.FadingInk && AppState.FadingAppliesTo(effectiveTool));

    public static TableStyle ForTable(AppState state) => ForTable(state, state.ActiveTool);

    /// <summary>텍스트 스타일 동결 (색·<see cref="AppState.TextFontSize"/>·페이딩).</summary>
    public static TextStyle ForText(AppState state, ToolKind effectiveTool) =>
        new(state.CurrentColor, state.TextFontSize, state.FadingInk && AppState.FadingAppliesTo(effectiveTool));

    public static TextStyle ForText(AppState state) => ForText(state, state.ActiveTool);
}

/// <summary>
/// 진행 중인 획의 점 목록 + 필압 목록 + 시작 시점에 동결된 스타일. 시작점을 생성자로 받으므로
/// <b>비어 있는 상태가 표현 불가능</b>하다 (<see cref="StrokeElement"/>는 0점을 거부한다).
/// </summary>
public sealed class StrokeAccumulator
{
    /// <summary>
    /// 새 점을 채택하는 최소 이동 거리 (논리 px). 마우스 이동 이벤트를 그대로 쌓으면
    /// 한 획이 수천 점이 되어 렌더·히트테스트·직렬화가 모두 비싸진다.
    /// </summary>
    public const double MinPointDistance = 1.5;

    private readonly List<Point> _points = [];
    private readonly List<float> _pressures = [];

    public StrokeAccumulator(Point start, StrokeStyle style, float startPressure = 0.5f)
    {
        Style = style;
        _points.Add(start);
        _pressures.Add(Math.Clamp(startPressure, 0.05f, 1.0f));
    }

    /// <summary>시작 시점에 동결된 스타일 (이후 <see cref="AppState"/> 변경에 영향받지 않는다).</summary>
    public StrokeStyle Style { get; }

    /// <summary>지금까지 채택된 점들 (항상 1개 이상).</summary>
    public IReadOnlyList<Point> Points => _points;

    /// <summary>각 점의 필압 (0.05 ~ 1.0).</summary>
    public IReadOnlyList<float> Pressures => _pressures;

    /// <summary>
    /// 점을 채택했으면 <c>true</c>. 거리는 <b>마지막으로 채택된 점</b>에서 잰다 —
    /// 거절된 점에서 재면 표본 간격이 달라져 획 모양이 바뀐다.
    /// </summary>
    public bool TryAppend(Point p, float pressure = 0.5f)
    {
        if ((p - _points[^1]).Length < MinPointDistance)
        {
            return false;
        }
        _points.Add(p);
        _pressures.Add(Math.Clamp(pressure, 0.05f, 1.0f));
        return true;
    }
}
