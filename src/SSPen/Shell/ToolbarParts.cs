using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SSPen.Annotation;

namespace SSPen.Shell;

// 36단계: ToolbarStripBuilder.cs 머리에 있던 ButtonParts/ToolbarParts를 자기 파일로 옮겼다 (글자 그대로). 조립(StripBuilder)은
// 창 생성 때 한 번 달리고, 산출물·갱신 어댑터(Parts)는 창이 사는 동안 AppState.Changed마다 불린다 — 수명이 다르다.
// 갱신 안의 값 판정(미리보기 점 지름·보드 배지·퀵스와치 링)은 ToolbarStateMap이 소유하고 여기는 값을 읽어 UI에 쓰기만 한다.

/// <summary>버튼 시각 요소 묶음: 배경/글리프/플라이아웃 삼각형/색 배지(도구 그룹별).</summary>
internal sealed record ButtonParts(
    Border Root,
    TextBlock Glyph,
    (string Regular, string Filled) Icon,
    System.Windows.Shapes.Polygon? FlyoutMark,
    System.Windows.Shapes.Ellipse? ColorBadge,
    ToolStyleGroup? BadgeGroup);

/// <summary>
/// 스트립 조립 산출물 (god file 분할, ARCH-11 후속): 버튼 딕셔너리·퀵스와치·미리보기 원·
/// 현재 색 스와치·보드 배지·메뉴 패널을 묶고, RefreshButton/RefreshActiveStates/UpdatePreviewDot
/// 갱신 로직을 메서드로 제공한다 (StateMap 소비).
/// </summary>
public sealed class ToolbarParts
{
    internal readonly Dictionary<ToolbarButtonId, ButtonParts> Buttons;

    // 사용자 요청 17차: 색을 담아 두지 않고 **칸 번호**만 담는다 — 설정에서 바로가기 색을
    // 바꾸면 빌드 시점에 박아 둔 색은 영원히 옛 색으로 남는다.
    // Ring은 이중 톤 선택 링의 바깥(강조색) 테두리를 지는 래퍼다 — 안쪽 흰 링만으로는
    // 흰색·노랑 칸에서 선택이 보이지 않는다 (ToolbarStateMap.QuickSwatchOuterRingThickness).
    internal readonly List<(Border Swatch, Border Ring, int Slot)> QuickSwatches;
    private readonly StackPanel? _menuPanel;

    // 버튼별 포인터 상태 (호버/눌림). 배경 선택은 PressStateRules가 하고 여기는 기억만 한다 —
    // AppState.Changed로 오는 갱신이 눌림 표시를 지워 버리지 않게 하려면 상태가 어딘가 남아 있어야 한다.
    private readonly Dictionary<ToolbarButtonId, (bool Hovered, bool Pressed)> _pointer = [];

    internal System.Windows.Shapes.Ellipse? PreviewDot;
    internal Border? CurrentColorSwatch;
    internal Border? BoardBadge;

    internal ToolbarParts(
        Dictionary<ToolbarButtonId, ButtonParts> buttons,
        List<(Border Swatch, Border Ring, int Slot)> quickSwatches,
        StackPanel? menuPanel)
    {
        Buttons = buttons;
        QuickSwatches = quickSwatches;
        _menuPanel = menuPanel;
    }

    private bool MenuCollapsed => _menuPanel is { Visibility: Visibility.Collapsed };

