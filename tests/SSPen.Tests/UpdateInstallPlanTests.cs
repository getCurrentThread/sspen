using System.ComponentModel;
using System.IO;
using System.Net.Http;
using SSPen.Updates;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="UpdateInstallPlan"/>의 증인 (77단계, A9-7 (c)·A1-8). 명령줄은 옮기기 전 <c>LaunchSilentInstallerAndExit</c>의 보간식을
/// 글자 그대로 옮긴 것이다 — 기대값은 그 보간식에 공백 든 경로를 넣어 손으로 펼친 스냅숏이다(같은 식을 다시 부르는 동어반복이 아니다).
/// </summary>
public class UpdateInstallPlanTests
{
    private const string InstallerPath = @"C:\Users\a b\AppData\Local\Temp\SSPen-Update\SSPen-Setup-v1.3.6.exe";
    private const string CurrentExe = @"C:\Users\a b\AppData\Local\Programs\SSPen\SSPen.exe";

    [Fact]
    public void CommandLine_PathsWithSpaces_MatchesLegacySnapshot()
    {
        const string expected = """
            /c "start /wait "" "C:\Users\a b\AppData\Local\Temp\SSPen-Update\SSPen-Setup-v1.3.6.exe" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS & start "" "C:\Users\a b\AppData\Local\Programs\SSPen\SSPen.exe""
            """;

        Assert.Equal(expected, UpdateInstallPlan.CommandLine(InstallerPath, CurrentExe));
    }

    /// <summary>
    /// 연결자는 <c>&amp;</c> 하나다 — <c>&amp;&amp;</c>로 바꾸면 설치가 실패했을 때 앱이 되살아나지 않는다(과거 실측).
    /// 스냅숏과 별도로 둔 이유: 누가 스냅숏을 '고쳐' 갱신해도 이 의도는 이름으로 남는다.
    /// </summary>
    [Fact]
    public void CommandLine_ChainsRestartWithSingleAmpersand_SoAppRevivesEvenIfInstallFails()
    {
        var line = UpdateInstallPlan.CommandLine(InstallerPath, CurrentExe);

        Assert.DoesNotContain("&&", line);
        Assert.Contains(" /FORCECLOSEAPPLICATIONS & start \"\" \"" + CurrentExe + "\"", line);
    }

    [Theory]
    [InlineData(typeof(Win32Exception))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(PlatformNotSupportedException))]
    [InlineData(typeof(ObjectDisposedException))]
    [InlineData(typeof(FileNotFoundException))]
    public void IsLaunchFailure_DocumentedProcessStartException_IsCaught(Type exceptionType)
    {
        var ex = exceptionType == typeof(ObjectDisposedException)
            ? new ObjectDisposedException("process")
            : (Exception)Activator.CreateInstance(exceptionType)!;

        Assert.True(UpdateInstallPlan.IsLaunchFailure(ex));
    }

    /// <summary>좁은 필터다 — 문서화되지 않은 예외(프로그래밍 오류)는 삼키지 않고 전역 처리기로 보낸다.</summary>
    [Theory]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(NullReferenceException))]
    [InlineData(typeof(HttpRequestException))]
    public void IsLaunchFailure_OtherException_IsNotCaught(Type exceptionType)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType)!;

        Assert.False(UpdateInstallPlan.IsLaunchFailure(ex));
    }
}
