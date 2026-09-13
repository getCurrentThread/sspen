using SSPen.Interop;

namespace SSPen.Shell;

/// <summary>
/// z-밴드 사후 검증의 순수 정책 (54단계 L3). WinEvent(최상위 z-순서 변화·포그라운드 전환)가 검증을 깨우고,
/// 검증은 실제 순서가 <see cref="ZBandOrder"/>와 다를 때만 <c>ApplyZBand</c>를 다시 돌린다.
///
/// 소유하는 것: (a) <b>코얼레싱</b> — 이벤트 폭주 속에서 디스패처 큐에는 검증 하나만 걸린다. (b) <b>백오프</b> —
/// 복구가 연속 <see cref="MaxConsecutiveRepairs"/>번 소용없으면(낡은 HWND·외부 앱이 계속 뒤집음) 정규 재적용
/// (<see cref="Reset"/>: AppState.Changed 등)까지 쉰다. 없으면 복구 → 재정렬 이벤트 → 검증 → 복구의 무한 루프가
/// Background 우선순위로 CPU를 먹는다. AGENTS의 "렌더 틱에서 밴드 재적용 금지"와 같은 정신이다: 밴드 적용은 사건에만 반응한다.
/// </summary>
public sealed class ZBandVerifyPolicy
{
    public const int MaxConsecutiveRepairs = 3;

    /// <summary>
    /// 이 WinEvent가 검증을 깨우는가. 실측(빌드 26200): 최상위 z-순서 변화는 <c>EVENT_OBJECT_REORDER</c>가 hwnd=데스크톱 창으로 온다 —
    /// 남의 앱 자식 창 재정렬(탐색기·브라우저의 리스트)은 hwnd가 그 창이라 거른다. <c>EVENT_SYSTEM_FOREGROUND</c>는 언제나 깨운다.
    /// 그 외 이벤트는 구독하지 않지만, 잘못 들어와도 깨우지 않는다.
    /// </summary>
    public static bool Wakes(uint eventType, nint hwnd, nint desktop) => eventType switch
    {
        NativeMethods.EVENT_SYSTEM_FOREGROUND => true,
        NativeMethods.EVENT_OBJECT_REORDER => hwnd == desktop,
        _ => false,
    };

    private int _consecutiveRepairs;
    private bool _pending;

    /// <summary>연속 복구 한도를 넘겨 정규 재적용까지 검증을 쉬는 중인가.</summary>
    public bool Suspended => _consecutiveRepairs >= MaxConsecutiveRepairs;

    /// <summary>검증이 큐에 걸려 아직 돌지 않았는가.</summary>
    public bool Pending => _pending;

    /// <summary>이벤트 1건. 검증을 큐에 넣어야 하면 true (이미 걸려 있거나 쉬는 중이면 false).</summary>
    public bool OnEvent()
    {
        if (_pending || Suspended)
        {
            return false;
        }
        _pending = true;
        return true;
    }

    /// <summary>
    /// 큐에서 검증이 돌았다. <paramref name="ordered"/>가 참이면 연속 복구 카운터를 지운다.
    /// 거짓이면 복구를 허용할지 돌려준다 — 한도에 닿으면 false이고 <see cref="Suspended"/>가 된다.
    /// </summary>
    public bool OnVerified(bool ordered)
    {
        _pending = false;
        if (ordered)
        {
            _consecutiveRepairs = 0;
            return false;
        }
        if (Suspended)
        {
            return false;
        }
        _consecutiveRepairs++;
        return true;
    }

    /// <summary>정규 재적용(상태 변경·핀·캡처·설정·툴바 토글)이 돌았다 — 백오프를 푼다.</summary>
    public void Reset() => _consecutiveRepairs = 0;
}
