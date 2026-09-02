using SSPen.Interop;

namespace SSPen.Shell;

/// <summary>
/// 툴바 초기 배치 산술 (34단계, AC-21/CRIT-17). 저장된 위치가 둘 다 있으면 복원하고, 없으면 주 모니터 우측 세로 중앙.
///
/// 보존이지 승인이 아니다: 주 모니터의 <b>물리</b> 픽셀 사각형(<c>Bounds</c>)을 DIP인 <c>Window.Left/Top</c>에 그대로 대입하는
/// 오늘의 동작(AGENTS L18 경계)을 고치지 않고 옮겼다 — 고치면 저장된 ToolbarLeft/Top의 의미가 바뀌어 설정 마이그레이션
/// 결정이 필요하다. <see cref="StripWidth"/>와 <see cref="RightMargin"/>은 다른 두 양이라 46 하나로 합치지 않는다 (c2904ff 교훈).
/// </summary>
public static class ToolbarPlacement
{
    /// <summary>스트립 너비 (테두리 2 + 내부 30 + 테두리 2).</summary>
    public const double StripWidth = 34;

    /// <summary>모니터 우측 가장자리와 스트립 사이 여백.</summary>
    public const double RightMargin = 12;

    /// <summary>
    /// CRIT-17: 실제 스트립 높이 = 로고(34+2) + 테두리(4) + 버튼 14개·구분선 4개·퀵컬러 블록. 선택 버튼 추가로 494 → 524.
    /// 이 값이 틀어지면 툴바가 모니터 중앙에서 밀려난다.
    /// </summary>
    public const double StripHeight = 524;

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
}