    /// <summary>접히는 메뉴 영역(눈 버튼 아래 전체)의 표시 여부를 설정한다.</summary>
    public void SetMenuCollapsed(bool collapsed)
    {
        if (_menuPanel is not null)
        {
            _menuPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public void RefreshButton(AppState state, ToolbarButtonId id)
    {
        if (!Buttons.TryGetValue(id, out var parts))
        {
            return;
        }
        bool active = ToolbarStateMap.IsActive(state, id, MenuCollapsed);
        var icon = ToolbarStateMap.IconFor(state, id, MenuCollapsed, parts.Icon);
        var (hovered, pressed) = _pointer.TryGetValue(id, out var pointer) ? pointer : (false, false);
        var visual = PressStateRules.Resolve(active, hovered, pressed);
        parts.Root.Background = visual switch
        {
            ButtonVisualState.Active => ToolbarTheme.AccentBrush,
            ButtonVisualState.Pressed => ToolbarTheme.ButtonPressedBrush,
            ButtonVisualState.Hover => ToolbarTheme.ButtonHoverBrush,
            _ => Brushes.Transparent,
        };
        // 호버 배경은 흰 스트립과 1.4:1이라 단독으로는 거의 보이지 않는다 (ShellPaletteTests) —
        // 어포던스는 이 1px 외곽선이 진다. 두께는 언제나 1이고 색만 바뀐다: 0↔1로 토글하면
        // 글리프가 한 픽셀 밀려 손을 얹을 때마다 흔들린다.
        parts.Root.BorderBrush = visual is ButtonVisualState.Hover or ButtonVisualState.Pressed
            ? ToolbarTheme.AccentBrush
            : Brushes.Transparent;
        parts.Glyph.Text = active ? icon.Filled : icon.Regular;
        parts.Glyph.FontFamily = ToolbarStateMap.GlyphFontIsFilled(icon, active) ? Icons.Filled : Icons.Regular;
        parts.Glyph.Foreground = active ? Brushes.White : ToolbarTheme.IconBrush;
        if (parts.FlyoutMark is not null)
        {
            parts.FlyoutMark.Fill = active ? Brushes.White : ToolbarTheme.IconBrush;
        }
        if (parts.ColorBadge is not null && parts.BadgeGroup is not null)
        {
            var badgeGroup = ToolbarStateMap.BadgeGroupFor(state, id, parts.BadgeGroup.Value);
            parts.ColorBadge.Fill = ToolbarTheme.Freeze(new SolidColorBrush(state.ColorOf(badgeGroup)));
        }
        // 보드 그룹 버튼 (사용자 조타 14차): 활성 보드 색 스와치 배지. 표시/색 판정은 ToolbarStateMap.
        if (id == ToolbarButtonId.Board && BoardBadge is not null)
        {
            BoardBadge.Visibility = ToolbarStateMap.BoardBadgeVisible(state.Board) ? Visibility.Visible : Visibility.Collapsed;
            BoardBadge.Background = ToolbarStateMap.BoardBadgeIsBlack(state.Board) ? Brushes.Black : Brushes.White;
        }
    }

    /// <summary>포인터 상태를 기록하고 그 버튼만 다시 칠한다 (어댑터의 마우스 핸들러가 부른다).</summary>
    internal void SetPointerState(AppState state, ToolbarButtonId id, bool hovered, bool pressed)
    {
        _pointer[id] = (hovered, pressed);
        RefreshButton(state, id);
    }

    public void RefreshActiveStates(AppState state)
    {
        foreach (var id in Buttons.Keys.ToList())
        {
            RefreshButton(state, id);
        }
        UpdatePreviewDot(state);
        if (CurrentColorSwatch is not null)
        {
            CurrentColorSwatch.Background = ToolbarTheme.Freeze(new SolidColorBrush(state.CurrentColor));
        }
        foreach (var (swatch, ring, slot) in QuickSwatches)
        {
            if (slot >= state.QuickColors.Count)
            {
                continue;
            }
            // 갱신 시점에 색을 읽는다 (AGENTS "Colors come from ColorPalette") — 빌드 시점 캡처 금지.
            var color = state.QuickColors[slot];
            swatch.Background = ToolbarTheme.Freeze(new SolidColorBrush(color));
            swatch.BorderThickness = new Thickness(ToolbarStateMap.QuickSwatchBorderThickness(color, state.CurrentColor));
            ring.BorderThickness = new Thickness(ToolbarStateMap.QuickSwatchOuterRingThickness(color, state.CurrentColor));
        }
    }

    public void UpdatePreviewDot(AppState state)
    {
        if (PreviewDot is null)
        {
            return;
        }
        double diameter = ToolbarStateMap.PreviewDotDiameter(state.Thickness);
        PreviewDot.Width = diameter;
        PreviewDot.Height = diameter;
        PreviewDot.Fill = ToolbarTheme.Freeze(new SolidColorBrush(state.CurrentColor));
    }
}
