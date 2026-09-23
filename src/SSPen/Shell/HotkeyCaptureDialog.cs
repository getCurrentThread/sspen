using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SSPen.Settings;

namespace SSPen.Shell;

/// <summary>
/// 모달 핫키 지정 대화상자 (F13: Epic Pen 모달 방식, "키 조합을 누르세요").
/// 호출 측이 모달 동안 라이브 핫키 맵을 억제한다 (ARCH-8).
/// </summary>
public sealed class HotkeyCaptureDialog : Window
{
    private readonly TextBlock _comboText;
    private readonly TextBlock _rejectedText;
    private HotkeyDef? _captured;

    public HotkeyCaptureDialog(HotkeyDef current)
    {
        Title = Strings.SettingsHotkeys;
        Width = 320;
        // 높이는 내용에 맞춘다 — 거부 안내(80단계, A6-7)가 보일 때만 두 줄만큼 늘어난다.
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.ToolWindow;
        Topmost = true;

        _comboText = new TextBlock
        {
            Text = HotkeyFormatting.Format(current),
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 8),
        };

        // 거부 이유 안내 (80단계, A6-7): Shift 단독 + 글자 입력 키처럼 잡지 않는 조합을 눌렀을 때만 보인다.
        _rejectedText = new TextBlock
        {
            Foreground = Brushes.Gray,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
            Visibility = Visibility.Collapsed,
        };

        var okButton = new Button { Content = Strings.SettingsOk, Width = 80, Margin = new Thickness(4), IsDefault = true };
        okButton.Click += (_, _) => { DialogResult = _captured is not null; };
        var cancelButton = new Button { Content = Strings.SettingsCancel, Width = 80, Margin = new Thickness(4), IsCancel = true };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = Strings.SettingsPressKeys,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)),
        });
        panel.Children.Add(_comboText);
        panel.Children.Add(_rejectedText);
        panel.Children.Add(buttons);
        Content = panel;
    }

    /// <summary>확정된 조합 (확인 시에만 유효).</summary>
    public HotkeyDef? Captured => _captured;

    /// <summary>판정은 <see cref="HotkeyCaptureRules.Decide"/>가 한다 (67단계, A6-5). 여기서는 답에 따라 기본 처리·라벨·<c>e.Handled</c>만 바꾼다 —
    /// 대화상자 조작 키만 기본 처리로 흘리고, 나머지(무시·확정·거부)는 입력을 삼킨다. 거부는 지금 조합을 바꾸지 않고
    /// 이유만 안내 줄에 보이며, 다음 확정 때 그 줄을 숨긴다 (80단계, A6-7).</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var verdict = HotkeyCaptureRules.Decide(e.Key, e.SystemKey, Keyboard.Modifiers);
        if (verdict.Action == HotkeyCaptureAction.PassThrough)
        {
            base.OnPreviewKeyDown(e);
            return; // 대화상자 조작 키는 그대로 둔다.
        }
        if (verdict.Captured is { } captured)
        {
            _captured = captured;
            _comboText.Text = HotkeyFormatting.Format(captured);
            _rejectedText.Visibility = Visibility.Collapsed;
        }
        else if (verdict.Action == HotkeyCaptureAction.Rejected)
        {
            _rejectedText.Text = verdict.Reason;
            _rejectedText.Visibility = Visibility.Visible;
        }
        e.Handled = true;
    }
}
