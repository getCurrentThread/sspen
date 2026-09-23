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
/// 네트워크는 실제로 부르지 않는다: <see cref="StubHandler"/>가 동기 <c>Send</c>만 구현하고 <c>SendAsync</c>는 던지므로,
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
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
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
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
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
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
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
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
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
            var handler = new StubHandler(_ => throw new InvalidOperationException("요청이 나가면 안 된다"));
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

    /// <summary>
    /// 동기 <c>Send</c>만 응답하는 가짜 처리기. <c>SendAsync</c>는 추상 멤버라 재정의가 필요하지만 던지기만 한다 —
    /// Task 경로로 들어오면 테스트가 깨지는 것이 목적이다.
    /// </summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly List<string> _requests = [];

        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_requests)
                {
                    return [.. _requests];
                }
            }
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_requests)
            {
                _requests.Add($"{request.Method} {request.RequestUri}");
            }
            return respond(request);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("업데이트 계층은 동기 Send만 쓴다 (77단계, A1-8) — SendAsync 경로에 들어오면 안 된다.");
    }
}
