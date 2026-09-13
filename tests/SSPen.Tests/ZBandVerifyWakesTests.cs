using SSPen.Interop;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="ZBandVerifyPolicy.Wakes"/> (54단계 L3). 실측으로 고정: 최상위 z-순서 변화는 REORDER가 데스크톱 창으로 온다 —
/// 남의 앱 자식 창 재정렬(hwnd=그 창)은 깨우지 않아야 Background 검증이 탐색기·브라우저의 리스트 갱신마다 돌지 않는다.
/// FOREGROUND는 언제나 깨우고, 구독하지 않은 이벤트는 깨우지 않는다.
/// </summary>
public class ZBandVerifyWakesTests
{
    private const long Desktop = 0x10010;

    [Theory]
    [InlineData(NativeMethods.EVENT_OBJECT_REORDER, Desktop, true)]
    [InlineData(NativeMethods.EVENT_OBJECT_REORDER, 0x2A0B44, false)]
    [InlineData(NativeMethods.EVENT_SYSTEM_FOREGROUND, 0x2A0B44, true)]
    [InlineData(NativeMethods.EVENT_SYSTEM_FOREGROUND, Desktop, true)]
    [InlineData(0x800Bu /* EVENT_OBJECT_LOCATIONCHANGE */, Desktop, false)]
    public void Wakes_EventAndHwnd_DecidesVerification(uint eventType, long hwnd, bool expected)
    {
        // 특성 인수는 nint를 허용하지 않아 long으로 받는다.
        Assert.Equal(expected, ZBandVerifyPolicy.Wakes(eventType, (nint)hwnd, (nint)Desktop));
    }
}
