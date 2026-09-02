using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SSPen.Shell;

/// <summary>
/// 이름 + 유효 단축키 2줄 툴팁 팩토리 (37단계, TIP-REG/AC-20; Epic Pen 대응: "선 도구" / "(ctrl + shift + L)").
/// ToolbarStripBuilder.AttachTooltip을 옮겼다 — 스트립 버튼과 플라이아웃 항목이 같은 팩토리를 쓰면서
/// ToolbarFlyouts→ToolbarStripBuilder 역참조(2-사이클)가 생겨 있었다. 등록 델리게이트는 필수다: 문자열 툴팁이나
/// 미등록 ToolTip은 자체 HWND 팝업이라 툴바가 숨을 때 닫을 수 없어 캡처 결과물 위에 남는다 (AGENTS L81).
/// 레지스트리(RegisterTooltip/CloseTooltips)는 ToolbarFlyouts가 그대로 소유한다 — 여기는 만들고 넘길 뿐이다.
/// </summary>
internal static class ToolbarTooltips
{
    /// <summary>
    /// <paramref name="target"/>에 ToolTip 인스턴스를 붙이고 <paramref name="register"/>로 정확히 한 번 등록한다.
    /// <paramref name="hotkeyId"/>가 null이면 둘째 줄이 아예 없고, 있으면 열릴 때마다 현재 유효 조합으로 갱신한다 (재지정 즉시 반영).
    /// </summary>
    public static void Attach(IShellActions actions, Border target, string label, string? hotkeyId, Action<ToolTip> register)
    {
        var title = new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Foreground = ToolbarTheme.IconBrush,
        };
        var panel = new StackPanel();
        panel.Children.Add(title);
        TextBlock? combo = null;
        if (hotkeyId is not null)
        {
            combo = new TextBlock { FontSize = 11, Foreground = ToolbarTheme.TooltipComboBrush };
            panel.Children.Add(combo);
        }
        var tooltip = new ToolTip { Content = panel };
        target.ToolTip = tooltip;
        register(tooltip);
        ToolTipService.SetInitialShowDelay(target, 300);
        // 사용자 조타: 툴팁이 옆 메뉴/플라이아웃을 가리지 않게 버튼 아래에 표시.
        ToolTipService.SetPlacement(target, PlacementMode.Bottom);
        if (combo is not null && hotkeyId is not null)
        {
            // 열릴 때마다 현재 유효 조합으로 갱신 (재지정 즉시 반영) — 빌드 시점에 라벨을 붙잡지 않는다.
            target.ToolTipOpening += (_, _) =>
            {
                var (text, visible) = ComboLine(actions.HotkeyLabel(hotkeyId));
                combo.Text = text;
                combo.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            };
        }
    }

    /// <summary>둘째 줄 표기: 조합이 있으면 "(조합)"·표시, 없으면(핫키 미할당) 빈 문자열·숨김 — 자리만 남기지 않는다.</summary>
    public static (string Text, bool Visible) ComboLine(string? label) =>
        label is null ? (string.Empty, false) : ($"({label})", true);
}
