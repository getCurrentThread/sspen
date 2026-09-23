using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using SSPen.Shell;
using SSPen.Updates;
using Xunit;
using static SSPen.Tests.StaThread;

namespace SSPen.Tests;

/// <summary>
/// 업데이트 대화상자의 닫기 판정 증인. 98단계(FINAL-REVIEW-UPDATE-DOWNLOADING)는 다운로드 중 닫기를 <b>거부</b>했다 — 제목 표시줄
/// X·Alt+F4로 닫히면 루트의 <c>_updateDialog</c>가 비어 다음 확인이 같은 설치 파일 경로로 두 번째 다운로드를 시작할 수 있었기 때문이다.
/// 그런데 본문 읽기가 네트워크에서 멈추면(<c>HttpClient.Timeout</c>은 헤더까지만 걸린다) Topmost·NoResize 창을 닫을 길이 트레이 종료뿐이었다.
/// 103단계(FINAL-REVIEW-UPDATE-CANCEL)가 의미를 '취소 후 닫기'로 바꿨다: 닫으면 서비스에 취소를 요청하고 곧바로 닫힌다. 이중 다운로드
/// 방지는 서비스의 진행 중 상태(<see cref="UpdateService.IsDownloading"/> — 취소된 작업의 결과가 전달될 때까지 참)를 확인 흐름이 읽는
/// 쪽으로 옮겨 갔다(<c>UpdateCheckFlowTests</c>·<c>UpdateServiceTests</c>).
/// 창은 띄우지 않는다(Show 없음): 띄운 적 없는 창의 <c>Close()</c>도 <c>OnClosing</c>을 거치고, 취소되지 않으면 <c>Closed</c>를 올린다.
/// 네트워크는 부르지 않는다 — 가짜 처리기가 본문 도중에 멈추는 스트림(<see cref="FakeBodyStream"/>)을 돌려준다. 디스패처는 펌프하지
/// 않는다: 닫힌 창에 늦게 오는 콜백은 창이 무시해야 하지만, 그것이 깨지면 모달 오류 상자가 떠 테스트가 멈추므로 여기서 돌리지 않는다.
/// </summary>
public class UpdateDialogTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 103단계 회귀 증인 (98단계의 '닫기 거부' 증인을 새 의미로 바꿨다): 본문 읽기가 멈춘 다운로드 중에 닫으면 창이 닫히고,
    /// 그 닫기가 서비스 취소를 불러 멈춘 본문 스트림을 닫는다(멈춘 Read가 깨어난다). 수정 전에는 <c>Closed</c>가 오지 않았다.
    /// </summary>
    [Fact]
    public void Close_WhileDownloading_ClosesAndCancelsDownload() => RunSta(() =>
    {
        using var body = new FakeBodyStream(length: 4096, stallAfterBody: true);
        var (dialog, service, installerPath) = NewDialog(_ =>
        {
            var content = new StreamContent(body);
            content.Headers.ContentLength = 1_000_000;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        var closed = false;
        dialog.Closed += (_, _) => closed = true;

        try
        {
            var updateButton = FindUpdateButton(dialog)!;
            updateButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.False(updateButton.IsEnabled); // 전제: 클릭이 다운로드를 시작했다(진행 중에는 버튼이 꺼진다).
            Assert.True(body.WaitUntilStalled(Deadline), "전제: 다운로드가 멈춘 본문 읽기에 들어서지 않았다.");

            dialog.Close();

            Assert.True(closed);
            Assert.True(body.WaitUntilDisposed(Deadline), "닫기가 다운로드 취소를 요청하지 않았다 — 멈춘 본문 읽기가 깨어나지 않는다.");
            var deadline = DateTime.UtcNow + Deadline;
            while (File.Exists(installerPath))
            {
                Assert.True(DateTime.UtcNow < deadline, "취소된 다운로드가 부분 파일을 지우지 않았다.");
                Thread.Sleep(5);
            }
            Assert.True(service.IsDownloading); // 취소된 결과는 아직 디스패처에 전달되지 않았다(펌프 안 함) — 확인 흐름이 읽는 값.
        }
        finally
        {
            try
            {
                File.Delete(installerPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 수정 전(빨강)에는 멈춘 작업 스레드가 부분 파일을 쥐고 있다 — 이름이 겹치지 않는 파일 하나가 남을 뿐이다.
            }
        }
    });

    /// <summary>대조군: 다운로드를 시작하지 않은 대화상자는 예전처럼 닫히고, 아무것도 취소하지 않는다.</summary>
    [Fact]
    public void Close_NotDownloading_Closes() => RunSta(() =>
    {
        var (dialog, service, _) = NewDialog(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var closed = false;
        dialog.Closed += (_, _) => closed = true;

        dialog.Close();

        Assert.True(closed);
        Assert.False(service.IsDownloading);
    });

    private static (UpdateDialog Dialog, UpdateService Service, string InstallerPath) NewDialog(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var service = new UpdateService(
            Dispatcher.CurrentDispatcher,
            exitApp: () => { },
            apiUrl: "https://example.invalid/latest",
            httpClient: new HttpClient(new SyncStubHandler(respond)),
            launchInstaller: _ => { });
        var tag = $"v0.0.0-test-{Guid.NewGuid():N}";
        var info = new UpdateReleaseInfo(
            tag, new Version(99, 0, 0), "SS Pen", "notes",
            "https://example.invalid/release", "https://example.invalid/download/SSPen-Setup.exe");
        var installerPath = Path.Combine(Path.GetTempPath(), "SSPen-Update", $"SSPen-Setup-{tag}.exe");
        return (new UpdateDialog(info, service), service, installerPath);
    }

    /// <summary>'지금 업데이트' 버튼 — 논리 트리에서 문구(<see cref="Strings.UpdateNow"/>)로 찾는다. 테스트용 접근자를 창에 두지 않는다.</summary>
    private static Button? FindUpdateButton(DependencyObject node)
    {
        if (node is Button { Content: string text } button && text == Strings.UpdateNow)
        {
            return button;
        }
        return LogicalTreeHelper.GetChildren(node)
            .OfType<DependencyObject>()
            .Select(FindUpdateButton)
            .FirstOrDefault(found => found is not null);
    }
}
