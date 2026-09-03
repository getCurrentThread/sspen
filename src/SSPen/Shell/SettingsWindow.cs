using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SSPen.Annotation;
using SSPen.Interop;
using SSPen.Settings;

namespace SSPen.Shell;

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

    // 단축키 재지정 보류분: 다른 모든 항목과 같이 확인을 눌러야 적용된다.
    // 예전에는 캡처 즉시 SaveNow까지 해서 취소해도 단축키만 이미 저장돼 있었다.
    private readonly Dictionary<string, HotkeyDef> _pendingHotkeys = [];

    // 판서 화면을 모두 끄면 규칙이 첫 화면을 되살린다 — 그 사실을 알리는 인라인 라벨.
    private readonly TextBlock _monitorNotice;

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

        _quickColors = ColorPalette.RestoreQuickColors(s.QuickColors); // 드래프트 — 규칙은 ColorPalette 한 곳 (39단계)

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
            string label = Strings.SettingsMonitorLabel(i + 1, m.DeviceName, m.Bounds.Width, m.Bounds.Height) + (m.IsPrimary ? $" {Strings.PrimaryMonitorBadge}" : "");
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
        _monitorNotice = new TextBlock
        {
            Margin = new Thickness(4, 4, 4, 0),
            Foreground = Brushes.OrangeRed,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        monitorSection.Children.Add(_monitorNotice);

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
        okButton.Click += (_, _) => ApplyAndClose();
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

    /// <summary>
    /// 아직 확인을 누르지 않은 단축키 재지정. 보류 값이 있으면 그것이, 없으면 호스트의 현재 유효 조합이 충돌 판정의 입력이다 —
    /// 한 창에서 두 항목을 같은 조합으로 바꾸는 경우를 잡으려면 보류분도 표에 있어야 한다.
    /// </summary>
    private string? ConflictFor(string editingId, HotkeyDef candidate)
    {
        var table = _host.RemappableHotkeys
            .Select(entry => (entry.Id, entry.Name,
                Effective: _pendingHotkeys.TryGetValue(entry.Id, out var pending) ? pending : entry.Effective))
            .ToList();
        return HotkeyConflictRules.Find(table, editingId, candidate, Annotation.AppState.QuickColorCount);
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
            // ARCH-8 순서(억제 → 모달 → 반드시 복원)는 HotkeyRemapFlow가 소유한다 (40단계). 창은 대화상자와 라벨만.
            var captured = HotkeyRemapFlow.Run(_host, id, () =>
            {
                var dialog = new HotkeyCaptureDialog(effective) { Owner = this, Topmost = true };
                return dialog.ShowDialog() == true ? dialog.Captured : null;
            });
            if (captured is not { } def)
            {
                return;
            }
            // 충돌은 이 창에서, 지금 알린다 — 예전에는 나중에 RegisterHotKey가 실패하며
            // 조합을 만든 창 밖의 트레이 풍선으로 5초간 스쳐 갔다.
            if (ConflictFor(id, def) is { } owner)
            {
                MessageBox.Show(
                    Strings.HotkeyAlreadyUsed(owner), Strings.SettingsHotkeys,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // 다른 설정과 같은 규칙: 확인을 눌러야 적용된다. 여기서는 보류 목록과 라벨만 바꾼다.
            _pendingHotkeys[id] = def;
            comboButton.Content = HotkeyFormatting.Format(def);
            effective = def;
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

    /// <summary>
    /// 확인: 컨트롤 → 값 스냅샷은 여기, 값 → AppSettings는 <see cref="SettingsFormRules"/> (41단계).
    /// <c>_host.Settings</c>를 제자리 변형하고 ApplyGeneralSettings를 정확히 1회 부른다 — 새 AppSettings를 만들면 폼에 없는
    /// 필드(핫키·툴바 위치·페이딩·도구별 스타일)가 소실된다.
    /// </summary>
    private void Apply()
    {
        var values = new SettingsFormValues(
            RunAtLogin: _runAtLogin.IsChecked == true,
            CheckUpdateOnStart: _checkUpdate.IsChecked == true,
            WheelAdjustsPenSize: _wheelSize.IsChecked == true,
            SyncToolStyles: _syncStyles.IsChecked == true,
            BoardAllMonitors: _boardAll.IsChecked == true,
            DefaultBoardIsBlack: _boardBlack.IsChecked == true,
            QuickColors: _quickColors,
            HighlightCursor: _halo.IsChecked == true,
            SaveFolder: _saveFolder.Text,
            Monitors: [.. _monitorCheckBoxes.Select(item => (item.DeviceName, item.CheckBox.IsChecked == true))]);

        var updated = _host.Settings;
        var result = SettingsFormRules.ApplyTo(updated, values, Capture.CaptureFileNaming.DefaultSaveFolder());
        // 보류 중인 재지정을 여기서 반영한다 — 즉시 재등록(AC-23)은 RemapHotkey 안에서 그대로 일어난다.
        foreach (var (id, def) in _pendingHotkeys)
        {
            _host.RemapHotkey(id, def);
        }
        _pendingHotkeys.Clear();
        _host.ApplyGeneralSettings(updated);
        if (result.MonitorSelectionCoerced && result.RestoredDeviceName is { } device)
        {
            // 교정을 알리되 창은 닫지 않는다: 사용자가 방금 무슨 일이 일어났는지 보고 다시 고를 수 있어야 한다.
            _monitorNotice.Text = Strings.MonitorRestored(device);
            _monitorNotice.Visibility = Visibility.Visible;
            var restored = _monitorCheckBoxes.FirstOrDefault(item => item.DeviceName == device);
            if (restored.CheckBox is not null)
            {
                restored.CheckBox.IsChecked = true;
            }
        }
    }

    /// <summary>확인 버튼: 적용 후 교정이 있었으면 창을 열어 둔다 (사용자가 결과를 봐야 한다).</summary>
    private void ApplyAndClose()
    {
        Apply();
        if (_monitorNotice.Visibility != Visibility.Visible)
        {
            Close();
        }
    }
}
