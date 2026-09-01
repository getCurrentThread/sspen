using System.Windows;
using System.Windows.Media;

namespace SSPen.Annotation;

public enum ShapeKind
{
    Line,
    Arrow,
    Rectangle,
    Ellipse,
}

/// <summary>
/// 판서 요소 도메인 모델 (플랜 원칙 4: 하나의 획 모델).
/// 히트테스트는 순수 기하 계산으로 UI 요소와 분리되어 유닛테스트 가능하다.
/// 지우개는 클릭 + 드래그 삭제 (사용자 조타 12차로 Round 13 클릭 전용에서 확장).
/// </summary>
public abstract class AnnotationElement
{
    private static long _nextId;

    protected AnnotationElement(Color color, double thickness)
    {
        Id = Interlocked.Increment(ref _nextId);
        Color = color;
        Thickness = thickness;
    }

    public long Id { get; }

    public Color Color { get; }

    public double Thickness { get; }

    /// <summary>페이딩 잉크 활성 중 그려진 요소인가 (활성 이후 획만 대상).</summary>
    public bool IsFading { get; set; }

    /// <summary>
    /// 누적 기하 변형 (SEL-1). <c>IsFading</c>에 이은 두 번째이자 마지막 mutable 필드다.
    /// 원본 기하(<see cref="Color"/>/<see cref="Thickness"/>/좌표)는 get-only를 유지하므로
    /// f2(스타일·내용 편집 금지)와 f6(기하 변형 허용)이 컴파일러로 동시에 강제된다.
    /// </summary>
    public ElementTransformState TransformState { get; set; } = ElementTransformState.Identity;

    /// <summary>변형 전 모델 공간 축 정렬 경계 (타입별 원본 기하에서 계산).</summary>
    protected abstract Rect ModelBounds { get; }

    /// <summary>
    /// 로컬 경계 상자: <see cref="ModelBounds"/>를 각 축 <c>max(Thickness, 1)</c> 이상으로 벌린 것 (R16).
    /// 피벗·앵커·핸들 로컬 배치·스케일 기준이 전부 이 상자를 쓴다. 수평/수직 선처럼 한 축이
    /// 정확히 0인 기하에서 0/0 → NaN이 나와 요소가 증발하는 것을 원천 차단한다.
    /// </summary>
    public Rect LocalBounds => TransformMath.NonDegenerate(ModelBounds, Math.Max(Thickness, 1));

    /// <summary>현재 변형 상태의 월드 사상 행렬 (<see cref="TransformMath.ToMatrix"/> 단일 합성 지점 경유).</summary>
    public Matrix TransformMatrix
    {
        get
        {
            var bounds = LocalBounds;
            return TransformMath.ToMatrix(
                TransformState, new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2));
        }
    }

    /// <summary>축 정렬 월드 경계. **마퀴 교차 판정 전용**이다 (SEL-B-1) — 핸들 배치에는 쓰지 않는다.</summary>
    public Rect TransformedBounds => Rect.Transform(LocalBounds, TransformMatrix);

    /// <summary>
    /// 로컬 프레임 4점(OBB)을 **시계 방향**으로 반환한다: 좌상 → 우상 → 우하 → 좌하 (계약).
    /// 점선 경계·8핸들·회전 핸들 렌더가 이 위에 올라간다 (MI-1).
    /// </summary>
    public Point[] TransformedCorners()
    {
        var b = LocalBounds;
        var m = TransformMatrix;
        return
        [
            m.Transform(b.TopLeft),
            m.Transform(b.TopRight),
            m.Transform(b.BottomRight),
            m.Transform(b.BottomLeft),
        ];
    }

    /// <summary>점에서 요소 외곽까지의 거리 — **변형 전 모델 공간** (타입별 순수 기하).</summary>
    protected abstract double ModelDistanceTo(Point p);

    /// <summary>
    /// 점에서 요소 외곽까지의 거리 — **화면 공간** (ARCH-19).
    /// 명중 판정과 순위 비교의 공간을 하나로 통일한다: 모델 공간 값끼리 비교하면 3배 확대된 획의
    /// 모델 거리 5(화면 15)가 변형 없는 획의 모델 거리 8(화면 8)을 이겨 **화면상 더 먼 요소가 지워진다**.
    /// </summary>
    public double ScreenDistanceTo(Point p) =>
        ModelDistanceTo(TransformMath.ToLocal(TransformState, LocalBounds, p)) * TransformState.MeanScale;

    /// <summary>
    /// 선 굵기 절반 + 허용 오차 안이면 명중. 굵기는 모델 단위이므로 화면 공간에서는 배율을 곱해 올린다.
    /// 변형이 없으면 <c>MeanScale == 1</c>이라 변형 도입 이전과 판정이 완전히 동일하다.
    /// </summary>
    public bool HitTest(Point p, double tolerance) =>
        ScreenDistanceTo(p) <= tolerance + Thickness * TransformState.MeanScale / 2;

    protected static double DistanceToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lengthSq = dx * dx + dy * dy;
        if (lengthSq < double.Epsilon)
        {
            return (p - a).Length;
        }
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSq, 0, 1);
        var proj = new Point(a.X + t * dx, a.Y + t * dy);
        return (p - proj).Length;
    }
}

/// <summary>자유 획 (펜 / 형광펜, 필압 지원).</summary>
public sealed class StrokeElement : AnnotationElement
{
    private readonly List<Point> _points;
    private readonly List<float> _pressures;

