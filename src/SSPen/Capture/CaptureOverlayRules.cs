namespace SSPen.Capture;

/// <summary>선택이 확정된 뒤 오버레이에서 마우스를 눌렀을 때의 판정.</summary>
public enum CapturePointerVerdict
{
    /// <summary>도구모음 안 — 버튼 자신의 핸들러가 처리한다.</summary>
    Ignore,

    /// <summary>제자리 클릭 — 기본 동작으로 확정한다 (사용자 요청 15차).</summary>
    CommitDefault,

    /// <summary>끌었다 — 다시 고르려는 것이다. 선택을 새로 시작한다.</summary>
    RestartSelection,
}

/// <summary>
/// 캡처 오버레이 포인터 판정 (WI-11).
///
/// 고치는 것과 지키는 것을 구분한다:
/// <list type="bullet">
///   <item><b>지킨다</b> — 도구모음 밖 제자리 클릭은 계속 기본 동작(핀)으로 끝난다. 이것은 의도된 설계다 (사용자 요청 15차).</item>
///   <item><b>고친다</b> — 예전에는 <b>드래그</b>도 같은 취급을 받았다. 영역을 잘못 잡아 다시 끌면
///     누르는 순간 핀이 확정돼, 다시 고르려던 사용자가 원치 않는 핀 창을 얻었다.</item>
/// </list>
/// 정지 판정 임계값은 선택 계층(<c>SelectionGestureRules.ClickThresholdPixels</c>)과 <b>같은 3px</b>이다 —
/// 같은 손동작을 두 계층이 다르게 부르면 사용자는 그 차이를 학습할 방법이 없다.
/// </summary>
public static class CaptureOverlayRules
{
    /// <summary>
    /// 도구모음의 기본 동작. 배지·Enter 바인딩·바깥 클릭이 모두 이 하나를 참조하므로 서로 어긋날 수 없다.
    /// </summary>
    public const CaptureAction DefaultAction = CaptureAction.Pin;

    /// <summary>이 거리 이하로 움직였으면 '제자리 클릭'이다 (선택 계층과 같은 값).</summary>
    public const double ClickThresholdPixels = 3.0;

    public static CapturePointerVerdict PointerVerdict(bool barVisible, bool insideBar, double movedPixels)
    {
        if (!barVisible)
        {
            return CapturePointerVerdict.RestartSelection; // 아직 고르는 중 — 언제나 새 선택이다.
        }
        if (insideBar)
        {
            return CapturePointerVerdict.Ignore;
        }
        return movedPixels <= ClickThresholdPixels
            ? CapturePointerVerdict.CommitDefault
            : CapturePointerVerdict.RestartSelection;
    }
}
