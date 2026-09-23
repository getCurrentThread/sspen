using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Threading;
using SSPen.Updates;
using Xunit;
using static SSPen.Tests.StaThread;

namespace SSPen.Tests;

/// <summary>
/// <see cref="UpdateService"/>의 증인 (77단계, A1-8·A9-7) — 앱의 유일한 네트워크 코드에 처음 세우는 헤드리스 테스트다.
/// 네트워크는 실제로 부르지 않는다: <see cref="SyncStubHandler"/>가 동기 <c>Send</c>만 구현하고 <c>SendAsync</c>는 던지므로,
/// 확인·다운로드 두 경로가 모두 동기 <see cref="HttpClient.Send(HttpRequestMessage, HttpCompletionOption)"/>로만 지나간다는 것이
/// 그 자체로 증인이 된다(업데이트 계층도 no-async 규칙). 설치 실행은 <c>launchInstaller</c> 이음매로 기록만 한다 — cmd.exe도 앱 종료도 없다.
/// 작업은 스레드풀에서 돌고 결과는 디스패처로 돌아오므로 RunSta 안에서 <see cref="DispatcherPump.Drain"/>을 기한까지 반복한다.
/// </summary>
public class UpdateServiceTests
{
    private const string ApiUrl = "https://example.invalid/repos/o/r/releases/latest";
    private const string DownloadUrl = "https://example.invalid/download/SSPen-Setup.exe";

