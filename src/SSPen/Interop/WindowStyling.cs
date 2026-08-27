using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SSPen.Interop;

/// <summary>
/// exstyle 토글과 톱모스트 z-밴드 정책 (플랜 ARCH-5 / R10).
/// 밴드 순서(위→아래): 캡처 오버레이+액션바 > 툴바 > 콘텐츠 서피스 > 핀 > 기타 앱.
/// 표시/보드/핀 생성/캡처 세션/툴바 토글 전이마다 재적용한다.
/// </summary>
public static class WindowStyling
{
    public static nint GetHwnd(Window window) => new WindowInteropHelper(window).Handle;

    public static long GetExStyle(nint hwnd) => NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);

    public static void SetExStyleFlags(nint hwnd, long flags, bool on)
    {
        long value = GetExStyle(hwnd);
        long next = on ? value | flags : value & ~flags;
        if (next != value)
        {
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (nint)next);
        }
    }

    /// <summary>클릭 통과 (WS_EX_TRANSPARENT) 토글.</summary>
    public static void SetClickThrough(nint hwnd, bool on) =>
        SetExStyleFlags(hwnd, NativeMethods.WS_EX_TRANSPARENT, on);

    public static bool IsClickThrough(nint hwnd) =>
        (GetExStyle(hwnd) & NativeMethods.WS_EX_TRANSPARENT) != 0;

    /// <summary>도구 창 스타일 (Alt+Tab/작업 표시줄 제외).</summary>
    public static void SetToolWindow(nint hwnd, bool on) =>
        SetExStyleFlags(hwnd, NativeMethods.WS_EX_TOOLWINDOW, on);

    /// <summary>포커스 훔침 방지 (콘텐츠 서피스·캡처 액션바). 텍스트 도구는 WI-9 핸드셰이크로 일시 해제.</summary>
    public static void SetNoActivate(nint hwnd, bool on) =>
        SetExStyleFlags(hwnd, NativeMethods.WS_EX_NOACTIVATE, on);

    /// <summary>창을 물리 픽셀 사각형에 정확히 배치 (음수 원점 안전, R2).</summary>
    public static void PlacePhysical(nint hwnd, PhysicalRect bounds)
    {
        NativeMethods.SetWindowPos(
            hwnd, NativeMethods.HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// 창이 지정 앵커(예: 툴바) 위로 절대 올라가지 못하게 고정한다.
    /// 클릭/표시/재배치로 OS가 창을 밴드 최상단(HWND_TOPMOST/HWND_TOP)으로 올리려는 순간
    /// WM_WINDOWPOSCHANGING에서 삽입 위치를 앵커 바로 아래로 돌린다.
    /// (사용자 조타: 도구 선택 뒤 서피스가 툴바를 덮어 상호작용 불가 버그의 항구 수정.)
    /// 반환된 훅 델리게이트는 호출 측 필드로 붙잡아 GC를 막아야 한다.
    /// </summary>
    public static HwndSourceHook AnchorBelow(nint hwnd, Func<nint> anchorProvider)
    {
        HwndSourceHook hook = (nint h, int msg, nint wParam, nint lParam, ref bool handled) =>
        {
            if (msg == NativeMethods.WM_WINDOWPOSCHANGING)
            {
                var pos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
                bool zChanging = (pos.flags & NativeMethods.SWP_NOZORDER) == 0;
                bool risingToTop = pos.hwndInsertAfter == NativeMethods.HWND_TOPMOST
                    || pos.hwndInsertAfter == NativeMethods.HWND_TOP
                    || pos.hwndInsertAfter == NativeMethods.HWND_NOTOPMOST;
                if (zChanging && risingToTop)
                {
                    nint anchor = anchorProvider();
                    if (anchor != 0 && anchor != hwnd)
                    {
                        pos.hwndInsertAfter = anchor;
                        Marshal.StructureToPtr(pos, lParam, false);
                    }
                }
            }
            return 0;
        };
        AddHookOrThrow(hwnd, hook);
        return hook;
    }

    /// <summary>
    /// 지정 창이 톱모스트 밴드 밖으로 밀려나가지 않게 고정한다 (툴바 전용).
    ///
    /// <see cref="AnchorBelow"/>와 짝을 이룬다. 그쪽은 "서피스가 올라가는 교란"을 막고,
    /// 이쪽은 "툴바가 내려가는 교란"을 막는다. 둘은 별개의 사건이다 — 서피스 훅만 있을 때
    /// 외부 앱(전체화면 영상·게임, UAC, 세션 잠금, 다른 톱모스트 창)이 툴바의 WS_EX_TOPMOST를
    /// 벗기면 서피스가 툴바 위로 올라서도 서피스 훅은 발화하지 않아 아무도 복구하지 못한다.
    /// 그 상태에서는 툴바가 보이긴 하는데 클릭이 전부 서피스로 가 버튼이 죽는다
    /// (사용자 보고 18차: "그리는 중 갑자기 툴바가 안 눌림").
    /// 반환된 훅 델리게이트는 호출 측 필드로 붙잡아 GC를 막아야 한다.
    /// </summary>
    public static HwndSourceHook KeepTopmost(nint hwnd)
    {
        HwndSourceHook hook = (nint h, int msg, nint wParam, nint lParam, ref bool handled) =>
        {
            // WM_WINDOWPOSCHANGING에서 hwndInsertAfter를 보고 되돌리려는 접근은 통하지 않는다:
            // 실측 결과 Windows는 HWND_NOTOPMOST(-2) 같은 상수를 훅에 그대로 넘기지 않고
            // 이미 구체적인 HWND로 해석해서 준다 (실측: insertAfter=66294 flags=0x13).
            // 그래서 "밴드 밖으로 내리려는 의도"를 상수로는 구별할 수 없다.
            // 대신 변경이 끝난 뒤 exstyle을 본다 — WS_EX_TOPMOST가 벗겨졌는지가 결과적 진실이다.
            if (msg == NativeMethods.WM_WINDOWPOSCHANGED
                && (GetExStyle(hwnd) & NativeMethods.WS_EX_TOPMOST) == 0)
            {
                // 재귀 안전: 이 호출도 WM_WINDOWPOSCHANGED를 다시 낙지만, 그때는 TOPMOST가 복구된 뒤라
                // 위 조건이 거짓이 돼 즉시 멈춘다 (무한 루프 불가).
                NativeMethods.SetWindowPos(
                    hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                Diagnostics.Log.Info("툴바가 톱모스트 밴드 밖으로 밀려나 복구했다 (외부 앱 교란).");
            }
            return 0;
        };
        AddHookOrThrow(hwnd, hook);
        return hook;
    }

    /// <summary>
    /// 훅을 붙이되, 대상 <see cref="HwndSource"/>가 없으면 즉시 실패시킨다.
    /// 조용한 실패(<c>?.AddHook</c>)를 금지하는 이유: 훅이 안 붙으면 z-방어가 통째로 사라지는데,
    /// 그 증상은 "가끔 툴바가 안 눌림"라는 재현 어려운 형태로만 나타난다.
    /// </summary>
    private static void AddHookOrThrow(nint hwnd, HwndSourceHook hook)
    {
        var source = HwndSource.FromHwnd(hwnd)
            ?? throw new InvalidOperationException($"HwndSource를 찾지 못해 z-방어 훅을 붙일 수 없다 (hwnd={hwnd}).");
        source.AddHook(hook);
    }

    /// <summary>
    /// z-밴드 재적용. 목록은 위→아래 순서의 HWND. 첫 창을 톱모스트 최상단에 올린 뒤
    /// 나머지를 순서대로 그 아래에 삽입한다.
    /// </summary>
    public static void ApplyZBand(IReadOnlyList<nint> topToBottom)
    {
        const uint flags = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE;
        nint previous = 0;
        foreach (nint hwnd in topToBottom)
        {
            if (hwnd == 0)
            {
                continue;
            }
            NativeMethods.SetWindowPos(hwnd, previous == 0 ? NativeMethods.HWND_TOPMOST : previous, 0, 0, 0, 0, flags);
            previous = hwnd;
        }
    }
}
