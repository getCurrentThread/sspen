using SSPen.Interop;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="AnchorBelowRules.IsRise"/> (54단계 L1). 실측(빌드 26200)으로 고정하는 것: 상수 셋은 상승, 자기 소유 창(깊이 1·2)
/// 바로 아래로 오는 요청도 상승, 남의 창·앵커·소유 사슬 밖의 창은 상승 아님, 소유 사슬은 상한에서 끊긴다.
/// </summary>
public class AnchorBelowRulesTests
{
    private const nint Self = 0x1000;
    private const nint Ime = 0x2000;
    private const nint CtfUi = 0x3000;
    private const nint Other = 0x4000;
    private const nint OtherOwned = 0x5000;

    /// <summary>소유 사슬 가짜: MSCTFIME UI → IME → Self, OtherOwned → Other.</summary>
    private static nint OwnerOf(nint hwnd) => hwnd switch
    {
        Ime => Self,
        CtfUi => Ime,
        OtherOwned => Other,
        _ => 0,
    };

    [Theory]
    [InlineData(-1)] // HWND_TOPMOST — 실측으로는 훅에 오지 않지만 방어적 잔재
    [InlineData(0)] // HWND_TOP — TOPMOST가 정규화되어 오는 값
    [InlineData(-2)] // HWND_NOTOPMOST
    public void IsRise_BandConstants_AreRise(int insertAfter)
    {
        Assert.True(AnchorBelowRules.IsRise(insertAfter, Self, OwnerOf));
    }

    [Fact]
    public void IsRise_InsertAfterOwnImeWindow_IsRise()
    {
        // 활성화된 적 있는 창의 상승 요청은 hwndInsertAfter = 자기 IME 창으로 온다 (실측 S4/S7/S8).
        Assert.True(AnchorBelowRules.IsRise(Ime, Self, OwnerOf));
    }

    [Fact]
    public void IsRise_InsertAfterGrandOwnedWindow_IsRise()
    {
        // MSCTFIME UI → IME → Self: 깊이 2도 사슬을 따라 자기 자신에 닿는다.
        Assert.True(AnchorBelowRules.IsRise(CtfUi, Self, OwnerOf));
    }

    [Fact]
    public void IsRise_InsertAfterForeignWindow_IsNotRise()
    {
        // ApplyZBand의 구체 삽입(툴바·이전 핀·이전 서피스)은 상승이 아니다 — 여기서 상승으로 읽으면 밴드 적용 자체가 앵커 아래로 뒤틀린다.
        Assert.False(AnchorBelowRules.IsRise(Other, Self, OwnerOf));
    }

    [Fact]
    public void IsRise_InsertAfterWindowOwnedByForeign_IsNotRise()
    {
        Assert.False(AnchorBelowRules.IsRise(OtherOwned, Self, OwnerOf));
    }

    [Fact]
    public void IsRise_InsertAfterSelf_IsNotRise()
    {
        int calls = 0;
        Assert.False(AnchorBelowRules.IsRise(Self, Self, h => { calls++; return 0; }));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void IsRise_OwnerChainCycle_StopsAtMaxDepth()
    {
        // 소유 사슬이 순환하면(손상된 리드백) 상한에서 끊고 상승 아님으로 본다 — 훅 안에서 무한 루프는 곧 UI 정지다.
        int calls = 0;
        nint Cycle(nint h) { calls++; return h == Other ? OtherOwned : Other; }

        Assert.False(AnchorBelowRules.IsRise(Other, Self, Cycle));
        Assert.Equal(AnchorBelowRules.MaxOwnerDepth, calls);
    }
}
