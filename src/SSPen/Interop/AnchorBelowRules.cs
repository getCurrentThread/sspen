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
///
/// 훅이 "삽입 위치를 앵커로 바꿀까"를 정하는 판정 전체는 <see cref="RedirectTarget"/>이 가진다 (74단계 A7-2) —
/// 훅은 WINDOWPOS를 읽고 쓰는 부작용만 한다.
/// </summary>
public static class AnchorBelowRules
{
    /// <summary>소유 사슬 추적 상한. IME 사슬은 깊이 2(<c>MSCTFIME UI</c> → <c>IME</c> → 소유자)다.</summary>
    public const int MaxOwnerDepth = 8;

    /// <summary>
    /// 요청 단계(<c>WM_WINDOWPOSCHANGING</c>)에서 <c>hwndInsertAfter</c>를 무엇으로 바꿀지 정한다. 0이면 손대지 않고,
    /// 0이 아니면 그 HWND(앵커)로 돌린다. 평가 순서가 계약이다 — 앞 조건 하나라도 걸리면 뒤 델리게이트는 부르지 않는다:
    /// <list type="number">
    /// <item><paramref name="applyingBand"/>(<c>WindowStyling.ApplyZBand</c>가 도는 중) → 0. 밴드 적용의 형제 뒤 삽입은
    /// 활성화된 창에서 "자기 IME 창 바로 아래"로 도착해 상승과 구별되지 않는다 (AGENTS L15).</item>
    /// <item><paramref name="flags"/>에 <c>SWP_NOZORDER</c> → 0. z-순서를 바꾸지 않는 이동·크기 변경이다.</item>
    /// <item><see cref="IsRise"/>가 거짓 → 0. 남의 창 뒤 구체 삽입은 밴드 적용이나 의도된 형제 삽입이다.</item>
    /// <item><paramref name="anchor"/>를 <b>여기서 처음</b> 읽는다. 0(앵커 없음)이거나 자기 자신이면 → 0.</item>
    /// </list>
    /// 모두 통과하면 앵커를 돌려준다. 결과 단계(<c>WindowStyling.KeepBelow</c>)와 달리 앵커가 살아 있는지(<c>IsWindow</c>)는
    /// 보지 않는다 — 54단계부터의 비대칭이며 이 추출에서 바꾸지 않았다(동작 보존).
    /// </summary>
    /// <param name="flags">WINDOWPOS의 <c>flags</c>.</param>
    /// <param name="insertAfter">WINDOWPOS의 <c>hwndInsertAfter</c>(OS가 정규화한 값).</param>
    /// <param name="self">훅이 붙은 창.</param>
    /// <param name="applyingBand">밴드 적용 중인가(<c>WindowStyling._applyingBand</c>). 훅은 이것이 참이면 WINDOWPOS를 읽기도 전에
    /// 끊으므로 프로덕션에서는 늘 거짓으로 오지만, 억제 규칙의 순수 계약으로 여기에 남긴다.</param>
    /// <param name="anchor">앵커 조회(서피스 = 맨 아래 핀 또는 툴바, 핀 = 툴바). 상승으로 판정된 뒤에만 부른다.</param>
    /// <param name="ownerOf">소유자 조회(<c>GW_OWNER</c>) — <see cref="IsRise"/>에 넘긴다.</param>
    public static nint RedirectTarget(
        uint flags, nint insertAfter, nint self, bool applyingBand, Func<nint> anchor, Func<nint, nint> ownerOf)
    {
        if (applyingBand)
        {
            return 0;
        }
        if ((flags & NativeMethods.SWP_NOZORDER) != 0)
        {
            return 0;
        }
        if (!IsRise(insertAfter, self, ownerOf))
        {
            return 0;
        }
        nint a = anchor();
        if (a == 0 || a == self)
        {
            return 0;
        }
        return a;
    }

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
