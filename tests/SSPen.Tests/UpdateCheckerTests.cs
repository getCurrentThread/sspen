using SSPen.Updates;
using Xunit;

namespace SSPen.Tests;

public sealed class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.3.0", 1, 3, 0)]
    [InlineData("1.3.0", 1, 3, 0)]
    [InlineData("V2.0", 2, 0, -1)]
    [InlineData("v1.4.2.1", 1, 4, 2)]
    [InlineData("v1.5.0-preview1", 1, 5, 0)]
    public void TryParseVersion_ValidTag_ReturnsExpectedVersion(string tag, int major, int minor, int build)
    {
        var success = UpdateCheckerCore.TryParseVersion(tag, out var version);

        Assert.True(success);
        Assert.NotNull(version);
        Assert.Equal(major, version!.Major);
        Assert.Equal(minor, version.Minor);
        if (build >= 0)
        {
            Assert.Equal(build, version.Build);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("vABC")]
    public void TryParseVersion_InvalidTag_ReturnsFalse(string? tag)
    {
        var success = UpdateCheckerCore.TryParseVersion(tag, out var version);

        Assert.False(success);
        Assert.Null(version);
    }

    [Fact]
    public void IsNewerVersion_ComparesCorrectly()
    {
        var current = new Version(1, 3, 0);

        Assert.True(UpdateCheckerCore.IsNewerVersion(current, new Version(1, 3, 1)));
        Assert.True(UpdateCheckerCore.IsNewerVersion(current, new Version(1, 4, 0)));
        Assert.True(UpdateCheckerCore.IsNewerVersion(current, new Version(2, 0, 0)));

        Assert.False(UpdateCheckerCore.IsNewerVersion(current, new Version(1, 3, 0)));
        Assert.False(UpdateCheckerCore.IsNewerVersion(current, new Version(1, 2, 9)));
    }

    [Fact]
    public void ParseReleaseJson_WithNewerVersionAndInstaller_ReturnsSuccessWithUpdate()
    {
        var current = new Version(1, 3, 0);
        var json = """
        {
          "tag_name": "v1.4.0",
          "name": "SS Pen v1.4.0 릴리즈",
          "body": "신규 기능 추가\n- 자동 업데이트 지원",
          "html_url": "https://github.com/getCurrentThread/sspen/releases/tag/v1.4.0",
          "assets": [
            {
              "name": "SSPen-portable-win-x64.zip",
              "browser_download_url": "https://github.com/getCurrentThread/sspen/releases/download/v1.4.0/SSPen-portable-win-x64.zip"
            },
            {
              "name": "SSPen-Setup-v1.4.0.exe",
              "browser_download_url": "https://github.com/getCurrentThread/sspen/releases/download/v1.4.0/SSPen-Setup-v1.4.0.exe"
            }
          ]
        }
        """;

        var result = UpdateCheckerCore.ParseReleaseJson(json, current);

        Assert.True(result.Success);
        Assert.True(result.HasUpdate);
        Assert.NotNull(result.ReleaseInfo);
        Assert.Equal("v1.4.0", result.ReleaseInfo!.TagName);
        Assert.Equal(new Version(1, 4, 0), result.ReleaseInfo.Version);
        Assert.Equal("SS Pen v1.4.0 릴리즈", result.ReleaseInfo.ReleaseTitle);
        Assert.Equal("신규 기능 추가\n- 자동 업데이트 지원", result.ReleaseInfo.ReleaseNotes);
        Assert.Equal("https://github.com/getCurrentThread/sspen/releases/tag/v1.4.0", result.ReleaseInfo.HtmlUrl);
        Assert.Equal("https://github.com/getCurrentThread/sspen/releases/download/v1.4.0/SSPen-Setup-v1.4.0.exe", result.ReleaseInfo.InstallerDownloadUrl);
    }

    [Fact]
    public void ParseReleaseJson_SameVersion_ReturnsHasUpdateFalse()
    {
        var current = new Version(1, 3, 0);
        var json = """
        {
          "tag_name": "v1.3.0",
          "name": "SS Pen v1.3.0",
          "body": "안정화 버전",
          "html_url": "https://github.com/getCurrentThread/sspen/releases/tag/v1.3.0",
          "assets": []
        }
        """;

        var result = UpdateCheckerCore.ParseReleaseJson(json, current);

        Assert.True(result.Success);
        Assert.False(result.HasUpdate);
        Assert.NotNull(result.ReleaseInfo);
    }

    [Fact]
    public void ParseReleaseJson_MalformedJson_ReturnsFailure()
    {
        var current = new Version(1, 3, 0);
        var json = "not a valid json content";

        var result = UpdateCheckerCore.ParseReleaseJson(json, current);

        Assert.False(result.Success);
        Assert.False(result.HasUpdate);
        Assert.Null(result.ReleaseInfo);
        Assert.NotNull(result.ErrorMessage);
    }
}
