namespace SSPen.Updates;

/// <summary>
/// GitHub Release에서 추출한 릴리즈 정보.
/// </summary>
public sealed record UpdateReleaseInfo(
    string TagName,
    Version Version,
    string ReleaseTitle,
    string ReleaseNotes,
    string HtmlUrl,
    string? InstallerDownloadUrl
);

/// <summary>
/// 업데이트 검사 결과.
/// </summary>
public sealed record UpdateCheckResult(
    bool Success,
    bool HasUpdate,
    UpdateReleaseInfo? ReleaseInfo,
    string? ErrorMessage = null
);
