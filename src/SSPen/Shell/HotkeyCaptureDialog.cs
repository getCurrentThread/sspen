using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SSPen.Interop;
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

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None)
        {
            e.Handled = true;
            return; // 수식키 단독은 조합이 아니다.
        }
        if (key is Key.Escape or Key.Enter or Key.Tab)
        {
            base.OnPreviewKeyDown(e);
            return; // 대화상자 조작 키는 그대로 둔다.
        }

        uint mods = 0;
        var modifiers = Keyboard.Modifiers;
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            mods |= NativeMethods.MOD_ALT;
        }
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            mods |= NativeMethods.MOD_CONTROL;
        }
        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            mods |= NativeMethods.MOD_SHIFT;
        }
        if (mods == 0)
        {
            e.Handled = true;
            return; // 전역 핫키는 최소 1개의 수식키가 필요하다.
        }

        _captured = new HotkeyDef(mods, (uint)KeyInterop.VirtualKeyFromKey(key));
        _comboText.Text = HotkeyFormatting.Format(_captured);
        e.Handled = true;
    }
}

/// <summary>핫키 조합 표기 (키캡 이름 — Epic Pen 한국어 UI와 동일하게 키 이름은 그대로 표기).</summary>
public static class HotkeyFormatting
{
    public static string Format(HotkeyDef def)
    {
        var parts = new List<string>(4);
        if ((def.Modifiers & NativeMethods.MOD_CONTROL) != 0)
        {
            parts.Add("Ctrl");
        }
        if ((def.Modifiers & NativeMethods.MOD_ALT) != 0)
        {
            parts.Add("Alt");
        }
        if ((def.Modifiers & NativeMethods.MOD_SHIFT) != 0)
        {
            parts.Add("Shift");
        }
        parts.Add(KeyName(def.VirtualKey));
        return string.Join("+", parts);
    }

    private static string KeyName(uint vk) => vk switch
    {
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        VirtualKeys.OemOpenBracket => "[",
        VirtualKeys.OemCloseBracket => "]",
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        0x20 => "Space",
        0x2C => "PrtScn",
        _ => $"0x{vk:X2}",
    };
}
