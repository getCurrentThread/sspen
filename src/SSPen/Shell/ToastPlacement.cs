using SSPen.Interop;

namespace SSPen.Shell;

/// <summary>
/// 토스트 배치 산술 (순수). 커서가 있는 화면의 <b>작업 영역</b> 하단 중앙에 놓는다 —
/// <c>Bounds</c>가 아니라 <c>WorkArea</c>인 이유는 서피스 배치와 같다 (AGENTS: 작업 표시줄을 덮지 않는다).
/// 툴바가 기본으로 오른쪽 세로 스트립이라 하단 중앙은 기하학적으로 겹치지 않는다.
/// </summary>
public static class ToastPlacement
{
    /// <summary>작업 영역 아래쪽 여백 (물리 픽셀 기준 계산 전 논리 여백).</summary>
    public const int BottomMarginDip = 48;

    /// <summary>
    /// 토스트 창의 좌상단 물리 좌표. 창이 작업 영역보다 크면 잘라내지 않고 <b>왼쪽·위로 클램프</b>해
    /// 최소한 시작 지점이 화면 안에 남게 한다 (음수 원점 토폴로지 안전).
    /// </summary>
    public static (int X, int Y) Anchor(PhysicalRect workArea, int width, int height, int bottomMargin)
    {
        int x = workArea.X + (workArea.Width - width) / 2;
        int y = workArea.Bottom - bottomMargin - height;
        return (
            Math.Clamp(x, workArea.X, Math.Max(workArea.X, workArea.Right - width)),
            Math.Clamp(y, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - height)));
    }

    /// <summary>커서를 담고 있는 화면. 어느 화면에도 걸치지 않으면(모니터 사이 공백) 주 화면으로 떨어진다.</summary>
    public static MonitorSurfaceInfo MonitorFor(IReadOnlyList<MonitorSurfaceInfo> monitors, int cursorX, int cursorY)
    {
        foreach (var monitor in monitors)
        {
            if (monitor.Bounds.Contains(cursorX, cursorY))
            {
                return monitor;
            }
        }
        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
    }
}
