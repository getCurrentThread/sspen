using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace SSPen.Annotation;

/// <summary>
/// 획 지오메트리와 필압 정책의 단일 소유 지점 (31단계, R8). 필압 클램프(0.05..1.0)가 세 곳(팩토리·누적기 시작·누적기 추가)에,
/// 기본 필압 0.5가 여덟 곳에 리터럴로 있었다 — R8 튜닝 때 드리프트가 예정된 사본이다. WPF Ink 엔진 의존
/// (<see cref="System.Windows.Ink.Stroke"/>, <see cref="StylusPointCollection"/>)도 이 파일 안으로 캡슐화한다.
/// 미리보기(<c>SurfaceInputController.UpdateActiveStrokeVisual</c>)와 커밋(<see cref="AnnotationVisualFactory.BuildVisual"/>)이
/// 같은 <see cref="Create"/>를 부른다.
/// </summary>
public static class StrokeGeometry
{
    /// <summary>필압 정보가 없는 입력(마우스)의 기본 필압.</summary>
    public const float DefaultPressure = 0.5f;

    /// <summary>필압 하한 — 0이면 Ink 엔진이 폭 0 세그먼트를 만든다.</summary>
    public const float MinPressure = 0.05f;

    public const float MaxPressure = 1.0f;

    public static float ClampPressure(float pressure) => Math.Clamp(pressure, MinPressure, MaxPressure);

    /// <summary>
    /// 점들과 필압 정보로부터 WPF Ink 엔진 기반의 매끄러운 가변 두께 아웃라인 지오메트리를 생성한다.
    /// <paramref name="pressures"/>가 없거나 짧으면 <see cref="DefaultPressure"/>를 쓴다.
    /// </summary>
    public static Geometry Create(IReadOnlyList<Point> points, IReadOnlyList<float>? pressures, double thickness, bool isHighlighter)
    {
        var spc = new StylusPointCollection();
        for (int i = 0; i < points.Count; i++)
        {
            float p = (pressures != null && i < pressures.Count) ? pressures[i] : DefaultPressure;
            spc.Add(new StylusPoint(points[i].X, points[i].Y, ClampPressure(p)));
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
}
