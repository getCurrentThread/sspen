using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SSPen.Interop;

/// <summary>
/// exstyle 토글과 톱모스트 z-밴드 정책 (플랜 ARCH-5 / R10).
/// 밴드 순서(위→아래): 토스트 > 설정창 > 캡처 오버레이+액션바 > 툴바 > 핀 > 콘텐츠 서피스(보드) > 기타 앱
/// (71단계 사용자 결정, 정책은 <c>Shell/ZBandOrder</c>). 앵커는 핀 = 툴바, 서피스 = 맨 아래 핀(없으면 툴바).
/// 표시/보드/핀 생성/캡처 세션/툴바 토글 전이마다 재적용한다.
/// 방어는 세 겹이다 (54단계): 요청 단계 <see cref="AnchorBelow"/>(서피스·핀) → 결과 단계 <see cref="KeepBelow"/>(서피스·핀) +
/// <see cref="KeepTopmost"/>(툴바) → 사후 검증(<c>Shell/ZBandVerifier</c>, WinEvent — 72단계). 순수 판정은
/// <see cref="AnchorBelowRules"/>·<see cref="ZOrderInvariant"/>가 가진다.
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
    /// 창을 물리 픽셀 위치로 옮긴다 (크기는 건드리지 않는다 — <c>SizeToContent</c> 창용).
    /// DIP인 <c>Window.Left/Top</c> 대입과 달리 배율이 섞일 여지가 없다.
    /// </summary>
    public static void MovePhysical(nint hwnd, int x, int y)
    {
        NativeMethods.SetWindowPos(
            hwnd, 0, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// 창의 위치와 크기를 물리 픽셀 사각형으로 <b>한 번의</b> <c>SetWindowPos</c>에 함께 바꾼다 (z-순서·활성화는 건드리지 않는다).
    /// DIP인 <c>Left/Top/Width/Height</c>를 따로 대입하면 WPF가 속성마다 <c>SetWindowPos</c>를 불러 "이동만 된" "크기만 바뀐"
    /// 중간 사각형이 레이어드 창에 그대로 비친다 — 핀 휠 확대의 떨림이 그것이었다 (82단계, 사용자 신고).
    /// WPF는 이 호출이 보내는 WM_MOVE/WM_SIZE로 Left·Top·Width·Height를 스스로 맞추고(Window.WmMoveChanged/WmSizeChanged),
    /// 옛 크기로 되돌리는 <c>SetWindowPos</c>를 내지 않는다 (실측: 칸당 WM_WINDOWPOSCHANGED 1회, Width/Height 동기화 — PinZoomSmoothnessTests).
    /// 호출자는 핀 확대/축소와 토스트 배치다.
    /// </summary>
    public static void MoveResizePhysical(nint hwnd, PhysicalRect bounds)
    {
        NativeMethods.SetWindowPos(
            hwnd, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// 창이 지정 앵커(예: 툴바) 위로 절대 올라가지 못하게 고정한다 — <b>요청 단계</b> 방어.
    /// 클릭/표시/재배치/활성화로 OS가 창을 밴드 최상단으로 올리려는 순간 WM_WINDOWPOSCHANGING에서 삽입 위치를
    /// 앵커 바로 아래로 돌린다. "올리려는 요청"의 판정은 <see cref="AnchorBelowRules.IsRise"/>가 소유한다 —
    /// 상수(HWND_TOP 등)뿐 아니라 <b>자기 소유 창 바로 아래</b>(활성화된 적 있는 창의 IME 창)로 오는 요청도 상승이다 (54단계 L1).
    /// 바꿀지·무엇으로 바꿀지의 판정 전체(밴드 적용 중 억제 → SWP_NOZORDER → 상승 → 앵커 0/자기 자신)는
    /// <see cref="AnchorBelowRules.RedirectTarget"/>이 가진다 (74단계 A7-2). 이 훅은 WINDOWPOS 읽기·쓰기만 한다.
    /// (사용자 조타: 도구 선택 뒤 서피스가 툴바를 덮어 상호작용 불가 버그의 항구 수정.)
    /// 반환된 훅 델리게이트는 호출 측 필드로 붙잡아 GC를 막아야 한다.
    /// </summary>
    public static HwndSourceHook AnchorBelow(nint hwnd, Func<nint> anchorProvider)
    {
        HwndSourceHook hook = (nint h, int msg, nint wParam, nint lParam, ref bool handled) =>
        {
            // 밴드 적용 중이면 WINDOWPOS를 읽지도 않고 끊는다 — 추출 전과 같은 부작용 순서. RedirectTarget의 applyingBand 인자는
            // 같은 억제 규칙의 순수 계약이다(여기서는 늘 거짓).
            if (msg == NativeMethods.WM_WINDOWPOSCHANGING && !_applyingBand)
            {
                var pos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
                nint target = AnchorBelowRules.RedirectTarget(
                    pos.flags, pos.hwndInsertAfter, hwnd, _applyingBand, anchorProvider, OwnerOf);
                if (target != 0)
                {
                    pos.hwndInsertAfter = target;
                    Marshal.StructureToPtr(pos, lParam, false);
                }
            }
            return 0;
        };
        AddHookOrThrow(hwnd, hook);
        return hook;
    }

    /// <summary>
    /// 창이 앵커 아래에 <b>실제로</b> 놓였는지를 z-변경이 끝난 뒤 확인하고, 위에 있으면 앵커 바로 아래로 다시 넣는다 —
    /// <b>결과 단계</b> 방어 (54단계 L2). <see cref="AnchorBelow"/>가 요청을 잘못 읽는 새 경우(다른 OS 빌드의 정규화)가 생겨도
    /// 결과는 같은 불변식으로 잡힌다. 재삽입은 WM_WINDOWPOSCHANGED를 다시 낳지만 그때는 앵커 아래라 판정이 거짓이 되고,
    /// 그래도 래치가 재진입을 한 번 더 막는다. 앵커가 없거나(0) 자기 자신이거나 이미 파괴됐으면 아무것도 하지 않는다.
    /// <paramref name="label"/>은 로그용 이름이다. 반환된 훅 델리게이트는 호출 측 필드로 붙잡아 GC를 막아야 한다.
    /// </summary>
    public static HwndSourceHook KeepBelow(nint hwnd, Func<nint> anchorProvider, string label)
    {
        bool repairing = false;
        HwndSourceHook hook = (nint h, int msg, nint wParam, nint lParam, ref bool handled) =>
        {
            if (msg != NativeMethods.WM_WINDOWPOSCHANGED || repairing)
            {
                return 0;
            }
            var pos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
            if ((pos.flags & NativeMethods.SWP_NOZORDER) != 0)
            {
                return 0;
            }
            nint anchor = anchorProvider();
            if (anchor == 0 || anchor == hwnd || !NativeMethods.IsWindow(anchor))
            {
                return 0;
            }
            if (ZOrderInvariant.IsBelow(hwnd, anchor, Above))
            {
                return 0;
            }
            repairing = true;
            try
            {
                bool ok = NativeMethods.SetWindowPos(
                    hwnd, anchor, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                Diagnostics.Log.Info(
                    $"{label}이(가) 앵커 위로 올라와 되돌렸다 (insertAfter=0x{pos.hwndInsertAfter:X} flags=0x{pos.flags:X} 성공={ok}).");
            }
            finally
            {
                repairing = false;
            }
            return 0;
        };
        AddHookOrThrow(hwnd, hook);
        return hook;
    }

    /// <summary>
    /// 창의 z-순서가 바뀔 때마다(WM_WINDOWPOSCHANGED, SWP_NOZORDER 아님) <paramref name="onChanged"/>를 부른다 (54단계 L2, 툴바용).
    /// 툴바는 자기 아래 창들을 모르므로 여기서 고치지 않는다 — 합성 루트의 밴드 검증을 깨울 뿐이다. 콜백 안에서 SetWindowPos를
    /// 직접 부르지 말고 Dispatcher로 미뤄라. 반환된 훅 델리게이트는 호출 측 필드로 붙잡아 GC를 막아야 한다.
    /// </summary>
    public static HwndSourceHook OnZOrderChanged(nint hwnd, Action onChanged)
    {
        HwndSourceHook hook = (nint h, int msg, nint wParam, nint lParam, ref bool handled) =>
        {
            if (msg == NativeMethods.WM_WINDOWPOSCHANGED)
            {
                var pos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
                if ((pos.flags & NativeMethods.SWP_NOZORDER) == 0)
                {
                    onChanged();
                }
            }
            return 0;
        };
        AddHookOrThrow(hwnd, hook);
        return hook;
    }

    /// <summary>
    /// <see cref="ApplyZBand(IReadOnlyList{nint})"/>가 도는 동안 참. 밴드 적용의 구체 삽입(이전 핀·이전 서피스 뒤)은 활성화된 적 있는 창에서
    /// "자기 IME 창 바로 아래"로 도착해 상승 요청과 구별할 수 없다 (통합 테스트 실측: flags=0x13 insertAfter=IME(owner=self)).
    /// 그 삽입은 의도된 것이므로 요청 단계 훅이 손대지 않는다 — 앵커로 돌리면 핀끼리·서피스끼리의 순서가 뒤집혀 사후 검증이 헛돈다.
    /// UI 스레드 전용이다. 억제 판정은 <see cref="AnchorBelowRules.RedirectTarget"/>, 설정·복원은
    /// <see cref="ApplyZBand(IReadOnlyList{nint}, Func{nint, nint, bool})"/>의 헤드리스 증인(<c>WindowStylingZBandTests</c>)이 잠근다 (74단계).
    /// </summary>
    private static bool _applyingBand;

    /// <summary><see cref="_applyingBand"/> 읽기 창구 — 헤드리스 증인(<c>WindowStylingZBandTests</c>)용 (74단계 A7-2).</summary>
    internal static bool IsApplyingBand => _applyingBand;

    /// <summary>소유자 조회 (<c>GW_OWNER</c>) — <see cref="AnchorBelowRules"/>에 주입한다.</summary>
    public static nint OwnerOf(nint hwnd) => NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER);

    /// <summary>z-순서상 바로 위 창 (<c>GW_HWNDPREV</c>, 최상단이면 0) — <see cref="ZOrderInvariant"/>에 주입한다.</summary>
    public static nint Above(nint hwnd) => NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDPREV);

    /// <summary>z-순서상 바로 아래 창 (<c>GW_HWNDNEXT</c>, 바닥이면 0) — <see cref="ZOrderInvariant"/>에 주입한다.</summary>
    public static nint Below(nint hwnd) => NativeMethods.GetWindow(hwnd, NativeMethods.GW_HWNDNEXT);

    /// <summary>살아 있는 창인가 — 닫힌 창의 낡은 HWND를 밴드 목록에서 걸러낼 때 쓴다.</summary>
    public static bool IsWindow(nint hwnd) => hwnd != 0 && NativeMethods.IsWindow(hwnd);

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
            // 실측 결과 Windows는 HWND_NOTOPMOST(-2)를 훅에 그대로 넘기지 않고 이미 구체적인 HWND로
            // 해석해서 준다 (실측: insertAfter=66294 flags=0x13; HWND_TOPMOST(-1)는 HWND_TOP(0)으로 온다).
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
    /// 나머지를 순서대로 그 아래에 삽입한다. 배치는 <c>SetWindowPos</c>(이동·크기·활성화 없음)이고, 실패하면 경고 로그를 남긴다 —
    /// 루프 규칙은 <see cref="ApplyZBand(IReadOnlyList{nint}, Func{nint, nint, bool})"/>가 가진다.
    /// </summary>
    public static void ApplyZBand(IReadOnlyList<nint> topToBottom) =>
        ApplyZBand(topToBottom, static (hwnd, insertAfter) =>
        {
            const uint flags = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE;
            bool ok = NativeMethods.SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0, flags);
            if (!ok)
            {
                // 실패는 조용히 지나가면 "가끔 툴바가 안 눌림"으로만 드러난다 — 원인(낡은 HWND 등)을 남긴다 (54단계 L5).
                // 오류 코드는 SetWindowPos 바로 뒤에서 읽는다.
                Diagnostics.Log.Warn(
                    $"z-밴드 적용 실패: hwnd=0x{hwnd:X} insertAfter=0x{insertAfter:X} 오류={Marshal.GetLastWin32Error()}");
            }
            return ok;
        });

    /// <summary>
    /// <see cref="ApplyZBand(IReadOnlyList{nint})"/>의 루프 — 배치(<paramref name="place"/>, 성공 여부를 돌려준다)를 주입받는 이음매 (74단계 A7-2).
    /// 규칙 (54단계 L5): 0 항목은 건너뛴다. 첫 성공 전까지는 <c>HWND_TOPMOST</c>에, 그 뒤로는 <b>마지막으로 성공한</b> 창 뒤에 삽입한다 —
    /// 실패한 항목은 건너뛰고 다음 항목이 그 자리를 잇는다. 도는 동안 <see cref="_applyingBand"/>가 참이고(요청 단계 억제, AGENTS L15),
    /// 끝나면 — 예외로 끝나도 — 들어올 때의 값으로 복원한다(중첩 호출 안전). UI 스레드 전용이다.
    /// </summary>
    internal static void ApplyZBand(IReadOnlyList<nint> topToBottom, Func<nint, nint, bool> place)
    {
        bool wasApplying = _applyingBand;
        _applyingBand = true;
        try
        {
            nint previous = 0;
            foreach (nint hwnd in topToBottom)
            {
                if (hwnd == 0)
                {
                    continue;
                }
                nint insertAfter = previous == 0 ? NativeMethods.HWND_TOPMOST : previous;
                if (!place(hwnd, insertAfter))
                {
                    continue;
                }
                previous = hwnd;
            }
        }
        finally
        {
            _applyingBand = wasApplying;
        }
    }
}
