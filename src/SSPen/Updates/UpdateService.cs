using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows.Threading;
using SSPen.Diagnostics;

namespace SSPen.Updates;

/// <summary>
/// 업데이트 확인, 다운로드 및 무음 설치를 총괄하는 서비스.
/// </summary>
public sealed class UpdateService
{
    private const string DefaultApiUrl = "https://api.github.com/repos/getCurrentThread/sspen/releases/latest";

    /// <summary>릴리스 목록 웹 페이지 — 릴리스에 <c>html_url</c>이 없을 때 대화상자가 여는 폴백 (77단계, A9-7 (d): 슬러그를 한 파일에 둔다).</summary>
    public const string ReleasesPageUrl = "https://github.com/getCurrentThread/sspen/releases";

    private readonly Dispatcher _dispatcher;
    private readonly Action _exitApp;
    private readonly string _apiUrl;
    private readonly HttpClient _httpClient;
    private readonly Action<string> _launchInstaller;

    /// <param name="launchInstaller">
    /// 다운로드를 마친 설치 파일 경로를 받아 실행하는 이음매 (77단계, A1-8). null이면 <see cref="LaunchSilentInstallerAndExit"/>
    /// (프로덕션 — AppController는 넘기지 않는다). 테스트는 경로를 기록만 하는 람다를 넣어 cmd.exe 실행과 앱 종료 없이 다운로드 경로를 검증한다.
    /// </param>
    public UpdateService(
        Dispatcher dispatcher,
        Action exitApp,
        string apiUrl = DefaultApiUrl,
        HttpClient? httpClient = null,
        Action<string>? launchInstaller = null)
    {
        _dispatcher = dispatcher;
        _exitApp = exitApp;
        _apiUrl = apiUrl;
        _httpClient = httpClient ?? CreateDefaultHttpClient();
        _launchInstaller = launchInstaller ?? LaunchSilentInstallerAndExit;
    }

    /// <summary>
    /// 현재 실행 중인 어셈블리의 버전(예: 1.3.0).
    /// </summary>
    public static Version CurrentVersion
    {
        get
        {
            var asm = typeof(UpdateService).Assembly;
            var ver = asm.GetName().Version;
            return ver is not null ? new Version(ver.Major, ver.Minor, Math.Max(0, ver.Build)) : new Version(1, 0, 0);
        }
    }

