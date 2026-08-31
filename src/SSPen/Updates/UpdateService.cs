using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows.Threading;

namespace SSPen.Updates;

/// <summary>
/// 업데이트 확인, 다운로드 및 무음 설치를 총괄하는 서비스.
/// </summary>
public sealed class UpdateService
{
    private const string DefaultApiUrl = "https://api.github.com/repos/getCurrentThread/sspen/releases/latest";
    private readonly Dispatcher _dispatcher;
    private readonly Action _exitApp;
    private readonly string _apiUrl;
    private readonly HttpClient _httpClient;

    public UpdateService(Dispatcher dispatcher, Action exitApp, string apiUrl = DefaultApiUrl, HttpClient? httpClient = null)
    {
        _dispatcher = dispatcher;
        _exitApp = exitApp;
        _apiUrl = apiUrl;
        _httpClient = httpClient ?? CreateDefaultHttpClient();
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

                using (var response = _httpClient.GetAsync(info.InstallerDownloadUrl, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
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
                    LaunchSilentInstallerAndExit(installerPath);
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
    /// </summary>
    public void LaunchSilentInstallerAndExit(string installerPath)
    {
        try
        {
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe) || !File.Exists(currentExe))
            {
                currentExe = Assembly.GetEntryAssembly()?.Location ?? Path.Combine(AppContext.BaseDirectory, "SSPen.exe");
            }

            // Inno Setup 스위치:
            // /VERYSILENT : 진행 대화상자 없이 완전 백그라운드 설치
            // /SUPPRESSMSGBOXES : 메시지 박스 억제
            // /NORESTART : 시스템 재부팅 억제
            // /CLOSEAPPLICATIONS : 충돌 프로그램 자동 닫기 시도
            // /FORCECLOSEAPPLICATIONS : 강제 닫기
            //
            // 체이닝: start /wait 로 설치 완료를 기다린 후, 설치된 새 버전의 실행 파일을 시작
            var cmdArgs = $"/c \"start /wait \"\" \"{installerPath}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS & start \"\" \"{currentExe}\"\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = cmdArgs,
                CreateNoWindow = true,
                UseShellExecute = false,
            };

            Process.Start(startInfo);
        }
        catch
        {
            // 프로세스 시작 실패 시 일반 실행 시도
            try
            {
                Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
            }
            catch
            {
                // 실패 시 무시
            }
        }

        // 현재 앱 종료
        _exitApp();
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SSPen-Updater/1.0 (Windows NT)");
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }
}