    public StrokeElement(IEnumerable<Point> points, Color color, double thickness, bool isHighlighter, IEnumerable<float>? pressures = null)
        : base(color, thickness)
    {
        _points = [.. points];
        if (_points.Count == 0)
        {
            throw new ArgumentException("획에는 최소 1개의 점이 필요합니다.", nameof(points));
        }
        if (pressures != null)
        {
            _pressures = [.. pressures];
        }
        else
        {
            _pressures = [.. Enumerable.Repeat(0.5f, _points.Count)];
        }
        IsHighlighter = isHighlighter;
    }

    public IReadOnlyList<Point> Points => _points;

    public IReadOnlyList<float> Pressures => _pressures;

    public bool IsHighlighter { get; }

    protected override Rect ModelBounds
    {
        get
        {
            double left = double.MaxValue, top = double.MaxValue;
            double right = double.MinValue, bottom = double.MinValue;
            foreach (var p in _points)
            {
                left = Math.Min(left, p.X);
                top = Math.Min(top, p.Y);
                right = Math.Max(right, p.X);
                bottom = Math.Max(bottom, p.Y);
            }
            return new Rect(left, top, right - left, bottom - top);
        }
    }

    protected override double ModelDistanceTo(Point p)
    {
        if (_points.Count == 1)
        {
            return (p - _points[0]).Length;
        }
        double min = double.MaxValue;
        for (int i = 1; i < _points.Count; i++)
        {
            min = Math.Min(min, DistanceToSegment(p, _points[i - 1], _points[i]));
        }
        return min;
    }
}

/// <summary>도형 4종: 드래그 시작점→끝점, 외곽선만, 확정 후 편집 불가 (Round 13).</summary>
public sealed class ShapeElement : AnnotationElement
{
    private const int EllipseSamples = 128;

    public ShapeElement(ShapeKind kind, Point start, Point end, Color color, double thickness)
        : base(color, thickness)
    {
        Kind = kind;
        Start = start;
        End = end;
    }

    public ShapeKind Kind { get; }

    public Point Start { get; }

    public Point End { get; }

    public Rect Bounds => new(Start, End);

    /// <summary>
    /// 화살표는 <see cref="Bounds"/>가 촉을 감싸지 못한다 — 날개 두 점이 시작점→끝점 사각형 **밖**으로
    /// 나가므로 경계가 과소해지고 마퀴 판정이 촉을 놓친다 (ARCH-16). 날개점까지 합집합한다.
    /// </summary>
    protected override Rect ModelBounds
    {
        get
        {
            var bounds = Bounds;
            if (Kind != ShapeKind.Arrow)
            {
                return bounds;
            }
            var (wing1, wing2) = AnnotationVisualFactory.ArrowHead(Start, End);
            bounds.Union(wing1);
            bounds.Union(wing2);
            return bounds;
        }
    }

    protected override double ModelDistanceTo(Point p)
    {
        switch (Kind)
        {
            case ShapeKind.Line:
            case ShapeKind.Arrow:
                return DistanceToSegment(p, Start, End);

            case ShapeKind.Rectangle:
            {
                var r = Bounds;
                var tl = r.TopLeft;
                var tr = r.TopRight;
                var br = r.BottomRight;
                var bl = r.BottomLeft;
                return Math.Min(
                    Math.Min(DistanceToSegment(p, tl, tr), DistanceToSegment(p, tr, br)),
                    Math.Min(DistanceToSegment(p, br, bl), DistanceToSegment(p, bl, tl)));
            }

            case ShapeKind.Ellipse:
            {
                var r = Bounds;
                double cx = r.X + r.Width / 2;
                double cy = r.Y + r.Height / 2;
                double rx = Math.Max(r.Width / 2, 0.5);
                double ry = Math.Max(r.Height / 2, 0.5);
                double min = double.MaxValue;
                Point prev = new(cx + rx, cy);
                for (int i = 1; i <= EllipseSamples; i++)
                {
                    double angle = i * (2 * Math.PI / EllipseSamples);
                    var current = new Point(cx + rx * Math.Cos(angle), cy + ry * Math.Sin(angle));
                    min = Math.Min(min, DistanceToSegment(p, prev, current));
                    prev = current;
                }
                return min;
            }

            default:
                throw new InvalidOperationException($"알 수 없는 도형: {Kind}");
        }
    }
}

/// <summary>텍스트: 맑은 고딕, 굵기 설정 연동 크기, 확정 후 편집 불가 (Round 13).</summary>
public sealed class TextElement : AnnotationElement
{
    public TextElement(Point origin, string text, Color color, double fontSize, Size measuredSize)
        : base(color, thickness: 1)
    {
        Origin = origin;
        Text = text;
        FontSize = fontSize;
        MeasuredSize = measuredSize;
    }

    public Point Origin { get; }

    public string Text { get; }

    public double FontSize { get; }

    public Size MeasuredSize { get; }

    public Rect Bounds => new(Origin, MeasuredSize);

    protected override Rect ModelBounds => Bounds;

    /// <summary>텍스트는 경계 상자 전체가 명중 대상 (요소 단위 삭제).</summary>
    protected override double ModelDistanceTo(Point p)
    {
        var b = Bounds;
        if (b.Contains(p))
        {
            return 0;
        }
        double dx = Math.Max(Math.Max(b.Left - p.X, 0), p.X - b.Right);
        double dy = Math.Max(Math.Max(b.Top - p.Y, 0), p.Y - b.Bottom);
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
