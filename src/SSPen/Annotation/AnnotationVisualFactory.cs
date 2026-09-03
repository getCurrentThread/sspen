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
            Data = StrokeGeometry.Create(stroke.Points, stroke.Pressures, stroke.Thickness, stroke.IsHighlighter),
            Fill = StrokeBrush(stroke.Color, stroke.IsHighlighter),
        };
        return path;
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

    /// <summary>
    /// 표 격자 지오메트리. 미리보기(<see cref="UpdateTableVisual"/>)와 커밋(<see cref="BuildVisual"/>)이 같은 함수를 쓴다.
    /// 외곽은 <b>닫힌 figure</b>(Miter 모서리) 하나, 내부 분할선은 <see cref="TableGeometry.Dividers"/> — 히트테스트와 같은 목록 (29단계).
    /// </summary>
    public static Geometry CreateTableGeometry(Point start, Point end, int rows, int columns)
    {
        var bounds = TableGeometry.Normalize(start, end);
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            // 외곽 사각형 — 닫힌 figure라 모서리가 Miter로 이어진다 (열린 선분 4개로 바꾸면 Flat 캡 노치가 생긴다).
            ctx.BeginFigure(bounds.TopLeft, isFilled: false, isClosed: true);
            ctx.LineTo(bounds.TopRight, isStroked: true, isSmoothJoin: false);
            ctx.LineTo(bounds.BottomRight, isStroked: true, isSmoothJoin: false);
            ctx.LineTo(bounds.BottomLeft, isStroked: true, isSmoothJoin: false);

            // 내부 분할선 (가로 rows−1 → 세로 columns−1) — TableElement 히트테스트와 같은 목록.
            foreach (var (a, b) in TableGeometry.Dividers(bounds, rows, columns))
            {
                ctx.BeginFigure(a, isFilled: false, isClosed: false);
                ctx.LineTo(b, isStroked: true, isSmoothJoin: false);
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

    // 값은 셸의 강조색(ShellPalette.Accent, #0071A8)과 같아야 한다 — 같은 앱에서 "선택됨"을 뜻하는 색이
    // 두 가지면 사용자가 둘을 다른 의미로 읽는다. 여기서 Shell을 참조하지 않는 이유는 계층 규약이다
    // (Annotation/은 using SSPen.Shell 금지). 대신 SelectionDecorationColorTests가 두 값이 갈라지면 빨간불을 낸다.
    private static readonly SolidColorBrush DecorationBrush = CreateFrozen(Color.FromRgb(0x00, 0x71, 0xA8));
    private static readonly SolidColorBrush HandleFillBrush = CreateFrozen(Colors.White);
    private static readonly SolidColorBrush MarqueeFillBrush = CreateFrozen(Color.FromArgb(0x22, 0x00, 0x71, 0xA8));

    /// <summary>핸들 외곽선 두께: 1px 선은 검은 보드·복잡한 배경 위에서 사라진다.</summary>
    private const double HandleStrokeThickness = 2;

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

    /// <summary>크기 핸들 하나 (사각형). <paramref name="center"/>는 월드 좌표이며 크기는 배율과 무관하게 일정하다.</summary>
    public static System.Windows.Shapes.Rectangle BuildHandle(Point center, double size)
    {
        var handle = new System.Windows.Shapes.Rectangle
        {
            Width = size,
            Height = size,
            Stroke = DecorationBrush,
            StrokeThickness = HandleStrokeThickness,
            Fill = HandleFillBrush,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(handle, center.X - size / 2);
        Canvas.SetTop(handle, center.Y - size / 2);
        return handle;
    }

    /// <summary>
    /// 회전 핸들 (원). 크기 핸들과 <b>모양</b>으로 구분한다 — 예전에는 아홉 개가 전부 같은 사각형이라
    /// 위치(상단 바깥)를 외우는 것 말고는 어느 것이 회전인지 알 방법이 없었고, 회전 핸들이 화면
    /// 가장자리에서 클램프되면 그 위치 단서마저 사라진다.
    /// 지름은 크기 핸들과 <b>같다</b> — 그려진 것이 잡히는 것보다 커지면 "그려진 것 == 잡히는 것"
    /// 보증이 깨진다 (히트 reach는 양쪽 다 size/2다).
    /// </summary>
    public static Ellipse BuildRotateHandle(Point center, double size)
    {
        var handle = new Ellipse
        {
            Width = size,
            Height = size,
            Stroke = DecorationBrush,
            StrokeThickness = HandleStrokeThickness,
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

    // ---- 표 드래그 HUD 배지 (방안 2, 26단계). 잉크가 아니라 UI지만 잉크 캔버스 위에 잠깐 떠 있는 일회성 힌트다. ----

    /// <summary>배지가 포인터에서 떨어져 앉는 오프셋 (논리 px).</summary>
    public const double TableBadgeOffset = 16;

    /// <summary>
    /// 표 드래그 HUD 배지. <paramref name="text"/>는 호출자(창)가 넘긴다 — 이 파일은 사용자 문자열을 모른다
    /// (Strings는 Shell의 것이고 합성 루트가 창에 포맷터를 주입한다). 시각 구성은 948b037 그대로.
    /// </summary>
    public static Border BuildTableBadge(string text, Point anchor)
    {
        var badge = new Border
        {
            Background = CreateFrozen(Color.FromArgb(0xDD, 0x1E, 0x1E, 0x1E)),
            BorderBrush = CreateFrozen(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(7, 3, 7, 3),
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            IsHitTestVisible = false,
        };
        UpdateTableBadge(badge, text, anchor);
        return badge;
    }

    /// <summary>텍스트·위치만 갱신한다 — 매 포인터 이동마다 불리므로 재구축하지 않는다 (오늘과 같은 비용).</summary>
    public static void UpdateTableBadge(Border badge, string text, Point anchor)
    {
        ((TextBlock)badge.Child).Text = text;
        Canvas.SetLeft(badge, anchor.X + TableBadgeOffset);
        Canvas.SetTop(badge, anchor.Y + TableBadgeOffset);
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
