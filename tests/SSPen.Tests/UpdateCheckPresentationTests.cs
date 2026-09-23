using SSPen.Updates;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="UpdateCheckPresentation"/>의 증인 (35단계, WI-16). 5갈래 진리표. <c>ReleaseInfo</c> null 폴백은 오늘 동작 보존(_Today).
/// 86단계(C-4)가 여섯째 갈래 <see cref="UpdateCheckOutcome.FocusExistingDialog"/>(새 버전 + 열린 대화상자)를 더했다.
/// 103단계(FINAL-REVIEW-UPDATE-CANCEL)가 일곱째 갈래 <see cref="UpdateCheckOutcome.DownloadInProgress"/>(새 버전 + 창 없음 + 다운로드 정리 중)를 더했다.
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

    /// <summary>
    /// 86단계 회귀 증인 (C-4): 새 버전 대화상자가 이미 열려 있으면 두 번째 창을 만들지 않고 그 창을 앞으로 가져온다.
    /// 시동 3초 자동 확인과 트레이·설정창 수동 확인이 겹치면 같은 내용의 Topmost 창이 둘 뜨고, 둘 다 '지금 업데이트'를 누르면
    /// 같은 설치 파일 경로에 동시에 File.Create를 해 한쪽은 공유 위반 오류, 다른 쪽은 설치·종료로 끝났다. 자동·수동 모두 같다.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decide_NewVersion_DialogAlreadyOpen_FocusesExisting(bool isManual) =>
        Assert.Equal(UpdateCheckOutcome.FocusExistingDialog,
            UpdateCheckPresentation.Decide(new UpdateCheckResult(true, true, Release), isManual, dialogOpen: true));

    /// <summary>열린 대화상자는 새 버전 판정에만 끼어든다 — 실패·최신·ReleaseInfo 없음 폴백은 dialogOpen과 무관하게 기존 결과다.</summary>
    [Theory]
    [InlineData(false, false, false, true, UpdateCheckOutcome.ShowErrorDialog)]
    [InlineData(false, false, false, false, UpdateCheckOutcome.LogError)]
    [InlineData(true, false, false, true, UpdateCheckOutcome.ShowUpToDate)]
    [InlineData(true, false, false, false, UpdateCheckOutcome.Silent)]
    [InlineData(true, true, false, true, UpdateCheckOutcome.ShowUpToDate)]
    public void Decide_DialogOpen_DoesNotAffectFailureOrUpToDate(
        bool success, bool hasUpdate, bool hasRelease, bool isManual, UpdateCheckOutcome expected) =>
        Assert.Equal(expected,
            UpdateCheckPresentation.Decide(
                new UpdateCheckResult(success, hasUpdate, hasRelease ? Release : null), isManual, dialogOpen: true));

    /// <summary>
    /// 기존 표 전수 (35단계 5갈래): dialogOpen을 생략하면(기본값 false) 86단계 이전과 한 칸도 다르지 않다 — Success·HasUpdate·ReleaseInfo·
    /// 수동 여부 16조합. 명시적 false도 같은 값이어야 한다.
    /// </summary>
    [Theory]
    [InlineData(true, true, true, true, UpdateCheckOutcome.ShowDialog)]
    [InlineData(true, true, true, false, UpdateCheckOutcome.ShowDialog)]
    [InlineData(true, true, false, true, UpdateCheckOutcome.ShowUpToDate)]
    [InlineData(true, true, false, false, UpdateCheckOutcome.Silent)]
    [InlineData(true, false, true, true, UpdateCheckOutcome.ShowUpToDate)]
    [InlineData(true, false, true, false, UpdateCheckOutcome.Silent)]
    [InlineData(true, false, false, true, UpdateCheckOutcome.ShowUpToDate)]
    [InlineData(true, false, false, false, UpdateCheckOutcome.Silent)]
    [InlineData(false, true, true, true, UpdateCheckOutcome.ShowErrorDialog)]
    [InlineData(false, true, true, false, UpdateCheckOutcome.LogError)]
    [InlineData(false, true, false, true, UpdateCheckOutcome.ShowErrorDialog)]
    [InlineData(false, true, false, false, UpdateCheckOutcome.LogError)]
    [InlineData(false, false, true, true, UpdateCheckOutcome.ShowErrorDialog)]
    [InlineData(false, false, true, false, UpdateCheckOutcome.LogError)]
    [InlineData(false, false, false, true, UpdateCheckOutcome.ShowErrorDialog)]
    [InlineData(false, false, false, false, UpdateCheckOutcome.LogError)]
    public void Decide_DefaultDialogOpen_MatchesLegacyTable(
        bool success, bool hasUpdate, bool hasRelease, bool isManual, UpdateCheckOutcome expected)
    {
        var result = new UpdateCheckResult(success, hasUpdate, hasRelease ? Release : null);

        Assert.Equal(expected, UpdateCheckPresentation.Decide(result, isManual));
        Assert.Equal(expected, UpdateCheckPresentation.Decide(result, isManual, dialogOpen: false));
        Assert.Equal(expected, UpdateCheckPresentation.Decide(result, isManual, dialogOpen: false, downloading: false));
    }

    /// <summary>
    /// 103단계 증인 (FINAL-REVIEW-UPDATE-CANCEL): 다운로드 중에 닫힌 대화상자의 작업이 아직 정리 중이면(창 없음 + 다운로드 중) 새 버전 판정은
    /// 창을 열지 않는다 — 그 구간에 새 창을 열면 '지금 업데이트'가 끝나 가는 작업과 같은 설치 파일 경로를 두고 다툰다. 자동·수동 모두 같다.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decide_NewVersion_DownloadingWithoutDialog_ReturnsDownloadInProgress(bool isManual) =>
        Assert.Equal(UpdateCheckOutcome.DownloadInProgress,
            UpdateCheckPresentation.Decide(new UpdateCheckResult(true, true, Release), isManual, dialogOpen: false, downloading: true));

    /// <summary>열린 채 내려받는 평소 상태에서는 열린 창을 앞으로 가져오는 86단계 판정이 먼저다.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Decide_NewVersion_DialogOpenWhileDownloading_FocusesExisting(bool isManual) =>
        Assert.Equal(UpdateCheckOutcome.FocusExistingDialog,
            UpdateCheckPresentation.Decide(new UpdateCheckResult(true, true, Release), isManual, dialogOpen: true, downloading: true));

    /// <summary>진행 중 다운로드도 새 버전 판정에만 끼어든다 — 실패·최신·ReleaseInfo 없음 폴백은 기존 결과다.</summary>
    [Theory]
    [InlineData(false, false, false, true, UpdateCheckOutcome.ShowErrorDialog)]
    [InlineData(false, false, false, false, UpdateCheckOutcome.LogError)]
    [InlineData(true, false, false, true, UpdateCheckOutcome.ShowUpToDate)]
    [InlineData(true, false, false, false, UpdateCheckOutcome.Silent)]
    [InlineData(true, true, false, true, UpdateCheckOutcome.ShowUpToDate)]
    public void Decide_Downloading_DoesNotAffectFailureOrUpToDate(
        bool success, bool hasUpdate, bool hasRelease, bool isManual, UpdateCheckOutcome expected) =>
        Assert.Equal(expected,
            UpdateCheckPresentation.Decide(
                new UpdateCheckResult(success, hasUpdate, hasRelease ? Release : null), isManual, dialogOpen: false, downloading: true));

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