    /// <summary>
    /// 백그라운드 스레드에서 최신 릴리즈 정보를 조회하여 디스패처로 결과를 반환한다.
    /// </summary>
    public void CheckForUpdates(Action<UpdateCheckResult> onResult)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            UpdateCheckResult result;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _apiUrl);
                using var response = _httpClient.Send(request);
                if (!response.IsSuccessStatusCode)
                {
                    result = new UpdateCheckResult(false, false, null, $"서버 응답 오류 ({(int)response.StatusCode} {response.ReasonPhrase})");
                }
                else
                {
                    using var reader = new StreamReader(response.Content.ReadAsStream());
                    var json = reader.ReadToEnd();
                    result = UpdateCheckerCore.ParseReleaseJson(json, CurrentVersion);
                }
            }
            catch (Exception ex)
            {
                result = new UpdateCheckResult(false, false, null, $"업데이트 확인 실패: {ex.Message}");
            }

            _dispatcher.BeginInvoke(() => onResult(result));
        });
    }

    /// <summary>
    /// 최신 설치 프로그램을 백그라운드에서 다운로드하고, 완료 시 무음 설치를 실행한 후 앱을 재시작한다.
    /// 다운로드도 확인 경로처럼 동기 <see cref="HttpClient.Send(HttpRequestMessage, HttpCompletionOption)"/>를 스레드풀에서 쓴다 —
    /// Task API를 GetResult로 막던 예전 호출을 걷어 냈다 (77단계, A1-8 — 업데이트 계층도 no-async 규칙).
    /// 디스패처 순서는 진행률 → <c>onCompleted(null)</c> → 설치 실행 이음매다.
    /// </summary>
    public void DownloadAndInstallSilently(
        UpdateReleaseInfo info,
        Action<double> onProgress,
        Action<Exception?> onCompleted)
    {
        if (string.IsNullOrEmpty(info.InstallerDownloadUrl))
        {
            onCompleted(new InvalidOperationException("설치 프로그램 다운로드 URL이 없습니다."));
            return;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "SSPen-Update");
                Directory.CreateDirectory(tempDir);
                var installerPath = Path.Combine(tempDir, $"SSPen-Setup-{info.TagName}.exe");

                using var request = new HttpRequestMessage(HttpMethod.Get, info.InstallerDownloadUrl);
                using (var response = _httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                    using var source = response.Content.ReadAsStream();
                    using var destination = File.Create(installerPath);

                    var buffer = new byte[81920];
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        destination.Write(buffer, 0, bytesRead);
                        totalRead += bytesRead;
                        if (totalBytes > 0)
                        {
                            var progress = (double)totalRead / totalBytes;
                            _dispatcher.BeginInvoke(() => onProgress(progress));
                        }
                    }
                }

                _dispatcher.BeginInvoke(() =>
                {
                    onCompleted(null);
                    _launchInstaller(installerPath);
                });
            }
            catch (Exception ex)
            {
                _dispatcher.BeginInvoke(() => onCompleted(ex));
            }
        });
    }

    /// <summary>
    /// Inno Setup 설치 프로그램을 무음 모드로 실행하고, 완료 후 새 버전의 앱을 재시작하도록 체이닝한 뒤 현재 앱을 종료한다.
    /// 명령줄은 <see cref="UpdateInstallPlan.CommandLine"/>이 만든다. 그 명령을 로그로 남기고, 실행 실패는 좁은 필터
    /// (<see cref="UpdateInstallPlan.IsLaunchFailure"/>)로 잡아 로그를 남긴다 — 무음 설치 실패가 로그에 흔적 없이 앱만 사라지던 것을
    /// 고친다 (77단계, A1-8). 실패해도 앱을 종료하는 것은 예전 그대로다.
    /// 명령 로그는 실행 <b>뒤</b>에 쓴다 (92단계): <c>Log</c>는 <c>IOException</c>만 삼키므로, 실행 전 try 안에서 그 밖의 로그 예외
    /// (예: <c>UnauthorizedAccessException</c>)가 나면 필터를 지나쳐 설치 체인이 아예 실행되지 않았다. 로그 줄 순서는 그대로다 —
    /// 성공이면 "체인 실행" → "종료 요청", 실패면 "체인 실행" → "실행 실패" 경고.
    /// </summary>
    public void LaunchSilentInstallerAndExit(string installerPath)
    {
        string? cmdArgs = null;
        bool launched = false;
        try
        {
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe) || !File.Exists(currentExe))
            {
                currentExe = Assembly.GetEntryAssembly()?.Location ?? Path.Combine(AppContext.BaseDirectory, "SSPen.exe");
            }

            // Inno Setup 스위치와 체이닝(start /wait … & start 새 exe)의 설명은 UpdateInstallPlan.CommandLine에 있다.
            cmdArgs = UpdateInstallPlan.CommandLine(installerPath, currentExe);

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = cmdArgs,
                CreateNoWindow = true,
                UseShellExecute = false,
            };

            Process.Start(startInfo);
            launched = true;
        }
        catch (Exception ex) when (UpdateInstallPlan.IsLaunchFailure(ex))
        {
            // 실패 경로도 실행하려던 명령을 먼저 남긴다 — 92단계 전과 같은 줄 순서.
            LogChain(cmdArgs);
            // 프로세스 시작 실패 시 일반 실행 시도
            Log.Warn($"무음 설치 실행 실패, 일반 실행 재시도: {ex.Message}");
            try
            {
                Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
            }
            catch (Exception retryEx) when (UpdateInstallPlan.IsLaunchFailure(retryEx))
            {
                // 실패해도 아래에서 앱은 종료한다 (예전 동작 그대로) — 로그만 남긴다.
                Log.Warn($"설치 프로그램 실행 실패: {retryEx.Message}");
            }
        }

        // 로그가 설치를 막지 않도록 체인을 띄운 뒤에 남긴다 (92단계) — 로그 예외가 나도 체인은 이미 떴다.
        if (launched)
        {
            LogChain(cmdArgs);
        }

        // 현재 앱 종료
        _exitApp();
    }

    /// <summary>실행한(또는 실행하려던) 설치 체인 명령의 로그 한 줄 — 명령을 만들기 전에 실패했으면 남기지 않는다.</summary>
    private static void LogChain(string? cmdArgs)
    {
        if (cmdArgs is not null)
        {
            Log.Info($"무음 설치 체인 실행: cmd.exe {cmdArgs}");
        }
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SSPen-Updater/1.0 (Windows NT)");
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }
}
