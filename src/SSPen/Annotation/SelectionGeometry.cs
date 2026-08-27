using System.Windows;

namespace SSPen.Annotation;

/// <summary>
/// 선택 힌트 판정 순수 헬퍼 (SEL-8). UI와 분리되어 헤드리스 유닛 테스트 대상이다.
///
/// 마퀴 판정은 **축 정렬 경계 상자** 대 마퀴 사각형의 교차다 (SEL-B-1, f9-a).
/// 잉크 실선 교차가 아니므로 화면을 가로지르는 긴 대각선 획은 잉크를 스치지 않아도 선택된다 —
/// 의도된 동작이며 Figma·Illustrator 관례와도 일치한다. MI-1이 핸들을 로컬축(OBB)으로 확정했어도
/// 이 판정은 **바뀌지 않는다**: OBB 교차(SAT)를 도입하지 않는다.
///
/// 페이딩 잉크 제외(f8)는 여기 두 함수에만 존재한다 (R11): 필터가 흩어지면
/// 조작 도중 대상이 증발하는 경로가 다시 열린다.
/// </summary>
public static class SelectionGeometry
{
    /// <summary>마퀴 사각형과 요소의 변형 적용 후 축 정렬 경계가 겹치는가. 접점도 겹침으로 본다.</summary>
    public static bool Intersects(Rect marquee, AnnotationElement element) =>
        !element.IsFading && !marquee.IsEmpty && marquee.IntersectsWith(element.TransformedBounds);

    /// <summary>마퀴에 걸린 요소 전부. **문서 순서를 보존**한다 (이관 시 상대 순서 근거 — SEL-B-3).</summary>
    public static IReadOnlyList<AnnotationElement> HitMarquee(
        IReadOnlyList<AnnotationElement> elements, Rect marquee)
    {
        var hits = new List<AnnotationElement>();
        foreach (var element in elements)
        {
            if (Intersects(marquee, element))
            {
                hits.Add(element);
            }
        }
        return hits;
    }

    /// <summary>
    /// 커서 아래의 **가장 위** 요소 (나중에 그린 것 우선).
    /// 지우개의 <see cref="AnnotationDocument.HitTestNearest"/>와 규칙이 다르다:
    /// 지우개는 '가장 가까운 것', 선택은 '가장 위'다.
    /// </summary>
    public static AnnotationElement? HitTopmost(
        IReadOnlyList<AnnotationElement> elements, Point p, double tolerance)
    {
        for (int i = elements.Count - 1; i >= 0; i--)
        {
            var element = elements[i];
            if (!element.IsFading && element.HitTest(p, tolerance))
            {
                return element;
            }
        }
        return null;
    }
}
