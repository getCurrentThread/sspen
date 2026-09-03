using System.Windows;
using SSPen.Interop;

namespace SSPen.Shell;

/// <summary>
/// 툴바 배치 산술 (34단계, AC-21/CRIT-17). 저장된 위치가 둘 다 있으면 <b>화면 안으로 클램프해</b> 복원하고,
/// 없으면 주 모니터 작업 영역 우측 세로 중앙에 <b>실측 크기</b>로 놓는다.
///
/// 이전 동작의 버그: 주 모니터의 물리 픽셀 사각형을 DIP인 <c>Window.Left/Top</c>에 그대로 대입했다.
/// 150% 배율 1920 물리 화면에서 <c>1920 − 34 − 12 = 1874</c>는 DIP로 해석되어 2811 물리 픽셀이 되고,
/// 첫 실행 툴바가 통째로 화면 밖에 놓였다 (세로 중앙도 같은 배율만큼 어긋났다).
///
/// 마이그레이션이 필요 없는 이유: 저장된 값은 여전히 그대로 돌려주고 <b>화면 밖일 때만</b> 끌어온다.
/// 정상 값은 바이트 그대로 나오므로 <c>AppSettings</c>의 무마이그레이션 원칙이 유지된다.
/// <see cref="StripWidth"/>와 <see cref="RightMargin"/>은 다른 두 양이라 46 하나로 합치지 않는다 (c2904ff 교훈).
/// </summary>
public static class ToolbarPlacement
{
    /// <summary>스트립 너비 (테두리 2 + 내부 30 + 테두리 2).</summary>
    public const double StripWidth = 34;

    /// <summary>모니터 우측 가장자리와 스트립 사이 여백.</summary>
    public const double RightMargin = 12;

    /// <summary>
    /// 레이아웃 전 폴백 높이 (로고 + 테두리 + 버튼·구분선·퀵컬러). 실제 배치는 <see cref="PhysicalOnPrimary"/>가
    /// <b>실측</b> 높이를 쓰므로 이 값이 틀어져도 툴바가 중앙에서 밀리지 않는다 — 첫 프레임의 임시 위치일 뿐이다.
    /// </summary>
    public const double StripHeight = 524;

    /// <summary>클램프 시 최소한 이만큼은 화면 안에 남긴다 (로고와 버튼 몇 개를 잡을 수 있는 높이).</summary>
    public const double MinVisibleHeight = 60;

    /// <summary>표시 전 임시 위치 (첫 프레임이 좌상단에서 튀지 않게). 최종 위치는 레이아웃 뒤에 정해진다.</summary>
    public static (double Left, double Top) Initial(double? savedLeft, double? savedTop, PhysicalRect primaryBounds)
    {
        if (savedLeft is { } left && savedTop is { } top)
        {
            return (left, top);
        }
        return (
            primaryBounds.X + primaryBounds.Width - StripWidth - RightMargin,
            primaryBounds.Y + (primaryBounds.Height - StripHeight) / 2.0);
    }

    /// <summary>
    /// 저장된 위치를 화면 안으로 끌어온다. 저장된 값이 없으면 null — 호출자는 실측 배치로 넘어간다.
    /// 사라진 모니터에 저장된 위치, 해상도가 줄어든 뒤의 위치, 그리고 옛 DIP 버그가 남긴 값이 모두 여기서 자가 치유된다.
    /// </summary>
    public static (double Left, double Top)? Restored(double? savedLeft, double? savedTop, Rect virtualScreenDip)
    {
        if (savedLeft is not { } left || savedTop is not { } top)
        {
            return null;
        }
        if (virtualScreenDip.Width <= 0 || virtualScreenDip.Height <= 0)
        {
            return (left, top); // 토폴로지를 모르면 사용자의 값을 건드리지 않는다.
        }
        double maxLeft = Math.Max(virtualScreenDip.X, virtualScreenDip.Right - StripWidth);
        double maxTop = Math.Max(virtualScreenDip.Y, virtualScreenDip.Bottom - MinVisibleHeight);
        return (
            Math.Clamp(left, virtualScreenDip.X, maxLeft),
            Math.Clamp(top, virtualScreenDip.Y, maxTop));
    }

    /// <summary>
    /// 주 모니터 작업 영역 우측 세로 중앙의 <b>물리</b> 좌표. 크기도 물리 픽셀이라 배율이 섞이지 않는다.
    /// 작업 영역(<c>WorkArea</c>)을 쓰는 이유는 서피스 배치와 같다 — 작업 표시줄을 덮지 않는다.
    /// </summary>
    public static (int X, int Y) PhysicalOnPrimary(PhysicalRect workArea, int width, int height, int rightMargin)
    {
        int x = workArea.Right - width - rightMargin;
        int y = workArea.Y + (workArea.Height - height) / 2;
        return (
            Math.Clamp(x, workArea.X, Math.Max(workArea.X, workArea.Right - width)),
            Math.Clamp(y, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - height)));
    }
}
