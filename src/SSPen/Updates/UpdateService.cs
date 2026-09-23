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

    // 103단계 (FINAL-REVIEW-UPDATE-CANCEL): 진행 중인 다운로드 — 시작에서 세우고, 결과를 디스패처로 전달할 때 내린다.
    private readonly object _downloadGate = new();
    private DownloadRun? _activeDownload;

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
    /// 다운로드도 확인 경로처럼 동기 <see cref="HttpClient.Send(HttpRequestMessage, HttpCompletionOption, CancellationToken)"/>를 스레드풀에서 쓴다 —
    /// Task API를 GetResult로 막던 예전 호출을 걷어 냈다 (77단계, A1-8 — 업데이트 계층도 no-async 규칙).
    /// 디스패처 순서는 진행률 → <c>onCompleted(null)</c> → 설치 실행 이음매다.
    /// <see cref="CancelDownload"/>로 취소되면(103단계) <c>onCompleted</c>는 <see cref="OperationCanceledException"/>으로 한 번 불리고,
    /// 설치 실행 이음매는 불리지 않으며, 받던 파일은 지워진다. 결과를 전달하기 전까지 <see cref="IsDownloading"/>은 참이다.
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

        var run = new DownloadRun();
        lock (_downloadGate)
        {
            _activeDownload = run;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            string? installerPath = null;
            Exception? failure = null;
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "SSPen-Update");
                Directory.CreateDirectory(tempDir);
                installerPath = Path.Combine(tempDir, $"SSPen-Setup-{info.TagName}.exe");

                using var request = new HttpRequestMessage(HttpMethod.Get, info.InstallerDownloadUrl);
                using (var response = _httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead, run.Token))
                {
                    // 본문 읽기에는 HttpClient.Timeout이 걸리지 않는다 — 멈춘 Read를 깨우는 길은 취소의 Dispose뿐이다 (103단계).
                    run.Track(response);
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                    using var source = response.Content.ReadAsStream();
                    run.Track(source);
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
                            _dispatcher.BeginInvoke(() =>
                            {
                                if (!run.CancelRequested)
                                {
                                    onProgress(progress);
                                }
                            });
                        }
                    }
                }
            }
            catch (Exception) when (run.CancelRequested)
            {
                // 취소가 스트림·응답을 닫아 깨운 예외다 — 실패가 아니다. 판정은 아래 전달에서 취소 플래그로 한 번 더 한다.
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            // 닫힌 스트림의 Read는 예외 대신 0(끝)을 돌려줄 수도 있다 — 예외 여부가 아니라 플래그로 부분 파일을 지운다.
            // 파일은 위의 using이 이미 닫았다. 결과 게시 전에 지우므로 다음 다운로드가 같은 경로를 두고 다투지 않는다.
            var discarded = run.CancelRequested && installerPath is not null;
            if (discarded)
            {
                DeleteInstallerFile(installerPath!);
            }

            _dispatcher.BeginInvoke(() => Deliver(run, installerPath, discarded, failure, onCompleted));
        });
    }

    /// <summary>
    /// 다운로드 결과를 UI 스레드에서 전달한다 (103단계). 진행 중 표시를 먼저 내린 뒤 취소 플래그를 읽는다 — 내린 뒤에는
    /// <see cref="CancelDownload"/>가 이 작업에 닿지 못하므로 그 뒤의 판정이 최종이다. 취소가 요청됐으면 작업이 끝까지 받았어도
    /// (완료와 취소의 경합) 설치 체인을 실행하지 않는다: 닫은 대화상자 뒤로 앱이 설치·종료되면 안 된다. 그때 받은 파일은 여기서 지운다
    /// (<paramref name="discarded"/> — 작업 스레드가 이미 지웠으면 다시 지우지 않는다).
    /// </summary>
    private void Deliver(DownloadRun run, string? installerPath, bool discarded, Exception? failure, Action<Exception?> onCompleted)
    {
        lock (_downloadGate)
        {
            if (ReferenceEquals(_activeDownload, run))
            {
                _activeDownload = null;
            }
        }

        if (run.CancelRequested)
        {
            if (!discarded && installerPath is not null)
            {
                DeleteInstallerFile(installerPath);
            }
            Log.Info("업데이트 다운로드 취소됨 — 받던 설치 파일을 지우고 설치를 실행하지 않는다");
            onCompleted(new OperationCanceledException());
            return;
        }

        onCompleted(failure);
        if (failure is null)
        {
            _launchInstaller(installerPath!);
        }
    }

    /// <summary>
    /// 다운로드가 진행 중인지 — 시작부터 결과가 디스패처로 전달될 때까지 참이다 (103단계). 취소된 작업도 결과가 전달될 때까지는 참이라,
    /// 확인 흐름(<c>UpdateCheckFlow</c>)이 이 값을 읽어 닫힌 대화상자의 작업이 정리되는 동안 새 대화상자를 열지 않는다 —
    /// 같은 설치 파일 경로로 두 번째 다운로드가 시작되지 않는다(98단계의 이중 다운로드 방지를 대화상자 '열림' 대신 이 값이 맡는다).
    /// </summary>
    public bool IsDownloading
    {
        get
        {
            lock (_downloadGate)
            {
                return _activeDownload is not null;
            }
        }
    }

    /// <summary>
    /// 진행 중인 다운로드를 취소한다 (103단계, FINAL-REVIEW-UPDATE-CANCEL) — 업데이트 대화상자가 다운로드 중에 닫힐 때 부른다.
    /// 헤더 대기는 취소 토큰으로, 멈춘 본문 Read는 추적 중인 응답·본문 스트림을 닫아 깨운다. UI 스레드에서 부른다(결과 전달과 같은 스레드여야
    /// 전달 직후의 취소가 낡은 실행을 건드리지 않는다). 여러 번 불러도 된다.
    /// 진행 중인 다운로드가 없으면(결과가 이미 전달됨 — 설치 체인이 띄운 <c>Application.Shutdown</c>이 대화상자를 닫는 경로 포함)
    /// 아무것도 하지 않는다 — 설치 체인이 쓸 파일을 건드리지 않는다.
    /// </summary>
    public void CancelDownload()
    {
        DownloadRun? run;
        lock (_downloadGate)
        {
            run = _activeDownload;
        }
        if (run is not null && run.Cancel())
        {
            Log.Info("업데이트 다운로드 취소 요청 — 진행 중인 응답과 본문 스트림을 닫는다");
        }
    }

    /// <summary>취소·경합으로 버리는 설치 파일을 지운다. 지우지 못해도 다음 다운로드의 <c>File.Create</c>가 덮어쓰므로 로그만 남긴다.</summary>
    private static void DeleteInstallerFile(string installerPath)
    {
        try
        {
            File.Delete(installerPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"취소된 업데이트 설치 파일 삭제 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 다운로드 한 번의 취소 상태 (103단계). 작업 스레드가 응답·본문 스트림을 <see cref="Track"/>으로 맡기고, UI 스레드의
    /// <see cref="Cancel"/>이 잠금 아래에서 플래그를 세운 뒤 잠금 밖에서 토큰을 취소하고 맡은 것을 닫는다(동기 Read에는 토큰이 닿지 않는다).
    /// 취소 뒤에 맡겨지는 자원은 곧바로 닫혀, 그다음 읽기가 예외로 끝난다 — 추적 사이 틈에 온 취소도 놓치지 않는다.
    /// 토큰 원천은 Dispose하지 않는다: 타이머·대기 핸들을 쓰지 않아 해제할 것이 없고, 닫은 뒤의 Cancel 경합만 생긴다.
    /// </summary>
    private sealed class DownloadRun
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _cancellation = new();
        private readonly List<IDisposable> _tracked = [];
        private bool _cancelRequested;

        public CancellationToken Token => _cancellation.Token;

        public bool CancelRequested
        {
            get
            {
                lock (_gate)
                {
                    return _cancelRequested;
                }
            }
        }

        public void Track(IDisposable resource)
        {
            lock (_gate)
            {
                if (!_cancelRequested)
                {
                    _tracked.Add(resource);
                    return;
                }
            }
            resource.Dispose();
        }

        /// <returns>이번 호출이 취소를 처음 요청했으면 참.</returns>
        public bool Cancel()
        {
            IDisposable[] tracked;
            lock (_gate)
            {
                if (_cancelRequested)
                {
                    return false;
                }
                _cancelRequested = true;
                tracked = [.. _tracked];
                _tracked.Clear();
            }

            _cancellation.Cancel();
            // 본문 스트림(나중에 맡긴 것)부터 닫는다 — 멈춘 Read를 먼저 깨운다.
            for (int i = tracked.Length - 1; i >= 0; i--)
            {
                tracked[i].Dispose();
            }
            return true;
        }
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
