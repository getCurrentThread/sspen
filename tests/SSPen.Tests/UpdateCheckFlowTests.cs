using System.Windows;
using SSPen.Shell;
using SSPen.Updates;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="UpdateCheckFlow"/>의 증인 (77단계, A1-2). 루트에 있던 1.3.5 판정 — 결과별 로그 수준, 오류 문구 폴백, 판정별 표시 —
/// 과 그 순서(시작 로그 → 확인 → 요약 로그 → 표시)를 헤드리스로 고정한다. 모든 호출은 한 목록에 기록해 순서까지 단언한다.
/// 로그 문구는 옮기기 전 AppController의 것과 바이트 동일해야 한다(동작 보존).
/// </summary>
public class UpdateCheckFlowTests
{
    private static readonly Version Current = new(1, 3, 5);

    private static readonly UpdateReleaseInfo Release = new(
        "v9.9.9", new Version(9, 9, 9), "SS Pen 9.9.9", "notes", "https://example.invalid/release", null);

    /// <summary>확인은 결과를 동기로 즉시 콜백한다 — 프로덕션의 디스패처 마샬링은 흐름의 관심사가 아니다.</summary>
    private static (UpdateCheckFlow Flow, List<string> Calls) Rig(UpdateCheckResult result)
    {
        var calls = new List<string>();
        var flow = new UpdateCheckFlow(
            check: onResult =>
            {
                calls.Add("check");
                onResult(result);
            },
            current: () => Current,
            logInfo: text => calls.Add("info:" + text),
            logWarn: text => calls.Add("warn:" + text),
            showRelease: info => calls.Add("release:" + info.TagName),
            showMessage: (text, image) => calls.Add($"message:{image}:{text}"));
        return (flow, calls);
    }

    [Fact]
    public void Run_AutoUpToDate_LogsInfo_ShowsNothing()
    {
        var (flow, calls) = Rig(new UpdateCheckResult(true, false, null));

        flow.Run(isManual: false);

        Assert.Equal(
            [
                "info:업데이트 확인 시작 (현재 1.3.5, 자동)",
                "check",
                "info:업데이트 확인: 현재 1.3.5 / 원격 알 수 없음 → 최신",
            ],
            calls);
    }

    [Fact]
    public void Run_AutoFailure_LogsWarn_ShowsNothing()
    {
        var (flow, calls) = Rig(new UpdateCheckResult(false, false, null, "boom"));

        flow.Run(isManual: false);

        Assert.Equal(
            [
                "info:업데이트 확인 시작 (현재 1.3.5, 자동)",
                "check",
                "warn:업데이트 확인: 현재 1.3.5 → 실패 (boom)",
            ],
            calls);
    }

    /// <summary>오류 문구가 없으면 제목 문자열(<see cref="Strings.UpdateFailedTitle"/>)을 본문으로 쓴다 — 1.3.5 폴백 보존.</summary>
    [Fact]
    public void Run_ManualFailureWithoutMessage_ShowsUpdateFailedTitleAsWarning()
    {
        var (flow, calls) = Rig(new UpdateCheckResult(false, false, null));

        flow.Run(isManual: true);

        Assert.Equal($"message:{MessageBoxImage.Warning}:{Strings.UpdateFailedTitle}", calls[^1]);
        Assert.Contains("warn:업데이트 확인: 현재 1.3.5 → 실패 (원인 미상)", calls);
    }

    [Fact]
    public void Run_ManualFailureWithMessage_ShowsErrorMessage()
    {
        var (flow, calls) = Rig(new UpdateCheckResult(false, false, null, "서버 응답 오류 (503 Service Unavailable)"));

        flow.Run(isManual: true);

        Assert.Equal($"message:{MessageBoxImage.Warning}:서버 응답 오류 (503 Service Unavailable)", calls[^1]);
    }

    [Fact]
    public void Run_ManualUpToDate_ShowsLatestAlreadyAsInformation()
    {
        var (flow, calls) = Rig(new UpdateCheckResult(true, false, null));

        flow.Run(isManual: true);

        Assert.Equal($"message:{MessageBoxImage.Information}:{Strings.UpdateLatestAlready}", calls[^1]);
        Assert.Contains("info:업데이트 확인: 현재 1.3.5 / 원격 알 수 없음 → 최신", calls);
    }

    [Theory]
    [InlineData(true, "수동")]
    [InlineData(false, "자동")]
    public void Run_NewRelease_ShowsRelease_EvenWhenAuto(bool isManual, string origin)
    {
        var (flow, calls) = Rig(new UpdateCheckResult(true, true, Release));

        flow.Run(isManual);

        Assert.Equal(
            [
                $"info:업데이트 확인 시작 (현재 1.3.5, {origin})",
                "check",
                "info:업데이트 확인: 현재 1.3.5 / 원격 v9.9.9 → 새 버전 있음",
                "release:v9.9.9",
            ],
            calls);
    }

    /// <summary>
    /// 순서 증인: 콜백이 오기 전에는 시작 로그와 확인 요청만 있다(요약·표시는 결과를 기다린다). 결과가 오면 요약 로그가 표시보다 먼저다.
    /// 버전은 <see cref="UpdateCheckFlow.Run"/> 시점에 한 번 읽어 요약에도 같은 값을 쓴다 — 콜백 사이에 바뀐 값을 섞지 않는다.
    /// </summary>
    [Fact]
    public void Run_LogsStartThenSummaryBeforePresentation()
    {
        var calls = new List<string>();
        Action<UpdateCheckResult>? pending = null;
        var version = Current;
        var flow = new UpdateCheckFlow(
            check: onResult =>
            {
                calls.Add("check");
                pending = onResult;
            },
            current: () => version,
            logInfo: text => calls.Add("info:" + text),
            logWarn: text => calls.Add("warn:" + text),
            showRelease: info => calls.Add("release:" + info.TagName),
            showMessage: (text, image) => calls.Add($"message:{image}:{text}"));

        flow.Run(isManual: true);

        Assert.Equal(["info:업데이트 확인 시작 (현재 1.3.5, 수동)", "check"], calls);

        version = new Version(2, 0, 0);
        pending!(new UpdateCheckResult(false, false, null, "boom"));

        Assert.Equal(
            [
                "info:업데이트 확인 시작 (현재 1.3.5, 수동)",
                "check",
                "warn:업데이트 확인: 현재 1.3.5 → 실패 (boom)",
                $"message:{MessageBoxImage.Warning}:boom",
            ],
            calls);
    }
}
