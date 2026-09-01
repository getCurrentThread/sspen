using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SSPen.Annotation;
using SSPen.Interop;
using SSPen.Settings;

namespace SSPen.Shell;

/// <summary>설정 창이 셸에 위임하는 계약 (AppController가 구현).</summary>
public interface ISettingsHost
{
    AppSettings Settings { get; }

    IReadOnlyList<(string Id, string Name, HotkeyDef Effective)> RemappableHotkeys { get; }

    /// <summary>모달 확인 즉시 재등록 (AC-23).</summary>
    void RemapHotkey(string id, HotkeyDef def);

    void SuppressHotkeys();

    void RestoreHotkeys();

    /// <summary>일반 설정 적용 + 저장 (확인 버튼).</summary>
    void ApplyGeneralSettings(AppSettings updated);

    /// <summary>업데이트 확인 및 안내 대화상자 표시.</summary>
    void CheckForUpdates();

    /// <summary>프로그램 종료.</summary>
    void ExitApp();
}

/// <summary>
/// 설정 창 (WI-16, F13 실측: 510x444 세로 스크롤). 한국어 라벨은 잠근 문자열 확정본만 사용.
/// UI 배율 항목은 명시적 제외 (CRIT-7 / 이연 5번: Round 14 잠금 문자열·AC에 없음).
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly ISettingsHost _host;
    private readonly CheckBox _runAtLogin;
    private readonly CheckBox _checkUpdate;
    private readonly CheckBox _wheelSize;
    private readonly CheckBox _syncStyles;
    private readonly RadioButton _boardAll;
    private readonly RadioButton _boardSingle;
    private readonly RadioButton _boardWhite;
    private readonly RadioButton _boardBlack;
    private readonly CheckBox _halo;
    private readonly TextBox _saveFolder;
    private readonly List<(string DeviceName, CheckBox CheckBox)> _monitorCheckBoxes = [];

    // 바로가기 색상 편집 보류분 (사용자 요청 17차): 확인을 눌러야 적용된다 —
    // 취소로 닫으면 아무것도 바뀌지 않는 다른 항목들과 동일하게 동작하게 한다.
    private readonly Color[] _quickColors;
    private readonly List<Border> _quickSwatches = [];

    public SettingsWindow(ISettingsHost host)
    {
        _host = host;
        var s = host.Settings;

        Title = Strings.Settings;
        Width = 510;
        // 사용자 요청 17차로 보드 기본색 2줄 + 바로가기 색상 섹션이 늘어 444로는 마지막 섹션이
        // 접혀 보이지 않는다 (스크롤해야 닿음). 실측으로 재산출한 높이.
        Height = 560;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;

        _runAtLogin = new CheckBox { Content = Strings.SettingsRunAtLogin, IsChecked = RunAtLogin.IsEnabled(), Margin = RowMargin };
        _checkUpdate = new CheckBox { Content = Strings.SettingsCheckUpdate, IsChecked = s.CheckUpdateOnStart, VerticalAlignment = VerticalAlignment.Center };
        _wheelSize = new CheckBox { Content = Strings.SettingsWheelSize, IsChecked = s.WheelAdjustsPenSize, Margin = RowMargin };
        // 도구별 색·굵기 개별/동기화 (사용자 조타: 기본 개별).
        _syncStyles = new CheckBox { Content = Strings.SettingsSyncToolStyles, IsChecked = s.SyncToolStyles, Margin = RowMargin };
        _boardAll = new RadioButton { Content = Strings.SettingsBoardAll, IsChecked = s.BoardAllMonitors, Margin = RowMargin, GroupName = "board" };
        _boardSingle = new RadioButton { Content = Strings.SettingsBoardSingle, IsChecked = !s.BoardAllMonitors, Margin = RowMargin, GroupName = "board" };
        // 보드 기본색 (사용자 요청 17차): 보드 버튼을 눌렀을 때 켜지는 색.
        _boardWhite = new RadioButton { Content = Strings.Whiteboard, IsChecked = !s.DefaultBoardIsBlack, Margin = RowMargin, GroupName = "boardDefault" };
        _boardBlack = new RadioButton { Content = Strings.Blackboard, IsChecked = s.DefaultBoardIsBlack, Margin = RowMargin, GroupName = "boardDefault" };
        _halo = new CheckBox { Content = Strings.SettingsHighlightCursor, IsChecked = s.HighlightCursor, Margin = RowMargin };

        _quickColors = ReadQuickColors(s);

        _saveFolder = new TextBox
        {
            Text = string.IsNullOrEmpty(s.SaveFolder) ? Capture.CaptureFileNaming.DefaultSaveFolder() : s.SaveFolder,
            IsReadOnly = true,
            Width = 360,
        };
        var browse = new Button { Content = "...", Width = 32, Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += (_, _) => BrowseFolder();
        var folderRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = RowMargin };
        folderRow.Children.Add(_saveFolder);
        folderRow.Children.Add(browse);

        var updateRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = RowMargin };
        updateRow.Children.Add(_checkUpdate);
        var checkNowBtn = new Button
        {
            Content = Strings.SettingsCheckUpdateNow,
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(6, 1, 6, 1),
            FontSize = 11,
        };
        checkNowBtn.Click += (_, _) => _host.CheckForUpdates();
        var curVer = Updates.UpdateService.CurrentVersion;
        var versionLabel = new TextBlock
        {
            Text = $"(v{curVer})",
            Foreground = Brushes.Gray,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        updateRow.Children.Add(checkNowBtn);
        updateRow.Children.Add(versionLabel);

        var monitors = Interop.MonitorTopology.Enumerate();
        var monitorSection = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        monitorSection.Children.Add(SectionHeader(Strings.SettingsMonitors));
        monitorSection.Children.Add(new TextBlock
        {
            Text = Strings.SettingsMonitorsHint,
            Margin = new Thickness(4, 0, 4, 6),
            Foreground = Brushes.Gray,
            FontSize = 11,
        });
        for (int i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            string label = $"{i + 1}번 화면: {m.DeviceName} ({m.Bounds.Width}×{m.Bounds.Height})" + (m.IsPrimary ? $" {Strings.PrimaryMonitorBadge}" : "");
            bool isChecked = !s.DisabledMonitors.Contains(m.DeviceName);
            var cb = new CheckBox
            {
                Content = label,
                IsChecked = isChecked,
                Margin = RowMargin,
            };
            _monitorCheckBoxes.Add((m.DeviceName, cb));
            monitorSection.Children.Add(cb);
        }

        var stack = new StackPanel { Margin = new Thickness(14) };
        stack.Children.Add(SectionHeader(Strings.SettingsGeneral));
        stack.Children.Add(_runAtLogin);
        stack.Children.Add(updateRow);
        stack.Children.Add(_wheelSize);
        stack.Children.Add(_syncStyles);
        stack.Children.Add(_boardAll);
        stack.Children.Add(_boardSingle);
        stack.Children.Add(RowLabel(Strings.SettingsBoardDefault));
        stack.Children.Add(_boardWhite);
        stack.Children.Add(_boardBlack);
        stack.Children.Add(_halo);
        stack.Children.Add(RowLabel(Strings.SettingsSaveFolder));
        stack.Children.Add(folderRow);
        stack.Children.Add(monitorSection);
        stack.Children.Add(SectionHeader(Strings.SettingsQuickColors));
        stack.Children.Add(BuildQuickColorRow());
        stack.Children.Add(SectionHeader(Strings.SettingsHotkeys));
        foreach (var (id, name, effective) in host.RemappableHotkeys)
        {
            stack.Children.Add(HotkeyRow(id, name, effective));
        }

        var exitButton = new Button
        {
            Content = Strings.SettingsExitApp,
            Width = 100,
            Margin = new Thickness(4),
        };
        exitButton.Click += (_, _) =>
        {
            var res = MessageBox.Show(
                this,
                Strings.ExitConfirmMessage,
                Strings.AppName,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                Close();
                _host.ExitApp();
            }
        };

        var checkUpdateBottomBtn = new Button
        {
            Content = Strings.SettingsCheckUpdateBtn,
            Width = 100,
            Margin = new Thickness(4),
        };
        checkUpdateBottomBtn.Click += (_, _) => _host.CheckForUpdates();

        var okButton = new Button { Content = Strings.SettingsOk, Width = 80, Margin = new Thickness(4), IsDefault = true };
        okButton.Click += (_, _) => { Apply(); Close(); };
        var cancelButton = new Button { Content = Strings.SettingsCancel, Width = 80, Margin = new Thickness(4), IsCancel = true };
        cancelButton.Click += (_, _) => Close();

        var leftButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        leftButtons.Children.Add(exitButton);

        var rightButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        rightButtons.Children.Add(checkUpdateBottomBtn);
        rightButtons.Children.Add(okButton);
        rightButtons.Children.Add(cancelButton);

        var bottomGrid = new Grid { Margin = new Thickness(10) };
        bottomGrid.Children.Add(leftButtons);
        bottomGrid.Children.Add(rightButtons);

        var root = new DockPanel();
        DockPanel.SetDock(bottomGrid, Dock.Bottom);
        root.Children.Add(bottomGrid);
        root.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = stack,
        });
        Content = root;
    }

    public nint Hwnd { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Hwnd = WindowStyling.GetHwnd(this);
    }

    /// <summary>설정의 바로가기 색상을 읽어 6칸 배열로 만든다 (모자라거나 깨진 칸은 기본색).</summary>
    private static Color[] ReadQuickColors(AppSettings s)
    {
        var colors = new Color[AppState.QuickColorCount];
        for (int i = 0; i < colors.Length; i++)
        {
            var fallback = ColorPalette.DefaultQuickColors[i];
            colors[i] = s.QuickColors is { } saved && i < saved.Length
                ? ColorPalette.Parse(saved[i], fallback)
                : fallback;
        }
        return colors;
    }

    /// <summary>바로가기 색상 6칸 + 기본값 복원 버튼 (사용자 요청 17차).</summary>
    private UIElement BuildQuickColorRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2, 4, 2) };
        _quickSwatches.Clear();
        for (int i = 0; i < _quickColors.Length; i++)
        {
            int slot = i;
            var swatch = new Border
            {
                Width = 34,
                Height = 26,
                Margin = new Thickness(0, 0, 6, 0),
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(_quickColors[slot]),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = $"Ctrl+Shift+{slot + 1}",
            };
            swatch.MouseLeftButtonUp += (_, _) => PickQuickColor(slot);
            _quickSwatches.Add(swatch);
            row.Children.Add(swatch);
        }

        var reset = new Button { Content = Strings.SettingsQuickColorsReset, Padding = new Thickness(8, 2, 8, 2) };
        reset.Click += (_, _) =>
        {
            for (int i = 0; i < _quickColors.Length; i++)
            {
                _quickColors[i] = ColorPalette.DefaultQuickColors[i];
                _quickSwatches[i].Background = new SolidColorBrush(_quickColors[i]);
            }
        };
        row.Children.Add(reset);

        var wrapper = new StackPanel();
        wrapper.Children.Add(new TextBlock
        {
            Text = Strings.SettingsQuickColorsHint,
            Margin = new Thickness(4, 0, 4, 4),
            Foreground = Brushes.Gray,
            FontSize = 11,
        });
        wrapper.Children.Add(row);
        return wrapper;
    }

    /// <summary>
    /// 한 칸의 색을 확장 팔레트(24색)에서 고른다. 시스템 색 대화상자 대신 팔레트인 이유:
    /// 시스템 대화상자는 WinForms 의존을 하나 더 끌어들이고(프로젝트 규칙: NotifyIcon 전용),
    /// 툴바 팔레트 플라이아웃과 같은 색을 고르는 게 일관된다.
    /// </summary>
    private void PickQuickColor(int slot)
    {
        var popup = new Window
        {
            Title = Strings.SettingsQuickColors,
            Owner = this,
            Topmost = true,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 6, Margin = new Thickness(8) };
        foreach (var color in ColorPalette.Extended)
        {
            var choice = color;
            var cell = new Border
            {
                Width = 30,
                Height = 30,
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(choice),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            cell.MouseLeftButtonUp += (_, _) =>
            {
                _quickColors[slot] = choice;
                _quickSwatches[slot].Background = new SolidColorBrush(choice);
                popup.Close();
            };
            grid.Children.Add(cell);
        }
        popup.Content = grid;
        popup.ShowDialog();
    }

    private static Thickness RowMargin => new(4, 4, 4, 4);

    private static TextBlock SectionHeader(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.Bold,
        FontSize = 14,
        Margin = new Thickness(0, 12, 0, 6),
    };

    private static TextBlock RowLabel(string text) => new() { Text = text, Margin = new Thickness(4, 8, 4, 0) };

    private UIElement HotkeyRow(string id, string name, HotkeyDef effective)
    {
        var label = new TextBlock { Text = name, Width = 220, VerticalAlignment = VerticalAlignment.Center };
        var comboButton = new Button
        {
            Content = HotkeyFormatting.Format(effective),
            Width = 160,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        comboButton.Click += (_, _) =>
        {
            // ARCH-8: 모달 동안 라이브 맵 억제 → 종료 시 복원(확인이면 새 맵으로 재등록).
            _host.SuppressHotkeys();
            try
            {
                var dialog = new HotkeyCaptureDialog(effective) { Owner = this, Topmost = true };
                if (dialog.ShowDialog() == true && dialog.Captured is { } captured)
                {
                    _host.RemapHotkey(id, captured);
                    comboButton.Content = HotkeyFormatting.Format(captured);
                    effective = captured;
                }
            }
            finally
            {
                _host.RestoreHotkeys();
            }
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2, 4, 2) };
        row.Children.Add(label);
        row.Children.Add(comboButton);
        return row;
    }

    private void BrowseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            InitialDirectory = _saveFolder.Text,
        };
        if (dialog.ShowDialog(this) == true)
        {
            _saveFolder.Text = dialog.FolderName;
        }
    }

    private void Apply()
    {
        var updated = _host.Settings;
        updated.RunAtLogin = _runAtLogin.IsChecked == true;
        updated.CheckUpdateOnStart = _checkUpdate.IsChecked == true;
        updated.WheelAdjustsPenSize = _wheelSize.IsChecked == true;
        updated.SyncToolStyles = _syncStyles.IsChecked == true;
        updated.BoardAllMonitors = _boardAll.IsChecked == true;
        updated.DefaultBoardIsBlack = _boardBlack.IsChecked == true;
        updated.QuickColors = [.. _quickColors.Select(ColorPalette.ToHex)];
        updated.HighlightCursor = _halo.IsChecked == true;
        updated.SaveFolder = _saveFolder.Text == Capture.CaptureFileNaming.DefaultSaveFolder()
            ? string.Empty
            : _saveFolder.Text;

        var disabled = _monitorCheckBoxes
            .Where(item => item.CheckBox.IsChecked != true)
            .Select(item => item.DeviceName)
            .ToList();
        // 모든 모니터가 비활성화되는 것을 방지: 최소 1개는 켠다
        if (_monitorCheckBoxes.Count > 0 && disabled.Count == _monitorCheckBoxes.Count)
        {
            disabled.RemoveAt(0);
        }
        updated.DisabledMonitors = disabled;

        _host.ApplyGeneralSettings(updated);
    }
}
