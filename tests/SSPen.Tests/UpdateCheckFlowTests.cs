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

    /// <summary>
    /// 루트의 <c>_updateDialog</c> 필드를 흉내 낸다 (86단계, C-4): <c>showRelease</c>가 창을 열고, 닫히면(<see cref="Close"/>) 비운다.
    /// </summary>
    private sealed class DialogSlot
    {
        public bool Open { get; private set; }

        public void Show() => Open = true;

        public void Close() => Open = false;
    }

    /// <summary>확인은 결과를 동기로 즉시 콜백한다 — 프로덕션의 디스패처 마샬링은 흐름의 관심사가 아니다.</summary>
    private static (UpdateCheckFlow Flow, List<string> Calls) Rig(UpdateCheckResult result, DialogSlot? dialog = null)
    {
        var calls = new List<string>();
        var slot = dialog ?? new DialogSlot();
        var flow = new UpdateCheckFlow(
            check: onResult =>
            {
                calls.Add("check");
                onResult(result);
            },
            current: () => Current,
            logInfo: text => calls.Add("info:" + text),
            logWarn: text => calls.Add("warn:" + text),
            showRelease: info =>
            {
                calls.Add("release:" + info.TagName);
                slot.Show();
            },
            dialogOpen: () => slot.Open,
            focusDialog: () => calls.Add("focus"),
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
            dialogOpen: () => false,
            focusDialog: () => calls.Add("focus"),
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

    /// <summary>
    /// 86단계 회귀 증인 (C-4): 새 버전 대화상자가 떠 있는 동안 다시 확인하면 두 번째 창 대신 열린 창을 앞으로 가져온다 —
    /// showRelease는 한 번, focus는 한 번. 요약 로그는 판정과 무관하게 두 번 다 남는다.
    /// </summary>
    [Fact]
    public void Run_TwiceWithNewVersion_ShowsReleaseOnce_ThenFocuses()
    {
        var (flow, calls) = Rig(new UpdateCheckResult(true, true, Release));

        flow.Run(isManual: false);
        flow.Run(isManual: true);

        Assert.Equal(
            [
                "info:업데이트 확인 시작 (현재 1.3.5, 자동)",
                "check",
                "info:업데이트 확인: 현재 1.3.5 / 원격 v9.9.9 → 새 버전 있음",
                "release:v9.9.9",
                "info:업데이트 확인 시작 (현재 1.3.5, 수동)",
                "check",
                "info:업데이트 확인: 현재 1.3.5 / 원격 v9.9.9 → 새 버전 있음",
                "focus",
            ],
            calls);
    }

    /// <summary>
    /// 신고된 경합 그대로 (C-4): 시동 자동 확인이 끝나기 전에 수동 확인이 시작돼 두 요청이 동시에 떠 있다. 열림 여부는
    /// Run 시점이 아니라 <b>결과가 도착한 시점</b>에 읽어야 한다 — Run 시점에 읽으면 둘 다 '닫힘'을 보고 창을 둘 띄운다.
    /// </summary>
    [Fact]
    public void Run_OverlappingChecksWithNewVersion_SecondResultFocusesFirstDialog()
    {
        var calls = new List<string>();
        var pending = new List<Action<UpdateCheckResult>>();
        var dialog = new DialogSlot();
        var flow = new UpdateCheckFlow(
            check: pending.Add,
            current: () => Current,
            logInfo: _ => { },
            logWarn: _ => { },
            showRelease: info =>
            {
                calls.Add("release:" + info.TagName);
                dialog.Show();
            },
            dialogOpen: () => dialog.Open,
            focusDialog: () => calls.Add("focus"),
            showMessage: (text, image) => calls.Add($"message:{image}:{text}"));

        flow.Run(isManual: false);
        flow.Run(isManual: true);
        Assert.Empty(calls);

        pending[0](new UpdateCheckResult(true, true, Release));
        pending[1](new UpdateCheckResult(true, true, Release));

        Assert.Equal(["release:v9.9.9", "focus"], calls);
    }

    /// <summary>단일 인스턴스는 '열려 있는 동안'만이다 — 창이 닫힌 뒤(루트는 Closed에서 필드를 비운다) 다시 확인하면 새 창을 띄운다.</summary>
    [Fact]
    public void Run_NewVersionAfterDialogClosed_ShowsReleaseAgain()
    {
        var dialog = new DialogSlot();
        var (flow, calls) = Rig(new UpdateCheckResult(true, true, Release), dialog);

        flow.Run(isManual: true);
        dialog.Close();
        flow.Run(isManual: true);

        Assert.Equal(2, calls.Count(c => c == "release:v9.9.9"));
        Assert.DoesNotContain("focus", calls);
    }

    /// <summary>열린 대화상자는 새 버전 판정에만 끼어든다 — 그동안의 수동 확인 실패는 여전히 경고 상자로, 창을 앞으로 가져오지 않는다.</summary>
    [Fact]
    public void Run_ManualFailureWhileDialogOpen_ShowsWarning_DoesNotFocus()
    {
        var dialog = new DialogSlot();
        dialog.Show();
        var (flow, calls) = Rig(new UpdateCheckResult(false, false, null, "boom"), dialog);

        flow.Run(isManual: true);

        Assert.Equal($"message:{MessageBoxImage.Warning}:boom", calls[^1]);
        Assert.DoesNotContain("focus", calls);
    }
}
