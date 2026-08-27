using System.ComponentModel;
using System.Runtime.CompilerServices;
using SSPen.Diagnostics;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 사라진 창 입력 경주 필터 (사용자 보고 크래시 대응).
///
/// <c>StackTrace</c>는 읽기 전용이라 문자열로 주입할 수 없다. 그래서 이름이 실제 WPF 프레임을
/// 흉내 내는 메서드에서 <b>던지고</b>, 한 단계 <b>바깥에서 잡는다</b> — 그래야 트레이스에 그 프레임이 남는다
/// (같은 메서드 안에서 잡으면 트레이스에 잡은 프레임 하나만 남아 검사 대상이 사라진다).
/// <c>NoInlining</c>은 릴리스 빌드에서 프레임이 접히는 것을 막는다.
/// </summary>
public class InputRaceFilterTests
{
    private const int InvalidWindowHandle = 1400;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PopupControlService(int code) => throw new Win32Exception(code);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void MouseDevice(int code) => throw new Win32Exception(code);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SetWindowPosWrapper(int code) => throw new Win32Exception(code);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowsPlainException() => throw new InvalidOperationException("boom");

    /// <summary>호출을 한 단계 바깥에서 잡아 호출 대상 프레임이 트레이스에 남게 한다.</summary>
    private static Exception Capture(Action throwing)
    {
        try
        {
            throwing();
        }
        catch (Exception ex)
        {
            return ex;
        }
        throw new InvalidOperationException("예외가 발생하지 않았다 — 테스트 설정 오류");
    }

    [Fact]
    public void Null_IsNotBenign()
    {
        Assert.False(InputRaceFilter.IsBenignStaleWindowRace(null));
    }

    [Fact]
    public void NonWin32Exception_IsNotBenign()
    {
        var ex = Capture(ThrowsPlainException);

        Assert.False(InputRaceFilter.IsBenignStaleWindowRace(ex));
    }

    [Fact]
    public void Win32_InPopupPath_IsBenign()
    {
        // 사용자가 실제로 겪은 형태: 1400 + 팝업/툴팁 서비스 경로.
        var ex = Capture(() => PopupControlService(InvalidWindowHandle));

        Assert.Contains("PopupControlService", ex.StackTrace);
        Assert.True(InputRaceFilter.IsBenignStaleWindowRace(ex));
    }

    [Fact]
    public void Win32_InMouseDevicePath_IsBenign()
    {
        var ex = Capture(() => MouseDevice(InvalidWindowHandle));

        Assert.Contains("MouseDevice", ex.StackTrace);
        Assert.True(InputRaceFilter.IsBenignStaleWindowRace(ex));
    }

    [Fact]
    public void Win32_1400_OutsideInputPipeline_IsNotBenign()
    {
        // 핵심 안전장치: 같은 오류 코드라도 인터롭 버그(잘못된 HWND로 SetWindowPos 등)는
        // 숨기면 안 된다. 여기서 true가 나오면 진짜 배치 버그가 조용히 묻힌다.
        var ex = Capture(() => SetWindowPosWrapper(InvalidWindowHandle));

        Assert.False(InputRaceFilter.IsBenignStaleWindowRace(ex));
    }

    [Fact]
    public void Win32_DifferentErrorCode_InPopupPath_IsNotBenign()
    {
        // 경로가 맞아도 코드가 다르면 무해로 볼 근거가 없다 (5 = ERROR_ACCESS_DENIED).
        var ex = Capture(() => PopupControlService(5));

        Assert.False(InputRaceFilter.IsBenignStaleWindowRace(ex));
    }

    [Fact]
    public void Win32_WithoutStackTrace_IsNotBenign()
    {
        // 던져지지 않은 예외는 트레이스가 null이다 — 경로를 확인할 수 없으면 삼키지 않는다.
        var never = new Win32Exception(InvalidWindowHandle);

        Assert.Null(never.StackTrace);
        Assert.False(InputRaceFilter.IsBenignStaleWindowRace(never));
    }
}
