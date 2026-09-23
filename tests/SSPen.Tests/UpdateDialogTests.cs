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
/// 업데이트 대화상자의 닫기 판정 증인 (98단계, FINAL-REVIEW-UPDATE-DOWNLOADING). 86단계(C-4)의 단일 인스턴스 판정은
/// 루트의 <c>_updateDialog</c>가 비어 있는지만 본다 — '나중에'는 다운로드 중 비활성이지만 제목 표시줄 X·Alt+F4로는 닫혔고,
/// 그러면 다음 자동·수동 확인이 새 대화상자를 열어 같은 설치 파일 경로로 두 번째 다운로드를 시작할 수 있었다.
/// 다운로드 중 닫기를 거부하면 '다운로드 중 ⇒ 대화상자 열림'이 성립해 기존 판정이 그대로 옳아진다.
/// 창은 띄우지 않는다(Show 없음): 띄운 적 없는 창의 <c>Close()</c>도 <c>OnClosing</c>을 거치고, 취소되지 않으면 <c>Closed</c>를 올린다.
/// 네트워크는 부르지 않는다 — 가짜 처리기는 곧바로 404로 답하고(파일을 만들지 않는다), 그 완료 콜백은 이 STA 디스패처로
/// 게시되지만 펌프하지 않으므로 대화상자 쪽에서 다운로드는 끝까지 '진행 중'이다(모달 오류 상자도 뜨지 않는다).
/// </summary>
public class UpdateDialogTests
{
    [Fact]
    public void Close_WhileDownloading_IsCancelled() => RunSta(() =>
    {
        var dialog = NewDialog();
        var closed = false;
        dialog.Closed += (_, _) => closed = true;

        var updateButton = FindUpdateButton(dialog)!;
        updateButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.False(updateButton.IsEnabled); // 전제: 클릭이 다운로드를 시작했다(진행 중에는 버튼이 꺼진다).

        dialog.Close();

        Assert.False(closed);
    });

    /// <summary>대조군: 다운로드를 시작하지 않은 대화상자는 예전처럼 닫힌다 — 취소는 '다운로드 중'에만 걸린다.</summary>
    [Fact]
    public void Close_NotDownloading_Closes() => RunSta(() =>
    {
        var dialog = NewDialog();
        var closed = false;
        dialog.Closed += (_, _) => closed = true;

        dialog.Close();

        Assert.True(closed);
    });

    private static UpdateDialog NewDialog()
    {
        var service = new UpdateService(
            Dispatcher.CurrentDispatcher,
            exitApp: () => { },
            apiUrl: "https://example.invalid/latest",
            httpClient: new HttpClient(new NotFoundHandler()),
            launchInstaller: _ => { });
        var info = new UpdateReleaseInfo(
            $"v0.0.0-test-{Guid.NewGuid():N}", new Version(99, 0, 0), "SS Pen", "notes",
            "https://example.invalid/release", "https://example.invalid/download/SSPen-Setup.exe");
        return new UpdateDialog(info, service);
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

    /// <summary>곧바로 404로 답하는 동기 처리기 — 설치 파일을 만들기 전에 실패한다. SendAsync는 쓰지 않는다(업데이트 계층은 동기 Send만).</summary>
    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) =>
            new(HttpStatusCode.NotFound);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("업데이트 계층은 동기 Send만 쓴다 — SendAsync 경로에 들어오면 안 된다.");
    }
}
