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
    private readonly CheckBox _zBandPolling;
    private readonly TextBox _saveFolder;
    private readonly List<(string DeviceName, CheckBox CheckBox)> _monitorCheckBoxes = [];

    // 바로가기 색상 편집 보류분 (사용자 요청 17차): 확인을 눌러야 적용된다 —
    // 취소로 닫으면 아무것도 바뀌지 않는 다른 항목들과 동일하게 동작하게 한다.
    private readonly Color[] _quickColors;
    private readonly List<Border> _quickSwatches = [];

    // 단축키 재지정 보류분: 다른 모든 항목과 같이 확인을 눌러야 적용된다.
    // 예전에는 캡처 즉시 SaveNow까지 해서 취소해도 단축키만 이미 저장돼 있었다.
    // 보류 상태·충돌 표 합성·대화상자 초기값은 HotkeyDraft 한 곳이 소유한다 (67단계, A6-2).
    private readonly HotkeyDraft _draft = new();

    // 판서 화면을 모두 끄면 규칙이 첫 화면을 되살린다 — 그 사실을 알리는 인라인 라벨.
    private readonly TextBlock _monitorNotice;

    // 단축키 검색 (2단계): 행마다 (표시명, 조합 라벨을 읽는 함수, 행 자체)를 들고 있다가
    // SettingsSectionPlan.MatchesHotkeyFilter의 답으로 가시성만 토글한다.
    private readonly List<(string Name, Func<string> Combo, UIElement Row)> _hotkeyRows = [];
    private readonly TextBlock _hotkeyNoMatch = new()
    {
        Text = Strings.SettingsSearchNoMatch,
        Margin = new Thickness(4, 6, 4, 2),
        Foreground = Brushes.Gray,
        FontSize = 11,
        Visibility = Visibility.Collapsed,
    };

    public SettingsWindow(ISettingsHost host)
    {
        _host = host;
        var s = host.Settings;

        Title = Strings.Settings;
        Width = 510;
        // 사용자 요청 17차로 보드 기본색 2줄 + 바로가기 색상 섹션이 늘어 444로는 마지막 섹션이
        // 접혀 보이지 않는다 (스크롤해야 닿음). 실측으로 재산출한 높이.
        Height = SettingsSectionPlan.DefaultHeight;
        // 손으로 맞춘 높이는 그 화면에서만 맞다 — 작은 노트북·고DPI에서는 아래 버튼 줄에 닿지 못했다.
        // 최소 크기는 SettingsSectionPlan이 소유한다 (라벨 220 + 조합 160이 겹치지 않는 폭).
        ResizeMode = ResizeMode.CanResize;
        MinWidth = SettingsSectionPlan.MinWidth;
        MinHeight = SettingsSectionPlan.MinHeight;
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
        // 실험적 기능 (73단계): z-순서 주기 정정. 문구가 길어 최소 폭(460)에서 잘리지 않게 줄바꿈 TextBlock으로 싼다.
        _zBandPolling = new CheckBox
        {
            Content = new TextBlock { Text = Strings.SettingsZBandPolling, TextWrapping = TextWrapping.Wrap },
            IsChecked = s.ZBandPolling,
            Margin = RowMargin,
        };

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
            Text = Strings.SettingsVersionLabel(curVer.ToString()),
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
        // 실험적 기능은 접힌 단축키 위 (SettingsSectionPlan.Order, 73단계): 섹션 머리 + 체크박스 + 회색 힌트(판서 화면·바로가기 색상 힌트와 같은 모양).
        stack.Children.Add(SectionHeader(Strings.SettingsExperimental));
        stack.Children.Add(_zBandPolling);
        stack.Children.Add(new TextBlock
        {
            Text = Strings.SettingsZBandPollingHint,
            Margin = new Thickness(4, 0, 4, 6),
            Foreground = Brushes.Gray,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(BuildHotkeySection(host));

        // 하단 "업데이트 확인" 버튼은 뺐다: 버전 라벨 옆 인라인 버튼과 같은 동작이라,
        // 나란히 놓인 확인/취소와 같은 무게로 보이면 그 줄이 무엇을 확정하는 줄인지 흐려진다.
        var okButton = new Button { Content = Strings.SettingsOk, Width = 80, Margin = new Thickness(4), IsDefault = true };
        okButton.Click += (_, _) => ApplyAndClose();
        var cancelButton = new Button { Content = Strings.SettingsCancel, Width = 80, Margin = new Thickness(4), IsCancel = true };
        cancelButton.Click += (_, _) => Close();

        // 프로그램 종료 버튼은 툴바 설정 메뉴로 옮겼다 (55단계) — 여기서는 확인/취소만 남는다.
        var rightButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        rightButtons.Children.Add(okButton);
        rightButtons.Children.Add(cancelButton);

        var bottomGrid = new Grid { Margin = new Thickness(10) };
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
                ToolTip = QuickColorHotkeys.Label(slot), // 조합의 소유자는 QuickColorHotkeys (67단계, A9-3)
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
    /// 호스트의 지금 유효 조합. 행을 만들 때의 값에 머물지 않는 이유: 확인 후 창이 열린 채 남는 경로(판서 화면 교정 알림)에서
    /// 방금 적용한 행을 다시 열면 적용된 조합을 보여야 한다 — 예전 closure 재대입이 그 값이었다 (67단계, A6-2).
    /// </summary>
    private HotkeyDef? LiveEffective(string id) =>
        _host.RemappableHotkeys.FirstOrDefault(entry => entry.Id == id).Effective;

    private static Thickness RowMargin => new(4, 4, 4, 4);

    private static TextBlock SectionHeader(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.Bold,
        FontSize = 14,
        Margin = new Thickness(0, 12, 0, 6),
    };

    private static TextBlock RowLabel(string text) => new() { Text = text, Margin = new Thickness(4, 8, 4, 0) };

    /// <summary>
    /// 단축키 섹션: 검색 상자 + 21행을 담은 <see cref="Expander"/>. 기본 접힘 여부와 검색 판정은
    /// <see cref="SettingsSectionPlan"/>이 소유하고, 여기서는 <c>Visibility</c>만 바른다.
    /// 접어 두는 이유는 길이다 — 21행은 나머지 전 섹션을 합친 것보다 길어서 펼쳐 두면
    /// 자주 바꾸는 일반 항목이 화면 밖으로 밀린다.
    /// </summary>
    private UIElement BuildHotkeySection(ISettingsHost host)
    {
        var search = new TextBox { Margin = new Thickness(4, 2, 4, 6), Padding = new Thickness(2) };
        var rows = new StackPanel();
        foreach (var (id, name, effective) in host.RemappableHotkeys)
        {
            rows.Children.Add(HotkeyRow(id, name, effective));
        }
        rows.Children.Add(_hotkeyNoMatch);

        search.TextChanged += (_, _) => ApplyHotkeyFilter(search.Text);

        var body = new StackPanel();
        // 헤더만 굵게 — 상속되면 21행이 전부 굵어진다 (StackPanel은 Control이 아니라 첨부 속성으로 되돌린다).
        body.SetValue(System.Windows.Documents.TextElement.FontWeightProperty, FontWeights.Normal);
        body.SetValue(System.Windows.Documents.TextElement.FontSizeProperty, 12.0);
        body.Children.Add(new TextBlock
        {
            Text = Strings.SettingsSearchHotkeys,
            Margin = new Thickness(4, 4, 4, 2),
            Foreground = Brushes.Gray,
            FontSize = 11,
        });
        body.Children.Add(search);
        body.Children.Add(rows);

        return new Expander
        {
            Header = Strings.SettingsHotkeys,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Margin = new Thickness(0, 12, 0, 6),
            IsExpanded = SettingsSectionPlan.StartsExpanded(SettingsSection.Hotkeys),
            Content = body,
        };
    }

    private void ApplyHotkeyFilter(string query)
    {
        bool any = false;
        foreach (var (name, combo, row) in _hotkeyRows)
        {
            bool visible = SettingsSectionPlan.MatchesHotkeyFilter(name, combo(), query);
            row.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            any |= visible;
        }
        _hotkeyNoMatch.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }

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
            var captured = HotkeyRemapFlow.Run(_host, () =>
            {
                // 보류 값이 있으면 그것을, 없으면 호스트의 지금 유효 조합을 보여 준다 (예전 closure 재대입 effective = def와 같은 값).
                var dialog = new HotkeyCaptureDialog(_draft.EffectiveFor(id, LiveEffective(id) ?? effective)) { Owner = this, Topmost = true };
                return dialog.ShowDialog() == true ? dialog.Captured : null;
            });
            if (captured is not { } def)
            {
                return;
            }
            // 충돌은 이 창에서, 지금 알린다 — 예전에는 나중에 RegisterHotKey가 실패하며
            // 조합을 만든 창 밖의 트레이 풍선으로 5초간 스쳐 갔다. 보류분도 표에 덮어 본다 —
            // 한 창에서 두 항목을 같은 조합으로 바꾸는 경우를 잡으려면 보류분도 표에 있어야 한다.
            if (_draft.Conflict(_host.RemappableHotkeys, id, def, AppState.QuickColorCount) is { } owner)
            {
                MessageBox.Show(
                    Strings.HotkeyAlreadyUsed(owner), Strings.SettingsHotkeys,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // 다른 설정과 같은 규칙: 확인을 눌러야 적용된다. 여기서는 보류 목록과 라벨만 바꾼다.
            _draft.Stage(id, def);
            comboButton.Content = HotkeyFormatting.Format(def);
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2, 4, 2) };
        row.Children.Add(label);
        row.Children.Add(comboButton);
        // 조합은 재지정으로 바뀌므로 값이 아니라 읽는 함수를 등록한다 — 바꾼 직후 그 조합으로 검색해도 걸린다.
        _hotkeyRows.Add((name, () => comboButton.Content as string ?? string.Empty, row));
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
    /// 창을 닫을지는 반환한 결과의 <see cref="SettingsApplyResult.KeepsWindowOpen"/>이 정한다 (78단계, A6-1).
    /// </summary>
    private SettingsApplyResult Apply()
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
            Monitors: [.. _monitorCheckBoxes.Select(item => (item.DeviceName, item.CheckBox.IsChecked == true))],
            ZBandPolling: _zBandPolling.IsChecked == true);

        var updated = _host.Settings;
        var result = SettingsFormRules.ApplyTo(updated, values, Capture.CaptureFileNaming.DefaultSaveFolder());
        // 보류 중인 재지정을 여기서 한 번에 반영한다 — 전부 쓴 뒤 저장·재등록 1회 (AC-23; 79단계, A6-3).
        // 한 건씩 부르면 맞바꾸기 도중의 중간 충돌이 가짜 트레이 경고를 띄운다. 보류분이 없으면 부르지 않는다(예전 0회 루프와 같다).
        if (!_draft.IsEmpty)
        {
            _host.RemapHotkeys(_draft.Drain());
        }
        _host.ApplyGeneralSettings(updated);
        if (result.KeepsWindowOpen && result.RestoredDeviceName is { } device)
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
        else
        {
            // 이번 확인에는 교정이 없었다 — 지난 교정의 알림을 접는다 (78단계, A6-1).
            _monitorNotice.Visibility = Visibility.Collapsed;
        }
        return result;
    }

    /// <summary>
    /// 확인 버튼: 적용 후 교정이 있었으면 창을 열어 둔다 (사용자가 결과를 봐야 한다). 판정은 순수 결과값
    /// <see cref="SettingsApplyResult.KeepsWindowOpen"/> 하나다 — 알림 라벨의 가시성을 읽으면 한 번 뜬 알림이
    /// 이후의 모든 확인을 막는다 (78단계, A6-1).
    /// </summary>
    private void ApplyAndClose()
    {
        if (!Apply().KeepsWindowOpen)
        {
            Close();
        }
    }
}
