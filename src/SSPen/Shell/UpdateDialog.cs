using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SSPen.Updates;

namespace SSPen.Shell;

/// <summary>
/// 새 버전 알림 및 무음 자동 업데이트 진행 대화상자.
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
            Text = $"{Strings.UpdateCurrentVersionLabel} v{curVer}   →   {Strings.UpdateLatestVersionLabel} {info.TagName}",
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
            Text = string.IsNullOrWhiteSpace(info.ReleaseNotes) ? "(릴리즈 설명이 없습니다.)" : info.ReleaseNotes,
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
                    _progressBar.Value = p * 100.0;
                    _statusText.Text = $"{Strings.UpdateDownloading} ({(int)(p * 100)}%)";
                    if (p >= 1.0)
                    {
                        _statusText.Text = Strings.UpdateInstalling;
                    }
                },
                onCompleted: ex =>
                {
                    if (ex is not null)
                    {
                        _isUpdating = false;
                        _updateButton.IsEnabled = true;
                        _laterButton.IsEnabled = true;
                        progressPanel.Visibility = Visibility.Collapsed;

                        var res = MessageBox.Show(
                            this,
                            $"{Strings.UpdateFailedMessage}{ex.Message}",
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

    private void OpenWebReleasePage()
    {
        try
        {
            var url = string.IsNullOrEmpty(_info.HtmlUrl)
                ? "https://github.com/getCurrentThread/sspen/releases"
                : _info.HtmlUrl;

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // 브라우저 실행 실패 무시
        }
    }
}
