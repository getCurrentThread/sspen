namespace SSPen.Shell;

/// <summary>버튼 한 개의 시각 상태 (배경 토큰 선택의 유일한 어휘).</summary>
public enum ButtonVisualState
{
    /// <summary>평시 — 투명.</summary>
    Idle,

    Hover,

    Pressed,

    /// <summary>활성(선택된 도구·켜진 토글) — 강조색 배경 + 흰 글리프.</summary>
    Active,
}

/// <summary>
/// 버튼 상태 판정 (순수).
///
/// 고치는 것 둘:
/// <list type="bullet">
///   <item><b>눌림 상태가 없었다.</b> 누르는 동안 화면이 전혀 변하지 않아, 클릭이 먹었는지
///     알 수 있는 시점은 결과가 나온 뒤뿐이었다.</item>
///   <item><b>클릭 판정이 <c>MouseLeftButtonUp</c> 단독이었다.</b> 버튼 밖에서 누르기 시작해
///     버튼 위에서 떼면 동작이 발화하고, 버튼에서 눌렀다가 밖으로 끌어 취소하려 해도
///     막을 방법이 없었다 — 실행취소·전체 지우기처럼 되돌리기 힘든 버튼에서 특히 나쁘다.</item>
/// </list>
/// </summary>
public static class PressStateRules
{
    /// <summary>활성이 눌림·호버보다 우선한다 — 켜진 토글은 손을 얹었다고 꺼진 것처럼 보이면 안 된다.</summary>
    public static ButtonVisualState Resolve(bool active, bool hovered, bool pressed)
    {
        if (active)
        {
            return ButtonVisualState.Active;
        }
        if (pressed && hovered)
        {
            return ButtonVisualState.Pressed;
        }
        return hovered ? ButtonVisualState.Hover : ButtonVisualState.Idle;
    }

    /// <summary>
    /// 동작을 발화할 것인가: <b>이 버튼에서 눌렀고, 이 버튼에서 뗐을 때</b>만이다.
    /// 밖에서 시작한 클릭은 남의 것이고, 밖에서 뗀 것은 취소하려는 손이다.
    /// </summary>
    public static bool ShouldFire(bool pressedInside, bool releasedInside) => pressedInside && releasedInside;
}
