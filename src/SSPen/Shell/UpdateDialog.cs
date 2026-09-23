using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SSPen.Diagnostics;
using SSPen.Updates;

namespace SSPen.Shell;

/// <summary>
/// 새 버전 알림 및 무음 자동 업데이트 진행 대화상자. 보이는 문장은 <see cref="Strings"/>가, 진행 표시 판정은
/// <see cref="UpdateProgressText"/>가 소유한다 (77단계, C-5) — 창은 조립하지 않고 대입만 한다.
/// </summary>
public sealed class UpdateDialog : Window
{
    private readonly UpdateReleaseInfo _info;
    private readonly UpdateService _updateService;
    private readonly ProgressBar _progressBar;
    private readonly TextBlock _statusText;
    private readonly Button _updateButton;
    private readonly Button _webButton;
    private readonly Button _laterButton;
    private bool _isUpdating;

    // 103단계: 닫기가 확정됐다 — 그 뒤에 도착하는 진행·완료 콜백(취소 결과, 취소 전에 게시된 진행률)은 닫힌 창을 건드리지 않는다.
    private bool _closed;

    public UpdateDialog(UpdateReleaseInfo info, UpdateService updateService)
    {
        _info = info;
        _updateService = updateService;

        Title = Strings.UpdateTitle;
        Width = 460;
        Height = 400;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        Background = Brushes.White;

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Version info
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Notes
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Progress
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

        // 1. Header
        var header = new TextBlock
        {
            Text = Strings.UpdateAvailable,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // 2. Version Info
        var curVer = UpdateService.CurrentVersion;
        var versionPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var verText = new TextBlock
        {
            Text = Strings.UpdateVersionLine(curVer, info.TagName),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0, 102, 204)),
        };
        versionPanel.Children.Add(verText);
        Grid.SetRow(versionPanel, 1);
        root.Children.Add(versionPanel);

        // 3. Release Notes
        var notesPanel = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        notesPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        notesPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var notesLabel = new TextBlock
        {
            Text = Strings.UpdateReleaseNotesLabel,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(notesLabel, 0);
        notesPanel.Children.Add(notesLabel);

        var notesBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(info.ReleaseNotes) ? Strings.UpdateReleaseNotesEmpty : info.ReleaseNotes,
            IsReadOnly = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.Wrap,
            Background = new SolidColorBrush(Color.FromRgb(248, 249, 250)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 224, 230)),
            Padding = new Thickness(8),
            FontSize = 12,
        };
        Grid.SetRow(notesBox, 1);
        notesPanel.Children.Add(notesBox);

        Grid.SetRow(notesPanel, 2);
        root.Children.Add(notesPanel);

        // 4. Progress Area
        var progressPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed };
        _statusText = new TextBlock
        {
            Text = Strings.UpdateDownloading,
            FontSize = 12,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4),
        };
        _progressBar = new ProgressBar
        {
            Height = 16,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
        };
        progressPanel.Children.Add(_statusText);
        progressPanel.Children.Add(_progressBar);
        Grid.SetRow(progressPanel, 3);
        root.Children.Add(progressPanel);

        // 5. Buttons
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        _updateButton = new Button
        {
            Content = Strings.UpdateNow,
            Width = 110,
            Height = 28,
            Margin = new Thickness(6, 0, 0, 0),
            FontWeight = FontWeights.Bold,
        };

        _webButton = new Button
        {
            Content = Strings.UpdateOpenWebPage,
            Width = 95,
            Height = 28,
            Margin = new Thickness(6, 0, 0, 0),
        };

        _laterButton = new Button
        {
            Content = Strings.UpdateLater,
            Width = 75,
            Height = 28,
            Margin = new Thickness(6, 0, 0, 0),
            IsCancel = true,
        };

        _updateButton.Click += (_, _) =>
        {
            if (_isUpdating) return;

            if (string.IsNullOrEmpty(info.InstallerDownloadUrl))
            {
                OpenWebReleasePage();
                Close();
                return;
            }

            _isUpdating = true;
            _updateButton.IsEnabled = false;
            _laterButton.IsEnabled = false;
            progressPanel.Visibility = Visibility.Visible;

            _updateService.DownloadAndInstallSilently(
                info,
                onProgress: p =>
                {
                    if (_closed)
                    {
                        return;
                    }
                    _progressBar.Value = p * 100.0;
                    _statusText.Text = UpdateProgressText.For(p);
                },
                onCompleted: ex =>
                {
                    // 닫힌 뒤의 결과는 이 창이 청한 취소다(OperationCanceledException) — 오류 상자를 닫힌 창 위에 띄우지 않는다.
                    if (_closed)
                    {
                        return;
                    }
                    if (ex is not null)
                    {
                        _isUpdating = false;
                        _updateButton.IsEnabled = true;
                        _laterButton.IsEnabled = true;
                        progressPanel.Visibility = Visibility.Collapsed;

                        var res = MessageBox.Show(
                            this,
                            Strings.UpdateFailedDetail(ex.Message),
                            Strings.UpdateFailedTitle,
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Error);

                        if (res == MessageBoxResult.Yes)
                        {
                            OpenWebReleasePage();
                            Close();
                        }
                    }
                }
            );
        };

        _webButton.Click += (_, _) => OpenWebReleasePage();
        _laterButton.Click += (_, _) => Close();

        buttonRow.Children.Add(_updateButton);
        buttonRow.Children.Add(_webButton);
        buttonRow.Children.Add(_laterButton);

        Grid.SetRow(buttonRow, 4);
        root.Children.Add(buttonRow);

        Content = root;
    }

    /// <summary>
    /// 다운로드 중에 닫으면 다운로드를 취소하고 닫는다 (103단계, FINAL-REVIEW-UPDATE-CANCEL). 98단계는 이중 다운로드를 막으려고 여기서
    /// 닫기를 거부했지만, 본문 읽기는 <c>HttpClient.Timeout</c>(헤더까지만)이 지켜 주지 않아 네트워크가 멈추면 Topmost·NoResize 창을
    /// 닫을 길이 트레이 종료뿐이었다. 이제 닫기는 <see cref="UpdateService.CancelDownload"/>를 부르고 그대로 닫힌다 — 멈춘 Read는
    /// 서비스가 스트림을 닫아 깨우고, 받던 파일은 지운다. 이중 다운로드 방지는 서비스의 <see cref="UpdateService.IsDownloading"/>(취소된
    /// 결과가 전달될 때까지 참)을 확인 흐름이 읽는 쪽이 맡는다. 앱 종료(<c>Application.Shutdown</c> — 설치 체인의 종료 포함)도 여기를
    /// 지나지만, 그때는 완료가 이미 전달돼 진행 중인 다운로드가 없으므로 취소 요청은 아무것도 하지 않는다.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
        {
            return;
        }

        _closed = true;
        if (_isUpdating)
        {
            _updateService.CancelDownload();
        }
    }

    private void OpenWebReleasePage()
    {
        try
        {
            var url = string.IsNullOrEmpty(_info.HtmlUrl)
                ? UpdateService.ReleasesPageUrl
                : _info.HtmlUrl;

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (UpdateInstallPlan.IsLaunchFailure(ex))
        {
            // 브라우저 실행 실패는 무시하되 로그는 남긴다 (77단계, A1-8).
            Log.Warn($"릴리스 페이지 열기 실패: {ex.Message}");
        }
    }
}
