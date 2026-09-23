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
/// 유일한 예외는 실험적 <see cref="ZBandPoller"/>(73단계, 설정 게이트·기본 켜짐)다 — 2초 Background 타이머로 같은 IsOrdered 검사 후
/// 이 정책의 백오프와 무관하게 <c>Repair</c>한다(참조하지도, <see cref="Reset"/>으로 풀지도 않는다). 렌더 틱 금지는 그대로다.
/// (c) <b>깨우는 계기</b> — 어떤 WinEvent가 깨우는가(<see cref="Wakes"/>)와 두 훅을 모두 설치 시도하는 규칙(<see cref="InstallWatches"/>, 70단계).
/// 이 정책을 워치·순서 판정과 잇는 조립(훅 소유, 검증 본문, 복구는 <see cref="Reset"/> 없이, 종료)은 <see cref="ZBandVerifier"/>다 (72단계).
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

    /// <summary>
    /// 검증을 깨우는 두 WinEvent 훅(REORDER, FOREGROUND)을 이 순서로 <b>둘 다</b> 설치 시도한다 (70단계, A9-8).
    /// 예전 배선은 <c>||</c> 단락 평가라 REORDER가 실패하면 FOREGROUND는 시도조차 안 되어 사후 검증이 전혀 깨어나지 않았다.
    /// 한쪽만 성공해도 그 계기는 되돌리지 않는다 — 검증을 하나라도 깨우는 편이 낫다. 둘 다 성공하면 null,
    /// 아니면 어느 훅이 실패했는지 밝힌 경고 로그 문구를 돌려준다(기록은 호출자가 한다).
    /// </summary>
    public static string? InstallWatches(Func<bool> installReorder, Func<bool> installForeground)
    {
        bool reorder = installReorder();
        bool foreground = installForeground();
        if (reorder && foreground)
        {
            return null;
        }
        return $"z-밴드 검증 WinEvent 훅 설치 실패 (REORDER={Outcome(reorder)}, FOREGROUND={Outcome(foreground)}) — "
            + "요청·결과 단계 훅과 설치된 계기로만 방어한다.";

        static string Outcome(bool installed) => installed ? "설치됨" : "실패";
    }

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
