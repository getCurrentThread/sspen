namespace SSPen.Updates;

/// <summary>업데이트 확인 결과를 사용자에게 어떻게 보일지 (35단계).</summary>
public enum UpdateCheckOutcome
{
    /// <summary>새 버전 — 업데이트 대화상자.</summary>
    ShowDialog,

    /// <summary>실패 + 수동 확인 — 오류 메시지 상자.</summary>
    ShowErrorDialog,

    /// <summary>실패 + 자동 확인 — 로그만.</summary>
    LogError,

    /// <summary>최신 + 수동 확인 — "최신 버전" 안내.</summary>
    ShowUpToDate,

    /// <summary>최신 + 자동 확인 — 아무것도 보이지 않는다.</summary>
    Silent,

    /// <summary>
    /// 새 버전 + 대화상자가 이미 열려 있음 — 두 번째 창 대신 열린 창을 앞으로 (86단계, C-4). 창이 둘이면 둘 다 '지금 업데이트'로
    /// 같은 설치 파일 경로에 동시에 내려받아 한쪽은 공유 위반 오류, 다른 쪽은 설치·종료로 끝난다.
    /// </summary>
    FocusExistingDialog,
}

/// <summary>
/// 업데이트 확인 결과의 표시 판정 (35단계, WI-16). AppController.CheckForUpdates의 5갈래 분기를 순수 표로 뺐다 — 어댑터는
/// 결과에 따라 대화상자/메시지 상자/로그를 부를 뿐이다.
/// 보존이지 승인이 아니다: <c>Success &amp;&amp; HasUpdate</c>여도 <c>ReleaseInfo</c>가 null이면 오늘은 '최신' 분기로 떨어진다.
/// </summary>
public static class UpdateCheckPresentation
{
    /// <param name="result">확인 결과.</param>
    /// <param name="isManual">트레이·설정창의 "지금 확인"(참)인지 시동 자동 확인(거짓)인지.</param>
    /// <param name="dialogOpen">
    /// 새 버전 대화상자가 이미 열려 있는지 (86단계, C-4). 새 버전 판정만 <see cref="UpdateCheckOutcome.FocusExistingDialog"/>로
    /// 바꾸고, 실패·최신 판정에는 끼어들지 않는다. 기본값 false면 35단계 5갈래 표와 같다.
    /// </param>
    public static UpdateCheckOutcome Decide(UpdateCheckResult result, bool isManual, bool dialogOpen = false)
    {
        if (result.Success && result.HasUpdate && result.ReleaseInfo is not null)
        {
            return dialogOpen ? UpdateCheckOutcome.FocusExistingDialog : UpdateCheckOutcome.ShowDialog;
        }
        if (!result.Success)
        {
            return isManual ? UpdateCheckOutcome.ShowErrorDialog : UpdateCheckOutcome.LogError;
        }
        return isManual ? UpdateCheckOutcome.ShowUpToDate : UpdateCheckOutcome.Silent;
    }

    /// <summary>
    /// 확인 결과를 로그 한 줄로 서술한다. 자동 확인이 '최신'이면 화면에 아무것도 뜨지 않으므로(<see cref="UpdateCheckOutcome.Silent"/>),
    /// 이 한 줄이 "돌았는데 최신이었다"와 "아예 안 돌았다"를 가르는 유일한 흔적이다. 판정과 같은 순수 코어에 둬서 헤드리스로 검증한다.
    /// </summary>
    public static string Describe(UpdateCheckResult result, Version currentVersion)
    {
        if (!result.Success)
        {
            return $"업데이트 확인: 현재 {currentVersion} → 실패 ({result.ErrorMessage ?? "원인 미상"})";
        }

        var remote = result.ReleaseInfo?.TagName ?? "알 수 없음";
        var verdict = !result.HasUpdate
            ? "최신"
            : result.ReleaseInfo is null ? "새 버전 있음 (릴리스 정보 없음)" : "새 버전 있음";
        return $"업데이트 확인: 현재 {currentVersion} / 원격 {remote} → {verdict}";
    }
}
