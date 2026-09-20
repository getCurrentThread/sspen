using SSPen.Updates;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="UpdateCheckPresentation"/>의 증인 (35단계, WI-16). 5갈래 진리표. <c>ReleaseInfo</c> null 폴백은 오늘 동작 보존(_Today).
/// </summary>
public class UpdateCheckPresentationTests
{
    private static readonly UpdateReleaseInfo Release = new(
        "v9.9.9", new Version(9, 9, 9), "SS Pen 9.9.9", "notes", "https://example.invalid/release", null);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decide_SuccessWithUpdate_ShowsDialog_RegardlessOfManual(bool isManual) =>
        Assert.Equal(UpdateCheckOutcome.ShowDialog,
            UpdateCheckPresentation.Decide(new UpdateCheckResult(true, true, Release), isManual));

    [Fact]
    public void Decide_FailureManual_ShowsErrorDialog() =>
        Assert.Equal(UpdateCheckOutcome.ShowErrorDialog,
            UpdateCheckPresentation.Decide(new UpdateCheckResult(false, false, null, "boom"), isManual: true));

    [Fact]
    public void Decide_FailureAutomatic_LogsOnly() =>
        Assert.Equal(UpdateCheckOutcome.LogError,
            UpdateCheckPresentation.Decide(new UpdateCheckResult(false, false, null, "boom"), isManual: false));

    [Fact]
    public void Decide_UpToDateManual_ShowsUpToDate() =>
        Assert.Equal(UpdateCheckOutcome.ShowUpToDate,
            UpdateCheckPresentation.Decide(new UpdateCheckResult(true, false, null), isManual: true));

    [Fact]
    public void Decide_UpToDateAutomatic_IsSilent() =>
        Assert.Equal(UpdateCheckOutcome.Silent,
            UpdateCheckPresentation.Decide(new UpdateCheckResult(true, false, null), isManual: false));

    /// <summary>보존이지 승인이 아니다: HasUpdate인데 ReleaseInfo가 없으면 오늘은 '최신' 분기로 떨어진다.</summary>
    [Fact]
    public void Decide_HasUpdateButNullReleaseInfo_FallsToUpToDate_Today() =>
        Assert.Equal(UpdateCheckOutcome.ShowUpToDate,
            UpdateCheckPresentation.Decide(new UpdateCheckResult(true, true, null), isManual: true));

    private static readonly Version Current = new(1, 3, 5);

    [Fact]
    public void Describe_UpdateAvailable_MentionsCurrentAndRemoteVersions()
    {
        var line = UpdateCheckPresentation.Describe(new UpdateCheckResult(true, true, Release), Current);

        Assert.Contains("1.3.5", line);
        Assert.Contains("v9.9.9", line);
        Assert.Contains("새 버전 있음", line);
    }

    /// <summary>자동+최신은 Decide가 Silent라 화면에 아무것도 없다 — 이 한 줄이 "돌았는데 최신"의 유일한 흔적이다.</summary>
    [Fact]
    public void Describe_UpToDate_SaysLatestAndNamesRemoteTag()
    {
        var sameRelease = Release with { TagName = "v1.3.5", Version = new Version(1, 3, 5) };

        var line = UpdateCheckPresentation.Describe(new UpdateCheckResult(true, false, sameRelease), Current);

        Assert.Contains("v1.3.5", line);
        Assert.EndsWith("최신", line);
        Assert.DoesNotContain("새 버전", line);
    }

    [Fact]
    public void Describe_Failure_IncludesErrorMessage()
    {
        var line = UpdateCheckPresentation.Describe(
            new UpdateCheckResult(false, false, null, "서버 응답 오류 (403 rate limit exceeded)"), Current);

        Assert.Contains("1.3.5", line);
        Assert.Contains("실패", line);
        Assert.Contains("403", line);
    }

    [Fact]
    public void Describe_FailureWithoutMessage_DoesNotThrow_AndSaysUnknown()
    {
        var line = UpdateCheckPresentation.Describe(new UpdateCheckResult(false, false, null), Current);

        Assert.Contains("원인 미상", line);
    }

    [Fact]
    public void Describe_SuccessWithNullReleaseInfo_DoesNotThrow()
    {
        var upToDate = UpdateCheckPresentation.Describe(new UpdateCheckResult(true, false, null), Current);
        var hasUpdate = UpdateCheckPresentation.Describe(new UpdateCheckResult(true, true, null), Current);

        Assert.Contains("알 수 없음", upToDate);
        Assert.Contains("릴리스 정보 없음", hasUpdate);
    }
}
