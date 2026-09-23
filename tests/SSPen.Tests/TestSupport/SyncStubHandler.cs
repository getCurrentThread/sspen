using System.Net.Http;

namespace SSPen.Tests;

/// <summary>
/// 동기 <c>Send</c>만 응답하는 가짜 HTTP 처리기 (77단계 <c>UpdateServiceTests</c>의 private 처리기를 103단계에 승격 —
/// <c>UpdateDialogTests</c>도 같은 가짜로 다운로드를 멈춘다). <c>SendAsync</c>는 추상 멤버라 재정의가 필요하지만 던지기만 한다 —
/// 업데이트 계층은 동기 Send만 쓰므로(no-async 규칙) Task 경로로 들어오면 테스트가 깨지는 것이 목적이다.
/// 받은 요청은 "METHOD URI" 문자열로 기록한다(스레드풀 작업이 쓰므로 잠금 아래).
/// </summary>
internal sealed class SyncStubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
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