    [Fact]
    public void CheckForUpdates_Non2xx_ReportsStatusCodeInErrorMessage()
    {
        RunSta(() =>
        {
            var handler = new SyncStubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                ReasonPhrase = "Service Unavailable",
            });
            using var client = new HttpClient(handler);
            var dispatcher = Dispatcher.CurrentDispatcher;
            var service = new UpdateService(dispatcher, exitApp: () => { }, apiUrl: ApiUrl, httpClient: client);
            UpdateCheckResult? result = null;

            service.CheckForUpdates(r => result = r);
            PumpUntil(dispatcher, () => result is not null);

            Assert.False(result!.Success);
            Assert.Equal("서버 응답 오류 (503 Service Unavailable)", result.ErrorMessage);
            Assert.Equal([$"GET {ApiUrl}"], handler.Requests);
        });
    }

    /// <summary>본문은 <see cref="UpdateCheckerCore.ParseReleaseJson"/>으로 판정된다. SendAsync가 불렸다면 예외가 실패 결과로 바뀌어 이 단언이 깨진다.</summary>
    [Fact]
    public void CheckForUpdates_ValidJson_ParsesThroughCore()
    {
        RunSta(() =>
        {
            const string json = """
            {
              "tag_name": "v99.0.0",
              "name": "SS Pen v99.0.0",
              "body": "notes",
              "html_url": "https://example.invalid/releases/tag/v99.0.0",
              "assets": [
                { "name": "SSPen-Setup-v99.0.0.exe", "browser_download_url": "https://example.invalid/download/SSPen-Setup-v99.0.0.exe" }
              ]
            }
            """;
            var handler = new SyncStubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
            using var client = new HttpClient(handler);
            var dispatcher = Dispatcher.CurrentDispatcher;
            var service = new UpdateService(dispatcher, exitApp: () => { }, apiUrl: ApiUrl, httpClient: client);
            UpdateCheckResult? result = null;

            service.CheckForUpdates(r => result = r);
            PumpUntil(dispatcher, () => result is not null);

            Assert.True(result!.Success, result.ErrorMessage);
            Assert.True(result.HasUpdate);
            Assert.Equal("v99.0.0", result.ReleaseInfo!.TagName);
            Assert.Equal("https://example.invalid/download/SSPen-Setup-v99.0.0.exe", result.ReleaseInfo.InstallerDownloadUrl);
        });
    }

    /// <summary>
    /// 디스패처 순서 계약: 진행률(단조 증가, 마지막 1.0) → <c>onCompleted(null)</c> → 설치 실행 이음매. 경로는
    /// <c>%TEMP%\SSPen-Update\SSPen-Setup-{TagName}.exe</c>이고 내용은 받은 바이트 그대로다. 이음매가 기본 설치 체인을 대신하므로
    /// <c>exitApp</c>은 불리지 않는다.
    /// </summary>
    [Fact]
    public void DownloadAndInstallSilently_UsesSyncSend_WritesFile_ReportsProgress_ThenCompletesThenLaunchesViaSeam()
    {
        RunSta(() =>
        {
            var body = new byte[200_000];
            for (int i = 0; i < body.Length; i++)
            {
                body[i] = (byte)(i % 251);
            }
            var handler = new SyncStubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
            using var client = new HttpClient(handler);
            var dispatcher = Dispatcher.CurrentDispatcher;
            var events = new List<string>();
            var progress = new List<double>();
            string? launched = null;
            var service = new UpdateService(
                dispatcher, exitApp: () => events.Add("exit"), apiUrl: ApiUrl, httpClient: client,
                launchInstaller: path =>
                {
                    events.Add("launch");
                    launched = path;
                });
            var tag = UniqueTag();
            var expectedPath = Path.Combine(Path.GetTempPath(), "SSPen-Update", $"SSPen-Setup-{tag}.exe");

            try
            {
                service.DownloadAndInstallSilently(
                    Release(tag, DownloadUrl),
                    onProgress: p =>
                    {
                        events.Add("progress");
                        progress.Add(p);
                    },
                    onCompleted: ex => events.Add(ex is null ? "completed" : "failed:" + ex.Message));
                PumpUntil(dispatcher, () => launched is not null);

                Assert.Equal(expectedPath, launched);
                Assert.Equal(body, File.ReadAllBytes(expectedPath));
                Assert.Equal([$"GET {DownloadUrl}"], handler.Requests);

                Assert.NotEmpty(progress);
                Assert.Equal(1.0, progress[^1]);
                Assert.Equal(progress.OrderBy(p => p), progress);

                var completedAt = events.IndexOf("completed");
                Assert.True(completedAt > 0, string.Join(",", events));
                Assert.All(events.Take(completedAt), e => Assert.Equal("progress", e));
                Assert.Equal(["completed", "launch"], events.Skip(completedAt));
                Assert.DoesNotContain("exit", events);
            }
            finally
            {
                File.Delete(expectedPath);
            }
        });
    }

    [Fact]
    public void DownloadAndInstallSilently_HttpError_CompletesWithException_AndDoesNotLaunch()
    {
        RunSta(() =>
        {
            var handler = new SyncStubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
            using var client = new HttpClient(handler);
            var dispatcher = Dispatcher.CurrentDispatcher;
            var events = new List<string>();
            Exception? failure = null;
            var service = new UpdateService(
                dispatcher, exitApp: () => events.Add("exit"), apiUrl: ApiUrl, httpClient: client,
                launchInstaller: _ => events.Add("launch"));
            var tag = UniqueTag();
            var expectedPath = Path.Combine(Path.GetTempPath(), "SSPen-Update", $"SSPen-Setup-{tag}.exe");

            try
            {
                service.DownloadAndInstallSilently(
                    Release(tag, DownloadUrl),
                    onProgress: _ => events.Add("progress"),
                    onCompleted: ex =>
                    {
                        events.Add("completed");
                        failure = ex;
                    });
                PumpUntil(dispatcher, () => events.Contains("completed"));
                DispatcherPump.Drain(dispatcher);

                Assert.IsType<HttpRequestException>(failure);
                Assert.Equal(["completed"], events);
                Assert.False(File.Exists(expectedPath));
            }
            finally
            {
                File.Delete(expectedPath);
            }
        });
    }

    /// <summary>URL이 없으면 스레드풀·네트워크 없이 호출 안에서 곧바로 실패를 알린다(대화상자가 웹 페이지 폴백을 띄우는 경로).</summary>
    [Fact]
    public void DownloadAndInstallSilently_NoUrl_CompletesWithErrorSynchronously()
    {
        RunSta(() =>
        {
            var handler = new SyncStubHandler(_ => throw new InvalidOperationException("요청이 나가면 안 된다"));
            using var client = new HttpClient(handler);
            var launched = false;
            var service = new UpdateService(
                Dispatcher.CurrentDispatcher, exitApp: () => { }, apiUrl: ApiUrl, httpClient: client,
                launchInstaller: _ => launched = true);
            Exception? failure = null;

            service.DownloadAndInstallSilently(Release(UniqueTag(), null), onProgress: _ => { }, onCompleted: ex => failure = ex);

            Assert.IsType<InvalidOperationException>(failure);
            Assert.False(launched);
            Assert.Empty(handler.Requests);
        });
    }

    private static UpdateReleaseInfo Release(string tag, string? downloadUrl) =>
        new(tag, new Version(99, 0, 0), "SS Pen", "notes", "https://example.invalid/release", downloadUrl);

    /// <summary>실제 %TEMP%\SSPen-Update에 쓰므로 실행마다 겹치지 않는 태그를 쓰고, 테스트가 끝나면 그 파일만 지운다.</summary>
    private static string UniqueTag() => $"v0.0.0-test-{Guid.NewGuid():N}";

    /// <summary>디스패처를 비우며 조건을 기다린다 — 5초 기한(스레드풀 작업이 끝나 BeginInvoke가 도착할 때까지).</summary>
    private static void PumpUntil(Dispatcher dispatcher, Func<bool> done)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            DispatcherPump.Drain(dispatcher);
            if (done())
            {
                return;
            }
            Assert.True(DateTime.UtcNow < deadline, "UpdateService 결과가 5초 안에 디스패처로 돌아오지 않았다.");
            Thread.Sleep(5);
        }
    }

    /// <summary>200 OK에 <paramref name="body"/>를 스트림 본문으로 싣는다. 길이를 알리면 진행률도 게시된다.</summary>
    private static HttpResponseMessage OkWithStream(Stream body, long declaredLength)
    {
        var content = new StreamContent(body);
        content.Headers.ContentLength = declaredLength;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static string InstallerPath(string tag) => Path.Combine(Path.GetTempPath(), "SSPen-Update", $"SSPen-Setup-{tag}.exe");

    /// <summary>
    /// 정리용 삭제 — 수정 전(빨강)에는 멈춘 작업 스레드가 파일을 쥐고 있어 공유 위반이, 작업 스레드의 삭제와 겹치면 삭제 대기 중인
    /// 파일의 접근 거부가 날 수 있다. 그때는 이름이 겹치지 않는 파일 하나가 남을 뿐이라 테스트 결과를 가리지 않도록 삼킨다.
    /// </summary>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // ---- 103단계 (FINAL-REVIEW-UPDATE-CANCEL): 다운로드 취소 ----

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 103단계 회귀 증인: 본문 읽기가 네트워크에서 멈추면(<c>HttpClient.Timeout</c>은 헤더까지만 걸린다) 예전에는 작업을 끝낼 길이 없었다.
    /// <see cref="UpdateService.CancelDownload"/>가 진행 중 스트림을 닫아 멈춘 Read를 몇 초 안에 깨우고, 결과는 실패가 아니라 취소다 —
    /// 완료 콜백은 <see cref="OperationCanceledException"/>으로 한 번, 설치 실행·앱 종료 없음, 부분 파일 없음, 진행 중 상태 해제.
    /// </summary>
    [Fact]
    public void DownloadAndInstallSilently_CancelWhileBodyStalls_EndsPromptly_WithoutLaunchOrPartialFile()
    {
        RunSta(() =>
        {
            using var body = new FakeBodyStream(length: 4096, stallAfterBody: true);
            var handler = new SyncStubHandler(_ => OkWithStream(body, declaredLength: 1_000_000));
            using var client = new HttpClient(handler);
            var dispatcher = Dispatcher.CurrentDispatcher;
            var events = new List<string>();
            var completions = new List<Exception?>();
            var service = new UpdateService(
                dispatcher, exitApp: () => events.Add("exit"), apiUrl: ApiUrl, httpClient: client,
                launchInstaller: _ => events.Add("launch"));
            var tag = UniqueTag();
            var expectedPath = InstallerPath(tag);

            try
            {
                service.DownloadAndInstallSilently(Release(tag, DownloadUrl), onProgress: _ => { }, onCompleted: completions.Add);
                Assert.True(body.WaitUntilStalled(Deadline), "전제: 작업 스레드가 멈춘 본문 읽기에 들어서지 않았다.");
                Assert.True(File.Exists(expectedPath), "전제: 부분 파일이 생겼다.");
                Assert.True(service.IsDownloading);

                service.CancelDownload();

                Assert.True(body.WaitUntilDisposed(Deadline), "취소가 멈춘 본문 스트림을 닫아 Read를 깨우지 않았다.");
                PumpUntil(dispatcher, () => completions.Count > 0);
                DispatcherPump.Drain(dispatcher);

                Assert.IsType<OperationCanceledException>(Assert.Single(completions));
                Assert.Empty(events);
                Assert.False(File.Exists(expectedPath));
                Assert.False(service.IsDownloading);
            }
            finally
            {
                TryDelete(expectedPath);
            }
        });
    }

    /// <summary>
    /// 진행 중 상태는 작업 스레드가 끝난 뒤에도 취소 결과가 디스패처로 <b>전달될 때까지</b> 참이다 — 루트의 확인 흐름이 이 값을 읽어,
    /// 닫힌 대화상자의 작업이 정리되는 그 짧은 구간에 새 대화상자가 같은 설치 파일 경로로 두 번째 다운로드를 시작하지 못하게 한다
    /// (98단계 이중 다운로드 방지의 새 설계). 작업 스레드는 부분 파일을 지운 뒤에 결과를 게시하므로, 파일이 사라졌다는 것은 작업이 끝났다는 뜻이다.
    /// </summary>
    [Fact]
    public void IsDownloading_AfterCancel_StaysTrueUntilOutcomeDelivered()
    {
        RunSta(() =>
        {
            using var body = new FakeBodyStream(length: 4096, stallAfterBody: true);
            var handler = new SyncStubHandler(_ => OkWithStream(body, declaredLength: 1_000_000));
            using var client = new HttpClient(handler);
            var dispatcher = Dispatcher.CurrentDispatcher;
            var completions = new List<Exception?>();
            var service = new UpdateService(
                dispatcher, exitApp: () => { }, apiUrl: ApiUrl, httpClient: client, launchInstaller: _ => { });
            var tag = UniqueTag();
            var expectedPath = InstallerPath(tag);

            try
            {
                service.DownloadAndInstallSilently(Release(tag, DownloadUrl), onProgress: _ => { }, onCompleted: completions.Add);
                Assert.True(body.WaitUntilStalled(Deadline), "전제: 작업 스레드가 멈춘 본문 읽기에 들어서지 않았다.");

                service.CancelDownload();
                WaitUntil(() => !File.Exists(expectedPath), "취소된 작업 스레드가 부분 파일을 지우지 않았다.");

                Assert.True(service.IsDownloading); // 작업은 끝났지만 결과가 아직 디스패처에 전달되지 않았다.
                Assert.Empty(completions);

                PumpUntil(dispatcher, () => completions.Count > 0);

                Assert.False(service.IsDownloading);
            }
            finally
            {
                TryDelete(expectedPath);
            }
        });
    }

    /// <summary>
    /// 경합 증인: 작업 스레드가 본문을 다 받고 완료를 게시한 뒤, 그 완료가 UI 스레드에서 돌기 전에 대화상자가 닫혀 취소가 요청됐다.
    /// 취소가 이긴다 — 설치 체인을 실행하지 않고(닫은 창 뒤로 앱이 설치·종료되면 안 된다), 다 받은 파일도 지우고, 취소로 한 번 알린다.
    /// 본문 스트림이 닫혔다는 것은 작업 스레드가 파일을 닫고 응답까지 해제했다는 뜻이다(완료 게시 직전·직후).
    /// </summary>
    [Fact]
    public void DownloadAndInstallSilently_CancelAfterBodyCompletes_BeforeDelivery_DoesNotLaunch()
    {
        RunSta(() =>
        {
            using var body = new FakeBodyStream(length: 50_000, stallAfterBody: false);
            var handler = new SyncStubHandler(_ => OkWithStream(body, declaredLength: 50_000));
            using var client = new HttpClient(handler);
            var dispatcher = Dispatcher.CurrentDispatcher;
            var events = new List<string>();
            var completions = new List<Exception?>();
            var service = new UpdateService(
                dispatcher, exitApp: () => events.Add("exit"), apiUrl: ApiUrl, httpClient: client,
                launchInstaller: _ => events.Add("launch"));
            var tag = UniqueTag();
            var expectedPath = InstallerPath(tag);

            try
            {
                service.DownloadAndInstallSilently(Release(tag, DownloadUrl), onProgress: _ => { }, onCompleted: completions.Add);
                Assert.True(body.WaitUntilDisposed(Deadline), "전제: 작업 스레드가 본문을 끝까지 받고 응답을 닫지 않았다.");
                Assert.Empty(completions); // 전제: 완료는 아직 UI 스레드에서 돌지 않았다(펌프 전).

                service.CancelDownload();
                PumpUntil(dispatcher, () => completions.Count > 0);
                DispatcherPump.Drain(dispatcher);

                Assert.IsType<OperationCanceledException>(Assert.Single(completions));
                Assert.Empty(events);
                Assert.False(File.Exists(expectedPath));
                Assert.False(service.IsDownloading);
            }
            finally
            {
                TryDelete(expectedPath);
            }
        });
    }

    /// <summary>
    /// 앱 종료 경로의 무해함: 설치 실행 이음매 안에서(프로덕션은 체인을 띄우고 <c>Application.Shutdown</c> → 대화상자 OnClosing →
    /// 취소 요청) 취소가 불려도 이미 전달된 완료에는 아무 영향이 없다 — 설치 체인이 쓸 파일을 지우지 않는다.
    /// </summary>
    [Fact]
    public void CancelDownload_DuringInstallerLaunch_ShutdownPath_KeepsInstallerFile()
    {
        RunSta(() =>
        {
            var handler = new SyncStubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[10_000]) });
            using var client = new HttpClient(handler);
            var dispatcher = Dispatcher.CurrentDispatcher;
            var events = new List<string>();
            var completions = new List<Exception?>();
            UpdateService? service = null;
            service = new UpdateService(
                dispatcher, exitApp: () => events.Add("exit"), apiUrl: ApiUrl, httpClient: client,
                launchInstaller: _ =>
                {
                    events.Add("launch");
                    service!.CancelDownload();
                });
            var tag = UniqueTag();
            var expectedPath = InstallerPath(tag);

            try
            {
                service.DownloadAndInstallSilently(Release(tag, DownloadUrl), onProgress: _ => { }, onCompleted: completions.Add);
                PumpUntil(dispatcher, () => events.Contains("launch"));
                DispatcherPump.Drain(dispatcher);

                Assert.Null(Assert.Single(completions));
                Assert.Equal(["launch"], events);
                Assert.True(File.Exists(expectedPath));
                Assert.False(service.IsDownloading);
            }
            finally
            {
                TryDelete(expectedPath);
            }
        });
    }

    /// <summary>디스패처 없이 작업 스레드의 흔적(파일 등)을 기다린다 — 5초 기한.</summary>
    private static void WaitUntil(Func<bool> done, string failure)
    {
        var deadline = DateTime.UtcNow + Deadline;
        while (!done())
        {
            Assert.True(DateTime.UtcNow < deadline, failure);
            Thread.Sleep(5);
        }
    }
}
