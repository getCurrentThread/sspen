using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SSPen.Annotation;

/// <summary>
/// 판서 요소(<see cref="AnnotationElement"/>) → WPF 시각물 변환 순수 헬퍼.
/// <see cref="ContentSurfaceWindow"/>가 요소 추가/미리보기 렌더링에 소비한다.
/// </summary>
public static class AnnotationVisualFactory
{
    public static FrameworkElement BuildVisual(AnnotationElement element)
    {
        FrameworkElement visual = element switch
        {
            StrokeElement stroke => BuildStrokeVisual(stroke),
            ShapeElement shape => BuildShapeVisual(shape),
            TableElement table => BuildTableVisual(table),
            TextElement text => BuildTextVisual(text),
            _ => throw new InvalidOperationException($"알 수 없는 요소: {element.GetType().Name}"),
        };
        ApplyRenderTransform(visual, element);
        return visual;
    }

    /// <summary>
    /// 요소 → 시각물 변환 행렬의 **단일 소유 지점** (ARCH-21).
    /// <see cref="TextElement"/>만 특례가 있다: <c>TextBlock</c>은 자기 좌표계 (0,0)에서 시작하므로
    /// 모델 공간으로 올리려면 <c>T(Origin)</c>을 먼저 거쳐야 한다. 획·도형은 이미 절대 모델 좌표다.
    ///
    /// 이 분기를 아는 곳이 여러 군데면 채널 핸들러가 일반 요소처럼 <c>ToMatrix</c>만 걸어
    /// 텍스트의 <c>T(Origin)</c> 항이 빠지고, 그 결함이 undo·롤백·이관 경로에서만 재발한다.
    /// </summary>
    public static Matrix RenderMatrixFor(AnnotationElement element)
    {
        if (element is not TextElement text)
        {
            return element.TransformMatrix;
        }
        var m = Matrix.Identity;
        m.Translate(text.Origin.X, text.Origin.Y);
        m.Append(element.TransformMatrix);
        return m;
    }

    /// <summary>
    /// 시각물에 변형을 반영하는 **유일한** 지점 (R23).
    /// 드래그·undo·롤백·이관 보정이 전부 이 함수로 수렴한다.
    /// </summary>
    public static void ApplyRenderTransform(FrameworkElement visual, AnnotationElement element) =>
        visual.RenderTransform = new MatrixTransform(RenderMatrixFor(element));

    public static FrameworkElement BuildStrokeVisual(StrokeElement stroke)
    {
        var path = new Path
        {
            Data = CreateStrokeGeometry(stroke.Points, stroke.Pressures, stroke.Thickness, stroke.IsHighlighter),
            Fill = StrokeBrush(stroke.Color, stroke.IsHighlighter),
        };
        return path;
    }

    /// <summary>
    /// 점들과 필압 정보로부터 WPF Ink 엔진 기반의 매끄러운 가변 두께 아웃라인 지오메트리를 생성한다.
    /// </summary>
    public static Geometry CreateStrokeGeometry(IReadOnlyList<Point> points, IReadOnlyList<float>? pressures, double thickness, bool isHighlighter)
    {
        var spc = new StylusPointCollection();
        for (int i = 0; i < points.Count; i++)
        {
            float p = (pressures != null && i < pressures.Count) ? pressures[i] : 0.5f;
            spc.Add(new StylusPoint(points[i].X, points[i].Y, Math.Clamp(p, 0.05f, 1.0f)));
        }

        var da = new DrawingAttributes
        {
            Color = Colors.Black, // Fill 브러시로 채우므로 da의 색상은 기본값 사용
            Width = thickness,
            Height = thickness,
            IsHighlighter = isHighlighter,
            FitToCurve = true,
            StylusTip = StylusTip.Ellipse,
            IgnorePressure = false,
        };

        var wpfStroke = new System.Windows.Ink.Stroke(spc, da);
        return wpfStroke.GetGeometry(da);
    }

    private static FrameworkElement BuildShapeVisual(ShapeElement shape)
    {
        var visual = CreateShapeVisual(shape.Kind, shape.Color, shape.Thickness);
        UpdateShapeVisual(visual, shape.Kind, shape.Start, shape.End);
        return visual;
    }

    private static FrameworkElement BuildTableVisual(TableElement table)
    {
        var visual = CreateTableVisual(table.Color, table.Thickness);
        UpdateTableVisual(visual, table.Start, table.End, table.Rows, table.Columns);
        return visual;
    }

    public static Shape CreateTableVisual(Color color, double thickness)
    {
        return new Path
        {
            Stroke = CreateFrozen(color),
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Miter,
            StrokeStartLineCap = PenLineCap.Flat,
            StrokeEndLineCap = PenLineCap.Flat,
        };
    }

    public static void UpdateTableVisual(Shape visual, Point start, Point end, int rows, int columns)
    {
        if (visual is not Path path)
        {
            return;
        }
        path.Data = CreateTableGeometry(start, end, rows, columns);
    }

