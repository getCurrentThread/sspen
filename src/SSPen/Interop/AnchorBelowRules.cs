namespace SSPen.Interop;

/// <summary>
/// <see cref="WindowStyling.AnchorBelow"/>의 순수 판정 (54단계 L1). 훅이 본 <c>hwndInsertAfter</c>가
/// "밴드 최상단으로 올리려는 요청"인지 결정한다 — OS 호출 없이, 소유자 조회는 델리게이트로 받는다.
///
/// 실측(빌드 26200)으로 고친 두 사실:
/// - 훅은 <c>HWND_TOPMOST(-1)</c>를 절대 보지 못한다 — OS가 <c>HWND_TOP(0)</c>으로 정규화해 넘긴다.
///   <c>HWND_NOTOPMOST(-2)</c>는 구체 HWND로 온다. 상수 셋을 계속 보는 것은 방어적 잔재다.
/// - 창이 한 번이라도 <b>활성화</b>되면(텍스트 도구의 <c>Activate()</c>) 스레드 기본 IME 창(<c>IME</c>/<c>MSCTFIME UI</c>)의
///   소유자가 되고, 그 뒤 이 창을 올리려는 모든 요청은 <c>hwndInsertAfter = 그 소유 창</c>으로 도착한다
///   (소유 창은 항상 소유자 바로 위에 놓이므로 "내 소유 창 바로 아래" = "밴드 최상단"이다). 상수만 보던 판정은 여기서
///   눈이 멀어 서피스가 툴바를 덮었다 (사용자 보고: 자유선 로테이션 중 텍스트 도구가 한 번 선택된 뒤 재발).
///   그래서 <c>hwndInsertAfter</c>의 소유 사슬을 따라 올라가 자기 자신이 나오면 상승으로 본다.
/// </summary>
public static class AnchorBelowRules
{
    /// <summary>소유 사슬 추적 상한. IME 사슬은 깊이 2(<c>MSCTFIME UI</c> → <c>IME</c> → 소유자)다.</summary>
    public const int MaxOwnerDepth = 8;

    /// <summary>
    /// <paramref name="insertAfter"/>가 <paramref name="self"/>를 밴드 최상단으로 올리는 요청인가.
    /// <paramref name="ownerOf"/>는 소유자 HWND(없으면 0)를 돌려준다.
    /// </summary>
    public static bool IsRise(nint insertAfter, nint self, Func<nint, nint> ownerOf)
    {
        if (insertAfter == NativeMethods.HWND_TOPMOST
            || insertAfter == NativeMethods.HWND_TOP
            || insertAfter == NativeMethods.HWND_NOTOPMOST)
        {
            return true;
        }
        if (insertAfter == self)
        {
            return false;
        }
        nint w = insertAfter;
        for (int depth = 0; depth < MaxOwnerDepth; depth++)
        {
            w = ownerOf(w);
            if (w == 0)
            {
                return false;
            }
            if (w == self)
            {
                return true;
            }
        }
        return false;
    }
}
