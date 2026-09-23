using SSPen.Interop;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="AnchorBelowRules.IsRise"/> (54단계 L1). 실측(빌드 26200)으로 고정하는 것: 상수 셋은 상승, 자기 소유 창(깊이 1·2)
/// 바로 아래로 오는 요청도 상승, 남의 창·앵커·소유 사슬 밖의 창은 상승 아님, 소유 사슬은 상한에서 끊긴다.
/// <see cref="AnchorBelowRules.RedirectTarget"/> (74단계 A7-2): 요청 단계 재작성 판정 전체와 평가 순서
/// (밴드 적용 중 → SWP_NOZORDER → 상승 → 앵커 0/자기 자신) — 앞에서 걸리면 뒤 델리게이트는 불리지 않는다.
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

    private const nint Anchor = 0x6000;

    /// <summary>실측 모양: 활성화된 서피스의 상승·형제 삽입 둘 다 flags=0x13(NOSIZE|NOMOVE|NOACTIVATE)으로 온다.</summary>
    private const uint ZChangingFlags = NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE;

    private static nint AnchorMustNotBeRead() => throw new InvalidOperationException("앵커를 읽으면 안 된다");

    private static nint OwnerMustNotBeRead(nint hwnd) => throw new InvalidOperationException("소유자를 읽으면 안 된다");

    [Fact]
    public void RedirectTarget_ApplyingBand_ReturnsZero_WithoutReadingAnchorOrOwner()
    {
        // ApplyZBand의 형제 뒤 삽입은 "자기 IME 창 바로 아래"로 도착한다 — 상승과 모양이 같아도 밴드 적용 중이면 손대지 않는다 (AGENTS L15).
        nint target = AnchorBelowRules.RedirectTarget(
            ZChangingFlags, Ime, Self, applyingBand: true, AnchorMustNotBeRead, OwnerMustNotBeRead);

        Assert.Equal(0, target);
    }

    [Fact]
    public void RedirectTarget_NoZOrderFlag_ReturnsZero_WithoutReadingAnchor()
    {
        // z-순서를 바꾸지 않는 이동·크기 변경: insertAfter가 상승 상수여도 무시된다.
        nint target = AnchorBelowRules.RedirectTarget(
            ZChangingFlags | NativeMethods.SWP_NOZORDER, NativeMethods.HWND_TOP, Self, applyingBand: false,
            AnchorMustNotBeRead, OwnerMustNotBeRead);

        Assert.Equal(0, target);
    }

    [Fact]
    public void RedirectTarget_OwnImeInsert_ReturnsAnchor()
    {
        // 활성화된 적 있는 창의 상승(실측 flags=0x13 insertAfter=IME(owner=self)) → 앵커 바로 아래로 돌린다.
        int anchorReads = 0;

        nint target = AnchorBelowRules.RedirectTarget(
            ZChangingFlags, Ime, Self, applyingBand: false, () => { anchorReads++; return Anchor; }, OwnerOf);

        Assert.Equal(Anchor, target);
        Assert.Equal(1, anchorReads);
    }

    [Theory]
    [InlineData(-1)] // HWND_TOPMOST
    [InlineData(0)] // HWND_TOP
    [InlineData(-2)] // HWND_NOTOPMOST
    public void RedirectTarget_BandConstant_ReturnsAnchor_WithoutReadingOwner(int insertAfter)
    {
        nint target = AnchorBelowRules.RedirectTarget(
            ZChangingFlags, insertAfter, Self, applyingBand: false, () => Anchor, OwnerMustNotBeRead);

        Assert.Equal(Anchor, target);
    }

    [Fact]
    public void RedirectTarget_ForeignSiblingInsert_ReturnsZero_WithoutReadingAnchor()
    {
        // 남의 창(이전 핀·이전 서피스) 뒤 구체 삽입은 상승이 아니다 — 앵커는 상승 판정 뒤에만 읽는다.
        nint target = AnchorBelowRules.RedirectTarget(
            ZChangingFlags, Other, Self, applyingBand: false, AnchorMustNotBeRead, OwnerOf);

        Assert.Equal(0, target);
    }

    [Theory]
    [InlineData(0)] // 앵커 없음(툴바가 아직 없다)
    [InlineData((int)Self)] // 자기 자신 — 리터럴이 아니라 Self 상수에 묶는다 (92단계)
    public void RedirectTarget_AnchorZeroOrSelf_ReturnsZero(int anchor)
    {
        nint target = AnchorBelowRules.RedirectTarget(
            ZChangingFlags, Ime, Self, applyingBand: false, () => anchor, OwnerOf);

        Assert.Equal(0, target);
    }
}
