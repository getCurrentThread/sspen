using System.Text.Json;

namespace SSPen.Updates;

/// <summary>
/// GitHub Releases 응답 파싱 및 버전 비교를 담당하는 순수 코어 로직.
/// UI나 네트워크 의존성이 없어 단위 테스트가 용이하다.
/// </summary>
public static class UpdateCheckerCore
{
    /// <summary>
    /// 태그 문자열(예: "v1.3.0", "1.3.0", "v1.4.1.0")을 Version 객체로 안전하게 변환한다.
    /// </summary>
    public static bool TryParseVersion(string? tagName, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return false;
        }

        var cleaned = tagName.Trim().TrimStart('v', 'V');
        var dashIndex = cleaned.IndexOf('-');
        if (dashIndex >= 0)
        {
            cleaned = cleaned[..dashIndex];
        }

        return Version.TryParse(cleaned, out version);
    }

    /// <summary>
    /// 원격 버전이 현재 버전보다 높은지 비교한다.
    /// </summary>
    public static bool IsNewerVersion(Version currentVersion, Version remoteVersion) =>
        remoteVersion > currentVersion;

    /// <summary>
    /// GitHub Release Assets에서 Inno Setup 설치 프로그램(.exe) 다운로드 URL을 탐색한다.
    /// </summary>
    public static string? FindInstallerDownloadUrl(JsonElement assetsElement)
    {
        if (assetsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? fallbackExeUrl = null;

        foreach (var asset in assetsElement.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameProp) ||
                !asset.TryGetProperty("browser_download_url", out var urlProp))
            {
                continue;
            }

            var name = nameProp.GetString() ?? string.Empty;
            var url = urlProp.GetString();

            if (string.IsNullOrEmpty(url) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // SSPen-Setup-*.exe 또는 Setup*.exe 우선 선택
            if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            fallbackExeUrl ??= url;
        }

        return fallbackExeUrl;
    }

    /// <summary>
    /// GitHub /releases/latest API의 JSON 응답을 파싱하여 업데이트 정보를 추출한다.
    /// </summary>
    public static UpdateCheckResult ParseReleaseJson(string json, Version currentVersion)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagProp))
            {
                return new UpdateCheckResult(false, false, null, "tag_name 속성을 찾을 수 없습니다.");
            }

            var tagName = tagProp.GetString() ?? string.Empty;
            if (!TryParseVersion(tagName, out var remoteVersion) || remoteVersion is null)
            {
                return new UpdateCheckResult(false, false, null, $"버전 형식을 파싱할 수 없습니다: {tagName}");
            }

            var title = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? tagName : tagName;
            var notes = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? string.Empty : string.Empty;
            var htmlUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? string.Empty : string.Empty;

            string? installerUrl = null;
            if (root.TryGetProperty("assets", out var assetsProp))
            {
                installerUrl = FindInstallerDownloadUrl(assetsProp);
            }

            var releaseInfo = new UpdateReleaseInfo(
                TagName: tagName,
                Version: remoteVersion,
                ReleaseTitle: title,
                ReleaseNotes: notes,
                HtmlUrl: htmlUrl,
                InstallerDownloadUrl: installerUrl
            );

            var hasUpdate = IsNewerVersion(currentVersion, remoteVersion);
            return new UpdateCheckResult(true, hasUpdate, releaseInfo);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(false, false, null, $"JSON 파싱 오류: {ex.Message}");
        }
    }
}
