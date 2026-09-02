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
}
