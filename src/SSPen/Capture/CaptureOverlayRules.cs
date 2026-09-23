using System.Windows;
using SSPen.Interop;

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
/// 정지 판정 임계값 3px는 선택 계층(<c>SelectionGestureRules.ClickThresholdPixels</c>)과 <b>같은 값</b>이지만,
/// 척도는 스냅샷 픽셀 체비셰프 거리(≤)다 (<see cref="MovedPixels"/>). 선택 계층은 논리 픽셀 유클리드(&lt;)다.
///
/// 68단계(A7-5)부터는 오버레이 창 안에 있던 좌표·크기 판정 — 캔버스 → 물리 영역(<see cref="ToPhysicalRegion"/>),
/// 최소 선택 크기(<see cref="IsTooSmall"/>), 이동 거리, 도구모음 배치(<see cref="ActionBarOrigin"/>) — 도 여기 산다.
/// 창은 답을 받아 캔버스에 옮겨 적기만 한다.
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

    /// <summary>이보다 좁거나 낮은 선택은 실수로 본다 — 폭이나 높이 <b>한쪽만</b> 작아도 무시하고 다시 고른다 (스냅샷 픽셀).</summary>
    public const double MinSelectionPixels = 4;

    /// <summary>도구모음 오른쪽 끝을 선택 오른쪽 변에 맞추기 위한 왼쪽 오프셋 (도구모음 폭 근사).</summary>
    private const double ActionBarRightInset = 240;

    /// <summary>도구모음이 가상 스크린 오른쪽 밖으로 나가지 않게 하는 왼쪽 좌표 상한의 여유.</summary>
    private const double ActionBarMaxLeftInset = 250;

    /// <summary>선택 아래 변과 도구모음 사이 간격.</summary>
    private const double ActionBarGap = 8;

    /// <summary>
    /// 도구모음 높이 근사. 아래 가장자리에서 이만큼 남지 않으면 선택 위로 뒤집고, 뒤집을 때도 이만큼 올려 붙인다.
    /// </summary>
    private const double ActionBarFlipHeight = 44;

    /// <summary>놓은 선택이 너무 작아 무시해야 하는가 (<see cref="MinSelectionPixels"/> 미만, 폭·높이 중 하나라도).</summary>
    public static bool IsTooSmall(Rect selection) =>
        selection.Width < MinSelectionPixels || selection.Height < MinSelectionPixels;

    /// <summary>
    /// 캔버스 선택 → 물리 영역 (R2). 캔버스·이미지는 스냅샷 물리 픽셀 크기로 잡혀 있으므로 캔버스 단위 == 스냅샷
    /// 픽셀이다. 따라서 변환은 항등 + 가상 스크린 원점 보정만 수행한다 (아키텍트 2세대 권고: dpi 곱셈은 오히려
    /// 시각적 선택과 어긋난다. 혼합 DPI는 이연 목록 4번/Non-Goal). 반올림은 <see cref="Math.Round(double)"/>의
    /// 기본값(짝수 쪽, ToEven)이다. 빈 선택은 원점 보정 없이 0 사각형이다.
    /// </summary>
    public static PhysicalRect ToPhysicalRegion(Rect selection, PhysicalRect virtualScreen) =>
        selection.IsEmpty
            ? new PhysicalRect(0, 0, 0, 0)
            : new PhysicalRect(
                virtualScreen.X + (int)Math.Round(selection.X),
                virtualScreen.Y + (int)Math.Round(selection.Y),
                (int)Math.Round(selection.Width),
                (int)Math.Round(selection.Height));

    /// <summary>누른 점과 놓은 점 사이 이동 거리 — 체비셰프 거리(축별 차이의 최댓값)다. <see cref="PointerVerdict"/>의 입력.</summary>
    public static double MovedPixels(Point down, Point up) =>
        Math.Max(Math.Abs(up.X - down.X), Math.Abs(up.Y - down.Y));

    /// <summary>
    /// 선택 확정 뒤 도구모음의 캔버스 좌상단. 선택 오른쪽 아래에 붙이되 가상 스크린 좌우 안으로 가두고,
    /// 아래 가장자리에 걸리면 선택 위로 뒤집는다 (뒤집은 뒤에도 위쪽 0 밖으로는 나가지 않는다).
    /// </summary>
    public static Point ActionBarOrigin(Rect selection, PhysicalRect virtualScreen)
    {
        double x = Math.Clamp(selection.Right - ActionBarRightInset, 0, virtualScreen.Width - ActionBarMaxLeftInset);
        double y = selection.Bottom + ActionBarGap;
        if (y > virtualScreen.Height - ActionBarFlipHeight)
        {
            y = Math.Max(selection.Top - ActionBarFlipHeight, 0);
        }
        return new Point(x, y);
    }
}