    public static Geometry CreateTableGeometry(Point start, Point end, int rows, int columns)
    {
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            double left = Math.Min(start.X, end.X);
            double top = Math.Min(start.Y, end.Y);
            double right = Math.Max(start.X, end.X);
            double bottom = Math.Max(start.Y, end.Y);
            double width = right - left;
            double height = bottom - top;

            // 외곽 사각형
            ctx.BeginFigure(new Point(left, top), isFilled: false, isClosed: true);
            ctx.LineTo(new Point(right, top), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(right, bottom), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(left, bottom), isStroked: true, isSmoothJoin: false);

            // 가로 분할선 (rows - 1 개)
            int rCount = Math.Max(1, rows);
            double rowH = height / rCount;
            for (int r = 1; r < rCount; r++)
            {
                double y = top + r * rowH;
                ctx.BeginFigure(new Point(left, y), isFilled: false, isClosed: false);
                ctx.LineTo(new Point(right, y), isStroked: true, isSmoothJoin: false);
            }

            // 세로 분할선 (columns - 1 개)
            int cCount = Math.Max(1, columns);
            double colW = width / cCount;
            for (int c = 1; c < cCount; c++)
            {
                double x = left + c * colW;
                ctx.BeginFigure(new Point(x, top), isFilled: false, isClosed: false);
                ctx.LineTo(new Point(x, bottom), isStroked: true, isSmoothJoin: false);
            }
        }
        geom.Freeze();
        return geom;
    }

    private static FrameworkElement BuildTextVisual(TextElement text)
    {
        // ARCH-07: 예전에는 Canvas.SetLeft/Top으로 오프셋을 줘서 세 타입 중 텍스트만 배치 방식이 달랐고,
        // 그 탓에 변형 피벗이 어긋났다. 이제 RenderMatrixFor의 T(Origin) 항이 그 역할을 한다.
        return new TextBlock
        {
            Text = text.Text,
            FontFamily = new FontFamily(TextCommitRules.FontFamilyName),
            FontSize = text.FontSize,
            Foreground = CreateFrozen(text.Color),
        };
    }

    /// <summary>도형은 채우기 없이 외곽선만 (Round 13).</summary>
    public static Shape CreateShapeVisual(ShapeKind kind, Color color, double thickness)
    {
        // 도형은 날카로운 모서리 (사용자 조타): 둥근 조인/캡 대신 마이터/플랫.
        var path = new Path
        {
            Stroke = CreateFrozen(color),
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Miter,
            StrokeStartLineCap = PenLineCap.Flat,
            StrokeEndLineCap = PenLineCap.Flat,
            Fill = null,
        };
        return path;
    }

    public static void UpdateShapeVisual(Shape visual, ShapeKind kind, Point start, Point end)
    {
        var path = (Path)visual;
        switch (kind)
        {
            case ShapeKind.Line:
                path.Data = new LineGeometry(start, end);
                break;

            case ShapeKind.Arrow:
            {
                var group = new GeometryGroup();
                group.Children.Add(new LineGeometry(start, end));
                var (h1, h2) = ShapeGeometry.ArrowHead(start, end);
                group.Children.Add(new LineGeometry(end, h1));
                group.Children.Add(new LineGeometry(end, h2));
                path.Data = group;
                break;
            }

            case ShapeKind.Rectangle:
                path.Data = new RectangleGeometry(new Rect(start, end));
                break;

            case ShapeKind.Ellipse:
            {
                var rect = new Rect(start, end);
                path.Data = new EllipseGeometry(
                    new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2),
                    rect.Width / 2,
                    rect.Height / 2);
                break;
            }
        }
    }

    // ---- 선택 장식 (SEL-10). 잉크가 아니라 UI이므로 별도 레이어에 그리고 캡처에서 제외된다 (f4). ----

    private static readonly SolidColorBrush DecorationBrush = CreateFrozen(Color.FromRgb(0x00, 0xAD, 0xEF));
    private static readonly SolidColorBrush HandleFillBrush = CreateFrozen(Colors.White);
    private static readonly SolidColorBrush MarqueeFillBrush = CreateFrozen(Color.FromArgb(0x22, 0x00, 0xAD, 0xEF));

    /// <summary>로컬 프레임 4점(OBB) 위의 점선 경계. 축 정렬 <see cref="Rect"/>가 아니라 꼭짓점을 받는다 (MI-1).</summary>
    public static Polygon BuildSelectionBorder(Point[] corners)
    {
        var polygon = new Polygon
        {
            Stroke = DecorationBrush,
            StrokeThickness = 1,
            StrokeDashArray = [4, 3],
            Fill = null,
            IsHitTestVisible = false,
        };
        foreach (var corner in corners)
        {
            polygon.Points.Add(corner);
        }
        return polygon;
    }

    /// <summary>크기/회전 핸들 하나. <paramref name="center"/>는 월드 좌표이며 크기는 배율과 무관하게 일정하다.</summary>
    public static System.Windows.Shapes.Rectangle BuildHandle(Point center, double size)
    {
        var handle = new System.Windows.Shapes.Rectangle
        {
            Width = size,
            Height = size,
            Stroke = DecorationBrush,
            StrokeThickness = 1,
            Fill = HandleFillBrush,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(handle, center.X - size / 2);
        Canvas.SetTop(handle, center.Y - size / 2);
        return handle;
    }

    /// <summary>회전 핸들과 로컬 상단 변 중앙을 잇는 스템.</summary>
    public static Line BuildRotateStem(Point from, Point to) => new()
    {
        X1 = from.X,
        Y1 = from.Y,
        X2 = to.X,
        Y2 = to.Y,
        Stroke = DecorationBrush,
        StrokeThickness = 1,
        IsHitTestVisible = false,
    };

    /// <summary>마퀴 사각형. 핸들과 달리 **축 정렬**이다 (SEL-B-1).</summary>
    public static System.Windows.Shapes.Rectangle BuildMarquee(Rect rect)
    {
        var marquee = new System.Windows.Shapes.Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            Stroke = DecorationBrush,
            StrokeThickness = 1,
            StrokeDashArray = [3, 2],
            Fill = MarqueeFillBrush,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(marquee, rect.X);
        Canvas.SetTop(marquee, rect.Y);
        return marquee;
    }

    public static SolidColorBrush StrokeBrush(Color color, bool highlighter) =>
        CreateFrozen(highlighter ? Color.FromArgb(0x66, color.R, color.G, color.B) : color);

    public static SolidColorBrush CreateFrozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
