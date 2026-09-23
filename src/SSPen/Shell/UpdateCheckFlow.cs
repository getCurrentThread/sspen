using System.Windows;
using SSPen.Updates;

namespace SSPen.Shell;

/// <summary>
/// 업데이트 확인 한 번의 흐름 (77단계, A1-2). 35단계가 표시 판정(<see cref="UpdateCheckPresentation.Decide"/>)과 로그 서술
/// (<see cref="UpdateCheckPresentation.Describe"/>)을 순수 표로 뺐지만, 1.3.5가 더한 나머지 판정 — 결과에 따른 로그 수준,
/// 오류 문구 폴백(<c>ErrorMessage</c>가 없으면 <see cref="Strings.UpdateFailedTitle"/>을 본문으로), 판정별 표시 호출 — 은
/// 루트에 다시 쌓여 증인이 없었다. 여기로 옮겨 <c>UpdateCheckFlowTests</c>가 헤드리스로 지킨다.
///
/// 순서가 계약이다: 현재 버전 읽기 → "업데이트 확인 시작" 로그 → <c>check</c> → (결과 콜백에서) 요약 로그(성공 Info, 실패 Warn)
/// → 판정별 표시. 결과는 화면 판정과 무관하게 항상 로그로 남긴다 — 자동+최신은 Silent라 그 줄이 유일한 흔적이다.
/// 콜백의 스레드는 <c>check</c>가 정한다(프로덕션 <see cref="UpdateService.CheckForUpdates"/>는 디스패처로 마샬링한다) —
/// 이 클래스는 Task도 스레드도 만들지 않는다.
/// </summary>
/// <param name="check">확인 요청 — 결과를 UI 스레드 콜백으로 돌려준다 (<see cref="UpdateService.CheckForUpdates"/>).</param>
/// <param name="current">현재 버전 (<see cref="UpdateService.CurrentVersion"/>).</param>
/// <param name="logInfo">정보 로그 (<c>Log.Info</c>).</param>
/// <param name="logWarn">경고 로그 (<c>Log.Warn</c>).</param>
/// <param name="showRelease">새 버전 대화상자 표시 (<see cref="UpdateDialog"/>).</param>
/// <param name="showMessage">안내 상자 표시(본문, 아이콘) — owner 선택은 호출자가 한다 (<see cref="DialogOwnerRules"/>).</param>
public sealed class UpdateCheckFlow(
    Action<Action<UpdateCheckResult>> check,
    Func<Version> current,
    Action<string> logInfo,
    Action<string> logWarn,
    Action<UpdateReleaseInfo> showRelease,
    Action<string, MessageBoxImage> showMessage)
{
    /// <summary>확인을 시작한다. <paramref name="isManual"/>은 트레이·설정창의 "지금 확인"(참)과 시동 자동 확인(거짓)을 가른다.</summary>
    public void Run(bool isManual)
    {
        var version = current();
        logInfo($"업데이트 확인 시작 (현재 {version}, {(isManual ? "수동" : "자동")})");
        check(result => Present(result, version, isManual));
    }

    private void Present(UpdateCheckResult result, Version version, bool isManual)
    {
        var summary = UpdateCheckPresentation.Describe(result, version);
        if (result.Success)
        {
            logInfo(summary);
        }
        else
        {
            logWarn(summary);
        }

        switch (UpdateCheckPresentation.Decide(result, isManual))
        {
            case UpdateCheckOutcome.ShowDialog:
                showRelease(result.ReleaseInfo!);
                break;

            case UpdateCheckOutcome.ShowErrorDialog:
                showMessage(result.ErrorMessage ?? Strings.UpdateFailedTitle, MessageBoxImage.Warning);
                break;

            case UpdateCheckOutcome.ShowUpToDate:
                showMessage(Strings.UpdateLatestAlready, MessageBoxImage.Information);
                break;

            case UpdateCheckOutcome.LogError: // 위에서 이미 로그를 남겼다.
            case UpdateCheckOutcome.Silent:
                break;
        }
    }
}
