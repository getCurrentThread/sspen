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

    /// <summary>
    /// 선택 전용 관대 히트 (R6). 2단계다:
    /// <list type="number">
    /// <item>잉크 실선 근처 정확 히트가 있으면 <b>그것이 이긴다</b> (<see cref="HitTopmost"/>와 동일 규칙).</item>
    /// <item>없으면 커서를 품는 경계 상자 중 <b>면적이 가장 작은</b> 요소. 동률이면 위쪽 요소.</item>
    /// </list>
    ///
    /// 왜 면적 최소인가: 무조건 경계 상자 히트로 바꾸면 화면을 가로지르는 대각선 획 하나가
    /// 화면 전체의 클릭 표적이 되어 그 아래 어떤 요소도 고를 수 없다. 면적 순위는 "작은 것이 위에
    /// 있다"는 시각적 직관과 일치하고, 큰 도형 안의 작은 글씨를 고를 수 있게 한다.
    ///
    /// 왜 <see cref="AnnotationElement.HitTest"/>를 고치지 않는가: 그 함수는 <b>지우개</b>
    /// (<see cref="AnnotationDocument.HitTestNearest"/>)와 공유된다. 거기까지 면적 히트로 바꾸면
    /// "사각형 안 글씨를 지우려다 사각형이 지워지는" 랭킹 붕괴가 생기므로 선택 경로에만 둔다.
    /// </summary>
    public static AnnotationElement? HitForSelect(
        IReadOnlyList<AnnotationElement> elements, Point p, double tolerance)
    {
        if (HitTopmost(elements, p, tolerance) is { } exact)
        {
            return exact;
        }

        AnnotationElement? best = null;
        double bestArea = double.MaxValue;
        for (int i = elements.Count - 1; i >= 0; i--)
        {
            var element = elements[i];
            if (element.IsFading)
            {
                continue;
            }
            var bounds = element.TransformedBounds;
            if (!bounds.Contains(p))
            {
                continue;
            }
            double area = bounds.Width * bounds.Height;
            if (area < bestArea)
            {
                bestArea = area;
                best = element;
            }
        }
        return best;
    }

    /// <summary>
    /// 점이 요소의 <b>로컬 프레임(OBB)</b> 안에 있는가 (R6: 이미 선택된 요소는 안쪽 아무 데나 잡아 옮긴다).
    /// 축 정렬 <see cref="AnnotationElement.TransformedBounds"/>가 아니라 회전을 반영한 4점을 쓴다 —
    /// 화면에 그려진 점선 경계와 잡히는 영역이 일치해야 하기 때문이다.
    /// </summary>
    public static bool ContainsInFrame(AnnotationElement element, Point p)
    {
        var corners = element.TransformedCorners();
        // 볼록 사각형 내부 판정: 네 변에 대한 외적 부호가 모두 같으면 내부. 경계(0)는 내부로 본다.
        bool negative = false;
        bool positive = false;
        for (int i = 0; i < corners.Length; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % corners.Length];
            double cross = ((b.X - a.X) * (p.Y - a.Y)) - ((b.Y - a.Y) * (p.X - a.X));
            if (cross < 0)
            {
                negative = true;
            }
            else if (cross > 0)
            {
                positive = true;
            }
            if (negative && positive)
            {
                return false;
            }
        }
        return true;
    }
}
