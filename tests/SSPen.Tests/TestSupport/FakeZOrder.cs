namespace SSPen.Tests;

/// <summary>
/// 위→아래 배열 기반 가짜 z-순서 (54단계 ZOrderInvariantTests 출신, 58단계 A8-5에서 승격).
/// 이웃 조회 두 방향(<see cref="Above"/>/<see cref="Below"/>)을 배열에서 만든다 — 목록에 없거나 끝이면 0이다.
/// <see cref="SetOrder"/>는 가짜 복구(apply)가 순서를 바꾸는 시나리오용이다(z-밴드 검증기 증인).
/// </summary>
internal sealed class FakeZOrder(params nint[] topToBottom)
{
    private readonly List<nint> _order = [.. topToBottom];

    public nint Above(nint hwnd)
    {
        int i = _order.IndexOf(hwnd);
        return i <= 0 ? 0 : _order[i - 1];
    }

    public nint Below(nint hwnd)
    {
        int i = _order.IndexOf(hwnd);
        return i < 0 || i == _order.Count - 1 ? 0 : _order[i + 1];
    }

    /// <summary>위→아래 배열을 통째로 바꾼다 (생성자와 같은 의미).</summary>
    public void SetOrder(params nint[] topToBottom)
    {
        _order.Clear();
        _order.AddRange(topToBottom);
    }
}
