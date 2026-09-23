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
}
