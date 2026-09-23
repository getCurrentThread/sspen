namespace SSPen.Interop;

/// <summary>
/// z-순서 <b>결과</b> 판정의 순수 핵심 (54단계 L2/L3). 이웃 조회는 델리게이트로 받아 OS 없이 돈다.
/// <see cref="WindowStyling.AnchorBelow"/>는 요청(<c>WM_WINDOWPOSCHANGING</c>)을 고치는 사전 방어고,
/// 이 판정은 적용된 뒤(<c>WM_WINDOWPOSCHANGED</c>·WinEvent)의 실제 순서를 읽어 위반만 잡는 사후 방어다 —
/// 요청 해석이 또 빗나가더라도(새 OS 빌드의 다른 정규화) 결과는 같은 불변식으로 잡힌다.
/// </summary>
public static class ZOrderInvariant
{
    /// <summary>워크 상한. 최상위 창은 보통 수백 개다 — 사슬이 끊긴 리드백에서도 유한하게 끝나야 한다.</summary>
    public const int MaxSteps = 4096;

    /// <summary>
    /// <paramref name="self"/>가 <paramref name="anchor"/> 아래에 있는가. <paramref name="above"/>는 z-순서상 바로 위 창
    /// (<c>GW_HWNDPREV</c>, 최상단이면 0)을 돌려준다. self에서 위로 올라가며 anchor를 만나면 참이다 —
    /// 위반이 아닐 때 걸음 수가 가장 적은 방향이다(핀·서피스는 앵커 — 툴바, 맨 아래 핀 — 바로 아래 몇 칸 안에 있다, 71단계).
    /// anchor를 끝내 못 만나면(anchor가 아래에 있거나 목록에 없음) 거짓이다.
    /// </summary>
    public static bool IsBelow(nint self, nint anchor, Func<nint, nint> above)
    {
        if (self == 0 || anchor == 0 || self == anchor)
        {
            return false;
        }
        nint w = self;
        for (int step = 0; step < MaxSteps; step++)
        {
            w = above(w);
            if (w == 0)
            {
                return false;
            }
            if (w == anchor)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// <paramref name="topToBottom"/>의 창들이 그 순서대로 놓여 있는가 (사이에 남의 창이 끼는 것은 허용).
    /// <paramref name="below"/>는 z-순서상 바로 아래 창(<c>GW_HWNDNEXT</c>, 바닥이면 0)을 돌려준다.
    /// 첫 창에서 아래로 내려가며 나머지를 차례로 만나야 한다. 0인 항목은 건너뛴다. 항목이 하나 이하면 참이다.
    /// </summary>
    public static bool IsOrdered(IReadOnlyList<nint> topToBottom, Func<nint, nint> below)
    {
        var expected = new List<nint>(topToBottom.Count);
        foreach (nint hwnd in topToBottom)
        {
            if (hwnd != 0)
            {
                expected.Add(hwnd);
            }
        }
        if (expected.Count < 2)
        {
            return true;
        }
        int next = 1;
        nint w = expected[0];
        for (int step = 0; step < MaxSteps; step++)
        {
            w = below(w);
            if (w == 0)
            {
                return false;
            }
            if (w == expected[next])
            {
                next++;
                if (next == expected.Count)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
