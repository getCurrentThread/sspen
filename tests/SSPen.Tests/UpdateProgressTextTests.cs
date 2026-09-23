using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="UpdateProgressText"/>의 증인 (77단계, C-5). 창 람다에 있던 진행 문구 판정 — 백분율 버림, 1.0 이상이면 '설치 중' —
/// 을 고정한다. 기대 문구는 리터럴이다(문구가 바이트 단위로 같아야 한다).
/// </summary>
public class UpdateProgressTextTests
{
    [Theory]
    [InlineData(0.999, "업데이트 다운로드 중... (99%)")]
    [InlineData(0.5, "업데이트 다운로드 중... (50%)")]
    [InlineData(0.0, "업데이트 다운로드 중... (0%)")]
    [InlineData(0.019, "업데이트 다운로드 중... (1%)")]
    public void For_BelowOne_TruncatesPercent(double progress, string expected)
    {
        Assert.Equal(expected, UpdateProgressText.For(progress));
    }

    /// <summary>받은 바이트가 Content-Length보다 많아 1.0을 넘어도 '설치 중'이다.</summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.2)]
    public void For_OneOrMore_IsInstalling(double progress)
    {
        Assert.Equal(Strings.UpdateInstalling, UpdateProgressText.For(progress));
    }

    /// <summary>
    /// 옮기기 전 창 람다의 순서 — 퍼센트 문구를 먼저 쓰고, 1.0 이상이면 '설치 중'으로 덮어쓴다 — 의 최종 문구와 같다(동작 보존).
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.42)]
    [InlineData(0.999)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void For_MatchesLegacyWriteThenOverwriteSequence(double p)
    {
        var legacy = $"{Strings.UpdateDownloading} ({(int)(p * 100)}%)";
        if (p >= 1.0)
        {
            legacy = Strings.UpdateInstalling;
        }

        Assert.Equal(legacy, UpdateProgressText.For(p));
    }
}
