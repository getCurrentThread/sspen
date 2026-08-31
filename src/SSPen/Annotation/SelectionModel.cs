using System.Windows;

namespace SSPen.Annotation;

/// <summary>
/// 선택집합 (SEL-6). <b>전역 단일 인스턴스</b>로 모든 서피스가 공유한다 (옵션 B1).
///
/// 왜 전역인가: f7(모니터 경계를 넘는 이동)이 성립하려면 선택집합이 문서 경계를 넘어 살아야 한다.
/// 서피스별 집합(B3)은 이관 도중 두 서피스에 걸치는 순간을 표현할 수 없어 SEL-AC-5가 불가능하다.
///
/// 왜 <see cref="AppState"/> 필드가 아닌가 (B2 기각): <c>AppState.Changed</c>는 색·굵기·보드·가시성 등
/// 모든 하위 상태 변경에 발화하는 단일 이벤트라 퀵컬러 변경과 선택 변경을 구분할 수 없다.
/// 거기 구독하면 퀵컬러 클릭 한 번에 선택이 날아가 SEL-AC-17이 즉시 깨진다.
/// 따라서 해제 트리거는 <see cref="AppState.ActiveToolChanged"/> **전용 이벤트만**이다 (SEL-B-4).
/// </summary>
public sealed class SelectionModel
{
    private readonly List<AnnotationElement> _elements = [];
    private int _suppressDepth;

    /// <summary>선택된 요소 (선택된 순서).</summary>
    public IReadOnlyList<AnnotationElement> Elements => _elements;

    public int Count => _elements.Count;

    /// <summary>선택집합이 바뀌면 발생. 장식 레이어가 이것만 구독한다.</summary>
    public event Action? SelectionChanged;

    public bool Contains(AnnotationElement element) => _elements.Contains(element);

    /// <summary>선택집합을 통째로 교체 (마퀴 확정, 단일 클릭 선택).</summary>
    public void Set(IEnumerable<AnnotationElement> elements)
    {
        var next = elements.ToList();
        if (_elements.SequenceEqual(next))
        {
            return;
        }
        _elements.Clear();
        _elements.AddRange(next);
        SelectionChanged?.Invoke();
    }

    /// <summary>기존 선택에 추가 (Shift+클릭). 이미 있으면 무시.</summary>
    public void Add(AnnotationElement element)
    {
        if (_elements.Contains(element))
        {
            return;
        }
        _elements.Add(element);
        SelectionChanged?.Invoke();
    }

    /// <summary>Shift+클릭 토글: 없으면 추가, 있으면 제거.</summary>
    public void Toggle(AnnotationElement element)
    {
        if (!_elements.Remove(element))
        {
            _elements.Add(element);
        }
        SelectionChanged?.Invoke();
    }

    /// <summary>전체 해제 (빈 곳 클릭, 도구 전환, 전체 지우기).</summary>
    public void Clear()
    {
        if (_elements.Count == 0)
        {
            return;
        }
        _elements.Clear();
        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// 도구 전환 시 해제 (f11, SEL-B-4). <b><see cref="AppState.Changed"/>를 구독하지 않는다</b> —
    /// 그러면 퀵컬러·굵기·보드 변경에도 선택이 날아가 f12/SEL-AC-17이 깨진다.
    /// </summary>
    public void AttachTo(AppState state) => state.ActiveToolChanged += OnActiveToolChanged;

    /// <summary>
    /// 문서에서 사라진 요소를 선택집합에서 떨어뜨린다 (R17: 댕글링 참조 방지).
    /// 지우개·undo-of-Add·페이드 소멸이 모두 이 경로를 탄다.
    /// </summary>
    public void AttachTo(AnnotationDocument document) => document.ElementRemoved += OnElementRemoved;

    public void DetachFrom(AnnotationDocument document) => document.ElementRemoved -= OnElementRemoved;

    /// <summary>
    /// 소유권 이동 구간에서만 무효화를 억제한다 (LD-5). <b>적용 지점은 딱 둘</b>:
    /// P7 이관 절차와 P6 <c>TransformOperation.Undo</c>의 소유권 이동 분기.
    ///
    /// 왜 필요한가: <see cref="AnnotationDocument.Remove"/>는 성공 시 조건 없이
    /// <see cref="AnnotationDocument.ElementRemoved"/>를 발화하고, 이관 순서가 Remove → 보정 → Add이므로
    /// 억제가 없으면 <b>모니터를 넘겨 놓는 순간 선택이 통째로 비워진다</b> (SEL-AC-5 위반).
    ///
    /// 왜 <c>ownerLookup</c> 재조회가 아닌가: <c>ElementRemoved</c>가 발화하는 그 순간 요소는
    /// 어느 문서에도 없다 — 동기 재조회는 항상 null을 반환해 그대로 떨어뜨린다.
    ///
    /// 과잉 적용 금지: 이 스코프 밖의 진짜 제거(지우개·undo-of-Add·페이드)는 그대로 떨어져야 한다.
    /// </summary>
    public IDisposable SuppressInvalidation()
    {
        _suppressDepth++;
        return new SuppressionScope(this);
    }

    private void OnActiveToolChanged(ToolKind previous, ToolKind current) => Clear();

    private void OnElementRemoved(AnnotationElement element)
    {
        if (_suppressDepth > 0)
        {
            return;
        }
        if (_elements.Remove(element))
        {
            SelectionChanged?.Invoke();
        }
    }

    private sealed class SuppressionScope(SelectionModel owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            owner._suppressDepth--;
        }
    }
}
