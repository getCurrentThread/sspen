using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary><see cref="Strings"/>의 서식 함수 증인 (57단계, A6-8 — 설정 창의 인라인 버전 서식을 옮긴 것).</summary>
public class StringsTests
{
    [Fact]
    public void SettingsVersionLabel_WrapsWithV()
    {
        Assert.Equal("(v1.3.6)", Strings.SettingsVersionLabel("1.3.6"));
    }

    /// <summary>
    /// 설정 창은 <see cref="Version"/>을 문자열로 바꿔 넘긴다. 옮기기 전의 인라인 보간 <c>$"(v{curVer})"</c>와
    /// 결과가 한 글자도 다르지 않아야 한다 (동작 보존).
    /// </summary>
    [Fact]
    public void SettingsVersionLabel_OfVersionString_MatchesFormerInlineInterpolation()
    {
        var version = new Version(1, 3, 6);

        Assert.Equal($"(v{version})", Strings.SettingsVersionLabel(version.ToString()));
    }

    /// <summary>
    /// 업데이트 대화상자의 버전 줄 (77단계, C-5). 옮기기 전 창의 인라인 보간과 바이트 동일해야 한다 — 기대값은 리터럴이고
    /// 화살표 양옆 공백 세 칸까지 포함한다. 현재 버전에는 'v'를 붙이고 최신은 태그 그대로다.
    /// </summary>
    [Fact]
    public void UpdateVersionLine_MatchesLegacyFormat()
    {
        Assert.Equal("현재 버전: v1.3.6   →   최신 버전: v1.3.7", Strings.UpdateVersionLine(new Version(1, 3, 6), "v1.3.7"));
    }

    [Fact]
    public void UpdateDownloadingPercent_WrapsPercentInParentheses()
    {
        Assert.Equal("업데이트 다운로드 중... (42%)", Strings.UpdateDownloadingPercent(42));
    }

    [Fact]
    public void UpdateFailedDetail_AppendsReasonAfterFailedMessage()
    {
        var detail = Strings.UpdateFailedDetail("boom");

        Assert.Equal(Strings.UpdateFailedMessage + "boom", detail);
        Assert.EndsWith("\n\n오류: boom", detail);
    }
}
