namespace SSPen.Interop;

/// <summary>
/// 모니터 1개의 물리 정보.
/// <paramref name="Bounds"/>는 모니터 전체(rcMonitor), <paramref name="WorkArea"/>는 작업 표시줄을
/// 제외한 영역(rcWork)이다. 판서 서피스는 <paramref name="WorkArea"/>를 쓴다 — 전체를 덤으면
/// 작업 표시줄이 클릭되지 않거나 판서에 가려 보이지 않는다 (사용자 요청 18차).
/// </summary>
public sealed record MonitorSurfaceInfo(string DeviceName, PhysicalRect Bounds, PhysicalRect WorkArea, bool IsPrimary);

/// <summary>
/// 모니터 토폴로지 열거 (WI-2). EnumDisplayMonitors / GetSystemMetrics 기반, 전부 물리 픽셀.
/// 대상 환경: DISPLAY1(-1920,0) / DISPLAY3(0,0 주 모니터) / DISPLAY2(1920,0), 가상 스크린 5760x1080 원점 (-1920,0).
/// </summary>
public static class MonitorTopology
{
    public static IReadOnlyList<MonitorSurfaceInfo> Enumerate()
    {
        var monitors = new List<MonitorSurfaceInfo>();
        NativeMethods.MonitorEnumProc callback = (nint hMonitor, nint _, ref NativeMethods.RECT _, nint _) =>
        {
            var info = new NativeMethods.MONITORINFOEX
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
            };
            if (NativeMethods.GetMonitorInfoW(hMonitor, ref info))
            {
                var r = info.rcMonitor;
                var w = info.rcWork;
                monitors.Add(new MonitorSurfaceInfo(
                    info.szDevice,
                    new PhysicalRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top),
                    new PhysicalRect(w.Left, w.Top, w.Right - w.Left, w.Bottom - w.Top),
                    (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0));
            }
            return true;
        };

        if (!NativeMethods.EnumDisplayMonitors(0, 0, callback, 0))
        {
            throw new InvalidOperationException("EnumDisplayMonitors 실패");
        }

        // 왼쪽→오른쪽 안정 정렬 (음수 원점이 첫 번째).
        monitors.Sort((a, b) => a.Bounds.X != b.Bounds.X ? a.Bounds.X.CompareTo(b.Bounds.X) : a.Bounds.Y.CompareTo(b.Bounds.Y));
        return monitors;
    }

    /// <summary>가상 스크린 전체 물리 사각형 (음수 원점 포함).</summary>
    public static PhysicalRect VirtualScreen() => new(
        NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));
}
