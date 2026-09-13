using SSPen.Interop;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="ZOrderInvariant"/> (54단계 L2/L3). z-순서를 위→아래 배열로 흉내 내고 이웃 조회 두 방향을 그 배열에서 만든다.
/// 잠그는 것: 직하가 아니어도 아래면 참, 위면 거짓, 목록에 없으면 거짓, 0/자기 자신은 거짓, 순서 검사는 사이에 낀 남의 창을 허용하고
/// 0 항목을 건너뛰며, 항목이 하나 이하면 참, 워크는 상한에서 끝난다.
/// </summary>
public class ZOrderInvariantTests
{
    /// <summary>위→아래 배열 기반 가짜 z-순서.</summary>
    private sealed class FakeZOrder(params nint[] topToBottom)
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
    }

    [Fact]
    public void IsBelow_AnchorDirectlyAbove_IsTrue()
    {
        var z = new FakeZOrder(10, 20);
        Assert.True(ZOrderInvariant.IsBelow(20, 10, z.Above));
    }

    [Fact]
    public void IsBelow_AnchorSeveralAbove_IsTrue()
    {
        // 툴바 → (IME 창·다른 서피스) → 서피스: 직하가 아니어도 아래다.
        var z = new FakeZOrder(10, 11, 12, 20);
        Assert.True(ZOrderInvariant.IsBelow(20, 10, z.Above));
    }

    [Fact]
    public void IsBelow_SelfAboveAnchor_IsFalse()
    {
        var z = new FakeZOrder(20, 10);
        Assert.False(ZOrderInvariant.IsBelow(20, 10, z.Above));
    }

    [Fact]
    public void IsBelow_AnchorAbsent_IsFalse()
    {
        var z = new FakeZOrder(30, 20);
        Assert.False(ZOrderInvariant.IsBelow(20, 10, z.Above));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(20, 0)]
    [InlineData(20, 20)]
    public void IsBelow_ZeroOrSelfAnchor_IsFalseWithoutWalking(long self, long anchor)
    {
        int calls = 0;
        Assert.False(ZOrderInvariant.IsBelow((nint)self, (nint)anchor, _ => { calls++; return 0; }));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void IsBelow_CyclicReadback_StopsAtMaxSteps()
    {
        int calls = 0;
        Assert.False(ZOrderInvariant.IsBelow(20, 10, _ => { calls++; return 20; }));
        Assert.Equal(ZOrderInvariant.MaxSteps, calls);
    }

    [Fact]
    public void IsOrdered_ExactOrder_IsTrue()
    {
        var z = new FakeZOrder(1, 2, 3);
        Assert.True(ZOrderInvariant.IsOrdered([1, 2, 3], z.Below));
    }

    [Fact]
    public void IsOrdered_ForeignWindowsInterleaved_IsTrue()
    {
        var z = new FakeZOrder(1, 90, 2, 91, 92, 3);
        Assert.True(ZOrderInvariant.IsOrdered([1, 2, 3], z.Below));
    }

    [Fact]
    public void IsOrdered_TwoSwapped_IsFalse()
    {
        // 서피스(2)가 툴바(1) 위로 올라간 상태.
        var z = new FakeZOrder(2, 1, 3);
        Assert.False(ZOrderInvariant.IsOrdered([1, 2, 3], z.Below));
    }

    [Fact]
    public void IsOrdered_LastMissing_IsFalse()
    {
        var z = new FakeZOrder(1, 2);
        Assert.False(ZOrderInvariant.IsOrdered([1, 2, 3], z.Below));
    }

    [Fact]
    public void IsOrdered_SkipsZeroEntries()
    {
        var z = new FakeZOrder(1, 3);
        Assert.True(ZOrderInvariant.IsOrdered([0, 1, 0, 3, 0], z.Below));
    }

    [Theory]
    [InlineData(new long[0])]
    [InlineData(new long[] { 7 })]
    [InlineData(new long[] { 0, 7, 0 })]
    public void IsOrdered_OneOrFewerEntries_IsTrueWithoutWalking(long[] entries)
    {
        int calls = 0;
        Assert.True(ZOrderInvariant.IsOrdered(entries.Select(e => (nint)e).ToList(), _ => { calls++; return 0; }));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void IsOrdered_CyclicReadback_StopsAtMaxSteps()
    {
        int calls = 0;
        Assert.False(ZOrderInvariant.IsOrdered([1, 2], _ => { calls++; return 1; }));
        Assert.Equal(ZOrderInvariant.MaxSteps, calls);
    }
}
