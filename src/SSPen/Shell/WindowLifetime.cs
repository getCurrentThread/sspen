using System.Windows;
using System.Windows.Threading;

namespace SSPen.Shell;

/// <summary>
/// 사라지는 창(핀·캡처 오버레이)의 안전한 파괴 순서.
///
/// 문제: 마우스가 창 위에 있는 상태로 <c>Close()</c>를 부르면 HWND가 즉시 파괴되는데,
/// WPF 입력 계층은 "마우스가 직전에 어느 요소 위에 있었는지"를 계속 붙들고 있다.
/// 다음 마우스 이동에서 <c>MouseDevice.Synchronize()</c> → <c>PopupControlService.OnMouseMove</c> →
/// <c>PointUtil.ClientToScreen</c> 순으로 그 죽은 요소의 PresentationSource를 쓰려다
/// <c>Win32Exception(1400) 잘못된 창 핸들</c>로 터진다. 창을 닫고 한참 뒤(마우스를 다음에 움직일 때)
/// 터지므로 원인과 증상이 멀리 떨어져 보인다.
///
/// 해결: <b>먼저 숨기고(HWND는 아직 살아 있다) 입력 처리가 한 바퀴 돈 뒤에 파괴</b>한다.
/// 숨기는 순간 WPF가 히트테스트를 다시 돌려 "마우스 아래 요소"를 그 아래 창으로 정상 갱신하므로,
/// 실제로 HWND가 사라질 시점엔 아무도 그것을 가리키고 있지 않다.
/// </summary>
public static class WindowLifetime
{
    /// <summary>
    /// 창을 즉시 숨기고, 입력 처리가 정리된 뒤에 실제로 닫는다.
    /// 호출자 입장에서는 그 자리에서 사라진 것과 같다 (지연되는 것은 HWND 파괴뿐이다).
    /// </summary>
    public static void HideThenClose(Window window)
    {
        if (!window.IsVisible)
        {
            // 이미 숨겨져 있으면 위험 구간이 아니다 — 바로 닫는다.
            window.Close();
            return;
        }
        window.Hide();
        // Input보다 낮은 우선순위로 미뤄 Synchronize()가 먼저 끝나게 한다.
        window.Dispatcher.BeginInvoke(DispatcherPriority.Background, window.Close);
    }
}
