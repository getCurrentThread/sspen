using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 버전 동기화의 헤드리스 증인 (93단계, A8-8). <c>src/SSPen/SSPen.csproj</c>의 <c>&lt;Version&gt;</c>과
/// <c>installer/SSPen.iss</c>의 <c>MyAppVersion</c>은 손으로 같이 올려야 하는 두 벌이다. 어긋나면 설치본 파일명(iss)과
/// 앱이 자기 버전으로 읽는 값(csproj)이 달라져 자동 업데이트가 무한 루프를 돌거나 영영 뜨지 않는다. 지금까지 이 검사는
/// release.yml의 태그 푸시 단계에만 있어 main CI에서는 초록으로 지나갔다 — 태그 항은 테스트가 알 수 없으므로 그 검사는
/// 그대로 두고, 여기서는 csproj = iss 두 항만 매 유닛 실행마다 잠근다.
/// </summary>
public class VersionSyncTests
{
    private const string SolutionFileName = "SSPen.sln";

    // PackageReference의 Version="…" 속성은 요소가 아니라서 걸리지 않는다.
    private static readonly Regex CsprojVersion = new(@"<Version>\s*([^<\s]+)\s*</Version>");

    // release.yml의 '버전 동기화 검증' 단계가 쓰는 패턴과 같다.
    private static readonly Regex IssVersion = new("^#define MyAppVersion \"([^\"]+)\"", RegexOptions.Multiline);

    [Fact]
    public void Csproj_Version_EqualsInstallerMyAppVersion()
    {
        var root = FindRepositoryRoot();

        var csprojVersion = ReadSingleVersion(Path.Combine(root, "src", "SSPen", "SSPen.csproj"), CsprojVersion);
        var issVersion = ReadSingleVersion(Path.Combine(root, "installer", "SSPen.iss"), IssVersion);

        Assert.True(csprojVersion == issVersion,
            $"SSPen.csproj <Version>({csprojVersion})과 SSPen.iss MyAppVersion({issVersion})이 다르다 — 두 곳을 같이 올린다.");
    }

    /// <summary>테스트 실행 위치(bin/…)에서 <see cref="SolutionFileName"/>이 있는 디렉터리까지 올라간다.</summary>
    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionFileName)))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"{AppContext.BaseDirectory}에서 위로 올라가며 {SolutionFileName}을 찾지 못했다 — 이 테스트는 저장소 체크아웃 안에서 돌아야 한다.");
    }

    /// <summary>파일에서 버전 패턴이 정확히 한 번 나와야 한다 — 없거나 둘 이상이면 어느 값을 믿을지 모르므로 실패한다.</summary>
    private static string ReadSingleVersion(string path, Regex pattern)
    {
        Assert.True(File.Exists(path), $"버전 원천 파일이 없다: {path}");

        var matches = pattern.Matches(File.ReadAllText(path));

        Assert.True(matches.Count == 1, $"{path}에서 버전 패턴 {pattern}이 {matches.Count}번 나왔다(정확히 1번이어야 한다).");
        return matches[0].Groups[1].Value;
    }
}
