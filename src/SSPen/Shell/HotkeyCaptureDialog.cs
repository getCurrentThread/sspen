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
    private HotkeyDef? _captured;

    public HotkeyCaptureDialog(HotkeyDef current)
    {
        Title = Strings.SettingsHotkeys;
        Width = 320;
        Height = 170;
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
        panel.Children.Add(buttons);
        Content = panel;
    }

    /// <summary>확정된 조합 (확인 시에만 유효).</summary>
    public HotkeyDef? Captured => _captured;

    /// <summary>판정은 <see cref="HotkeyCaptureRules.Decide"/>가 한다 (67단계, A6-5). 여기서는 답에 따라 기본 처리·라벨·<c>e.Handled</c>만 바꾼다 —
    /// 대화상자 조작 키만 기본 처리로 흘리고, 나머지(무시·확정)는 입력을 삼킨다.</summary>
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
        }
        e.Handled = true;
    }
}
