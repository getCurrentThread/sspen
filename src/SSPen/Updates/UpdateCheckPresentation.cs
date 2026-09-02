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
}

/// <summary>
/// 업데이트 확인 결과의 표시 판정 (35단계, WI-16). AppController.CheckForUpdates의 5갈래 분기를 순수 표로 뺐다 — 어댑터는
/// 결과에 따라 대화상자/메시지 상자/로그를 부를 뿐이다.
/// 보존이지 승인이 아니다: <c>Success &amp;&amp; HasUpdate</c>여도 <c>ReleaseInfo</c>가 null이면 오늘은 '최신' 분기로 떨어진다.
/// </summary>
public static class UpdateCheckPresentation
{
    public static UpdateCheckOutcome Decide(UpdateCheckResult result, bool isManual)
    {
        if (result.Success && result.HasUpdate && result.ReleaseInfo is not null)
        {
            return UpdateCheckOutcome.ShowDialog;
        }
        if (!result.Success)
        {
            return isManual ? UpdateCheckOutcome.ShowErrorDialog : UpdateCheckOutcome.LogError;
        }
        return isManual ? UpdateCheckOutcome.ShowUpToDate : UpdateCheckOutcome.Silent;
    }
}
