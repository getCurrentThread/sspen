using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SSPen.Annotation;

namespace SSPen.Shell;

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
    internal readonly List<(Border Swatch, int Slot)> QuickSwatches;
    private readonly StackPanel? _menuPanel;

    internal System.Windows.Shapes.Ellipse? PreviewDot;
    internal Border? CurrentColorSwatch;
    internal Border? BoardBadge;

    internal ToolbarParts(
        Dictionary<ToolbarButtonId, ButtonParts> buttons,
        List<(Border Swatch, int Slot)> quickSwatches,
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
        parts.Root.Background = active ? ToolbarTheme.AccentBrush : Brushes.Transparent;
        parts.Glyph.Text = active ? icon.Filled : icon.Regular;
        parts.Glyph.FontFamily = active ? Icons.Filled : Icons.Regular;
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
        // 보드 그룹 버튼 (사용자 조타 14차): 활성 보드 색 스와치 배지.
        if (id == ToolbarButtonId.Board && BoardBadge is not null)
        {
            BoardBadge.Visibility = state.Board == BoardMode.None ? Visibility.Collapsed : Visibility.Visible;
            BoardBadge.Background = state.Board == BoardMode.Black ? Brushes.Black : Brushes.White;
        }
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
        foreach (var (swatch, slot) in QuickSwatches)
        {
            if (slot >= state.QuickColors.Count)
            {
                continue;
            }
            var color = state.QuickColors[slot];
            swatch.Background = ToolbarTheme.Freeze(new SolidColorBrush(color));
            swatch.BorderThickness = new Thickness(color == state.CurrentColor ? 2 : 0);
        }
    }

    public void UpdatePreviewDot(AppState state)
    {
        if (PreviewDot is null)
        {
            return;
        }
        double diameter = state.Thickness switch
        {
            ThicknessStep.XSmall => 8,
            ThicknessStep.Small => 11,
            ThicknessStep.Medium => 14,
            ThicknessStep.Large => 18,
            _ => 22,
        };
        PreviewDot.Width = diameter;
        PreviewDot.Height = diameter;
        PreviewDot.Fill = ToolbarTheme.Freeze(new SolidColorBrush(state.CurrentColor));
    }
}

/// <summary>
/// 툴바 스트립 조립 (god file 분할, ARCH-11 후속): BuildStrip/MakeButton/BuildPreviewButton/
/// BuildQuickColors/AttachTooltip/MakeBoardBadge — 시각 트리를 구성하고 산출물을 <see cref="ToolbarParts"/>로 반환한다.
/// </summary>
public static class ToolbarStripBuilder
{
    /// <summary>
    /// 스트립(로고+버튼 스택+플라이아웃 호스트)을 조립해 host UI(Grid)와 <see cref="ToolbarParts"/>를 반환한다.
    /// 버튼↔플라이아웃 연결(어느 버튼이 어느 플라이아웃을 여는지)은 여기서 확정한다.
    /// </summary>
    public static (UIElement Host, ToolbarTheme.LogoBadge Logo, ToolbarParts Parts) Build(
        AppState state,
        IShellActions actions,
        ToolbarFlyouts flyouts,
        Action onToggleMenuCollapsed,
        Action onRotateShapes,
        Action onRotatePenGroup,
        Action<ToolKind> onSelectTool,
        Action onToggleFading,
        Action onRotateBoard)
    {
        var buttons = new Dictionary<ToolbarButtonId, ButtonParts>();
        var quickSwatches = new List<(Border Swatch, int Slot)>();
        StackPanel? menuPanel = null;
        ToolbarParts? parts = null;

        Border MakeButton(
            ToolbarButtonId id,
            string tooltip,
            (string Regular, string Filled) icon,
            Action onClick,
            bool hasFlyout = false,
            ToolStyleGroup? badgeGroup = null,
            string? hotkeyId = null)
        {
            var glyph = new TextBlock
            {
                Text = icon.Regular,
                FontFamily = Icons.Regular,
                FontSize = 20,
                Foreground = ToolbarTheme.IconBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var content = new Grid();
            content.Children.Add(glyph);

            System.Windows.Shapes.Polygon? flyoutMark = null;
            if (hasFlyout)
            {
                // Epic Pen의 우하단 모서리 삼각형: 하위 메뉴 존재 어포던스.
                flyoutMark = ToolbarTheme.FlyoutMark();
                content.Children.Add(flyoutMark);
            }

            System.Windows.Shapes.Ellipse? badge = null;
            if (badgeGroup is not null)
            {
                // 색 사용 도구의 그룹별 색 배지 (Epic Pen의 펜 색 점 대응 — 사용자 조타: 도구별 개별 색).
                badge = new System.Windows.Shapes.Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(state.ColorOf(badgeGroup.Value)),
                    Stroke = Brushes.White,
                    StrokeThickness = 1,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 2, 0),
                    IsHitTestVisible = false,
                };
                content.Children.Add(badge);
            }

            var button = new Border
            {
                Width = 30,
                Height = 30,
                Background = Brushes.Transparent,
                Child = content,
            };
            ToolbarStripBuilder.AttachTooltip(actions, button, tooltip, hotkeyId, flyouts);
            button.MouseEnter += (_, _) =>
            {
                if (!hasFlyout)
                {
                    // 플라이아웃 없는 버튼 호버 시 열린 서브메뉴 즉시 닫기 (빠릿한 전환).
                    flyouts.CloseFlyoutsExcept(null);
                }
                if (!ToolbarStateMap.IsActive(state, id, menuPanel is { Visibility: Visibility.Collapsed }))
                {
                    button.Background = ToolbarTheme.ButtonHoverBrush;
                }
            };
            button.MouseLeave += (_, _) => parts!.RefreshButton(state, id);
            button.MouseLeftButtonUp += (_, _) => onClick();
            buttons[id] = new ButtonParts(button, glyph, icon, flyoutMark, badge, badgeGroup);
            return button;
        }

        UIElement BuildPreviewButton()
        {
            var previewDot = new System.Windows.Shapes.Ellipse
            {
                Fill = new SolidColorBrush(state.CurrentColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var host = new Grid();
            host.Children.Add(previewDot);
            var button = new Border
            {
                Width = 30,
                Height = 30,
                Background = Brushes.Transparent,
                Child = host,
            };
            ToolbarStripBuilder.AttachTooltip(actions, button, Strings.Thickness, "thickness-pair", flyouts);
            flyouts.ThicknessFlyout.PlacementTarget = button;
            flyouts.HoverOpen(button, flyouts.ThicknessFlyout);
            host.Children.Add(ToolbarTheme.FlyoutMark());
            button.MouseEnter += (_, _) => button.Background = ToolbarTheme.ButtonHoverBrush;
            button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
            button.MouseLeftButtonUp += (_, _) => flyouts.ToggleThicknessFlyout();
            button.MouseWheel += (_, e) =>
            {
                int direction = e.Delta > 0 ? 1 : -1;
                state.StepThickness(direction);
                flyouts.HighlightThicknessSelection();
                e.Handled = true;
            };
            parts!.PreviewDot = previewDot;
            parts.UpdatePreviewDot(state);
            return button;
        }

        UIElement BuildQuickColors()
        {
            // Epic Pen 하단 팔레트 재현 (사용자 조타: 기이함 수정): 여백 없는 플러시 모자이크가
            // 스트립 내부 폭(30px)을 꽉 채우고, 바로 아래 현재 색 대형 스와치. 잡다한 글리프 없음.
            var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 4, 0, 0) };
            quickSwatches.Clear();
            for (int i = 0; i < AppState.QuickColorCount; i++)
            {
                int slot = i;
                var swatch = new Border
                {
                    Height = 15,
                    Background = ToolbarTheme.Freeze(new SolidColorBrush(state.QuickColors[slot])),
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(0), // 선택 시만 흰 링 강조 (플러시 모자이크 유지).
                };
                ToolbarStripBuilder.AttachTooltip(actions, swatch, Strings.QuickColors, $"quickcolor:{slot + 1}", flyouts);
                // 클릭 시점에 색을 읽는다 — 설정에서 바뀌면 바뀜 색이 추서된다.
                swatch.MouseLeftButtonUp += (_, _) => state.CurrentColor = state.QuickColors[slot];
                quickSwatches.Add((swatch, slot));
                grid.Children.Add(swatch);
            }

            grid.MouseWheel += (_, e) =>
            {
                int currentIdx = -1;
                for (int i = 0; i < state.QuickColors.Count; i++)
                {
                    if (state.QuickColors[i] == state.CurrentColor)
                    {
                        currentIdx = i;
                        break;
                    }
                }
                if (currentIdx < 0) currentIdx = 0;
                int nextIdx = ToolbarStateMap.NextQuickColorSlotByWheel(currentIdx, e.Delta, state.QuickColors.Count);
                state.CurrentColor = state.QuickColors[nextIdx];
                e.Handled = true;
            };

            // 현재 색 대형 스와치: 전폭 플러시, 클릭 시 확장 팔레트. 하단 라운드 모서리를 피해 아래 여백.
            var currentColorSwatch = new Border
            {
                Height = 18,
                Margin = new Thickness(0, 2, 0, 5),
                Background = ToolbarTheme.Freeze(new SolidColorBrush(state.CurrentColor)),
                BorderThickness = new Thickness(0),
            };
            // 문자열 툴팁을 쓰지 않는 이유: WPF가 내부에서 ToolTip을 자동 생성해 버려
            // 수명 관리 대상으로 등록할 수 없다 — 툴바가 숨을 때 닫힐 수 없는 툴팁이 생긴다.
            var paletteTooltip = new ToolTip { Content = Strings.QuickColorsExtended };
            currentColorSwatch.ToolTip = paletteTooltip;
            flyouts.RegisterTooltip(paletteTooltip);
            currentColorSwatch.MouseLeftButtonUp += (_, _) =>
            {
                if (flyouts.PaletteFlyout.IsOpen) { flyouts.CloseFlyoutsExcept(null); } else { flyouts.OpenFlyout(flyouts.PaletteFlyout); }
            };
            flyouts.PaletteFlyout.PlacementTarget = currentColorSwatch;
            parts!.CurrentColorSwatch = currentColorSwatch;

            var stack = new StackPanel();
            stack.Children.Add(grid);
            stack.Children.Add(currentColorSwatch);
            return stack;
        }

        // 메뉴 패널을 먼저 만들어 ToolbarParts를 한 번만 구성하고, 이후 MakeButton 클로저가 이를 채운다.
        menuPanel = new StackPanel { Orientation = Orientation.Vertical };
        parts = new ToolbarParts(buttons, quickSwatches, menuPanel);

        var stack2 = new StackPanel { Orientation = Orientation.Vertical };

        // 눈 버튼: 메뉴 접기/펼치기 + 판서 동시 숨김/표시 (사용자 조타). Alt+Shift+1/트레이는 판서만 토글.
        stack2.Children.Add(MakeButton(ToolbarButtonId.Visibility, Strings.Visibility, Icons.Eye, onToggleMenuCollapsed));

        // 접히는 메뉴 영역: 눈 버튼 아래 전체.
        stack2.Children.Add(menuPanel);
        var menu = menuPanel;

        // 그룹 1: 클릭 통과.
        menu.Children.Add(MakeButton(ToolbarButtonId.ClickThrough, Strings.ClickThrough, Icons.Cursor, () => state.ClickThrough = !state.ClickThrough, hotkeyId: "clickthrough"));
        menu.Children.Add(ToolbarTheme.Separator());

        // 선택 도구 (SEL-15): 기존 획을 고르고 이동·크기·회전한다. 그리기가 아니라 조작이므로
        // 그리기 도구 그룹 앞에 두고, 색·굵기를 쓰지 않으므로 색 배지도 없다 (SEL-5).
        menu.Children.Add(MakeButton(ToolbarButtonId.Select, Strings.Select, Icons.Select, () => onSelectTool(ToolKind.Select), hotkeyId: "select"));

        // 그룹 2: 그리기 도구 (도형·펜·형광펜은 각자 그룹 색 배지, 플라이아웃 어포던스 삼각형).
        var shapesButton = MakeButton(ToolbarButtonId.Shapes, Strings.Shapes, Icons.Shapes, onRotateShapes, hasFlyout: true, badgeGroup: ToolStyleGroup.Shape);
        shapesButton.MouseWheel += (_, e) =>
        {
            state.ActiveTool = ToolbarStateMap.NextInCycle(ToolbarStateMap.ShapeCycle, state.ActiveTool, e.Delta);
            e.Handled = true;
        };
        flyouts.ShapesFlyout.PlacementTarget = shapesButton;
        flyouts.HoverOpen(shapesButton, flyouts.ShapesFlyout);
        menu.Children.Add(shapesButton);

        // 펜 그룹 버튼 (사용자 조타: 펜·형광펜·텍스트를 한 그룹으로 — Epic Pen 펜+A 플라이아웃 대응).
        var penButton = MakeButton(ToolbarButtonId.Pen, Strings.Pen, Icons.Pen, onRotatePenGroup, hasFlyout: true, badgeGroup: ToolStyleGroup.Pen, hotkeyId: "pen");
        penButton.MouseWheel += (_, e) =>
        {
            state.ActiveTool = ToolbarStateMap.NextInCycle(ToolbarStateMap.PenCycle, state.ActiveTool, e.Delta);
            e.Handled = true;
        };
        flyouts.PenFlyout.PlacementTarget = penButton;
        flyouts.HoverOpen(penButton, flyouts.PenFlyout);
        menu.Children.Add(penButton);
        menu.Children.Add(MakeButton(ToolbarButtonId.Eraser, Strings.Eraser, Icons.Eraser, () => onSelectTool(ToolKind.Eraser), hotkeyId: "eraser"));

        // 페이딩 잉크 (사용자 요청 17차): 도구가 아니라 그리기 도구에 얹히는 토글.
        // 색 배지를 뗀 이유: 이제 자체 색이 없다 — 획 색은 현재 도구(펜·형광펜·도형)의 색을 따른다.
        // 지속 시간은 호버 플라이아웃에서 고른다.
        var fadingButton = MakeButton(
            ToolbarButtonId.Fading, Strings.HotkeyFadingInk, Icons.Timer,
            onToggleFading,
            hasFlyout: true, hotkeyId: "fading");
        fadingButton.MouseWheel += (_, e) =>
        {
            double nextSec = FadingDurations.StepByWheel(actions.FadingSeconds, e.Delta);
            actions.SetFadingDuration(nextSec);
            flyouts.HighlightFadingSelection();
            e.Handled = true;
        };
        flyouts.FadingFlyout.PlacementTarget = fadingButton;
        flyouts.HoverOpen(fadingButton, flyouts.FadingFlyout);
        menu.Children.Add(fadingButton);

        // 현재 색 + 굵기 미리보기 (Epic Pen의 채워진 원 대응): 활성 그룹 기준, 호버 시 굵기 선택기.
        menu.Children.Add(BuildPreviewButton());
        menu.Children.Add(ToolbarTheme.Separator());

        // 그룹 3: 편집.
        menu.Children.Add(MakeButton(ToolbarButtonId.Undo, Strings.Undo, Icons.ArrowUndo, actions.Undo, hotkeyId: "undo"));
        menu.Children.Add(MakeButton(ToolbarButtonId.ClearAll, Strings.ClearAll, Icons.Delete, actions.ClearAll, hotkeyId: "clear"));
        menu.Children.Add(ToolbarTheme.Separator());

        // 그룹 4: 보드/캡처/설정. 보드 그룹 버튼 (사용자 조타 14차): 클릭 = 없음→화이트→블랙 로테이션,
        // 호버 플라이아웃 = 직접 선택, 활성 보드는 우상단 스와치 배지로 표시.
        var boardButton = MakeButton(ToolbarButtonId.Board, Strings.Board, Icons.Whiteboard, onRotateBoard, hasFlyout: true, hotkeyId: "whiteboard");
        var boardBadge = ToolbarStripBuilder.MakeBoardBadge();
        ((Grid)boardButton.Child).Children.Add(boardBadge);
        parts.BoardBadge = boardBadge;
        flyouts.BoardFlyout.PlacementTarget = boardButton;
        flyouts.HoverOpen(boardButton, flyouts.BoardFlyout);
        menu.Children.Add(boardButton);
        menu.Children.Add(MakeButton(ToolbarButtonId.Capture, Strings.Capture, Icons.Camera, actions.StartCapture, hotkeyId: "capture"));
        menu.Children.Add(MakeButton(ToolbarButtonId.Settings, Strings.Settings, Icons.Settings, actions.OpenSettings));
        menu.Children.Add(ToolbarTheme.Separator());

        // 그룹 5: 퀵컬러 6칸 (2열 x 3행) + 현재 색 대형 스와치 + 빠른 색상 확장.
        menu.Children.Add(BuildQuickColors());

        var strip = new Border
        {
            Background = ToolbarTheme.StripBrush,
            BorderBrush = ToolbarTheme.StripBorderBrush,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            Child = stack2,
            Width = 34,
        };

        // 로고는 스트립 밖, 투명 배경 위에 띄운다 (사용자 조타: 원형 아이콘 뒤 배경 제거 — Epic Pen 닙 배치).
        var logo = new ToolbarTheme.LogoBadge();
        // 좌상단 고정 (사용자 조타: 표시 접기/펼치기 때 미묘한 오프셋 틀어짐 수정):
        // 재측정으로 창 폭이 변해도 스트립이 가운데로 재배치되지 않게 좌측 정렬.
        var outer = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        outer.Children.Add(logo);
        outer.Children.Add(strip);

        flyouts.BuildAllFlyouts();

        var host = new Grid();
        host.Children.Add(outer);
        foreach (var popup in flyouts.AllFlyouts)
        {
            host.Children.Add(popup);
        }

        return (host, logo, parts);
    }

    /// <summary>
    /// 이름 + 유효 단축키 2줄 툴팁 (Epic Pen 대응: "선 도구" / "(ctrl + shift + L)").
    /// 생성한 툴팁은 <paramref name="flyouts"/>에 등록해 툴바가 숨을 때 함께 닫힐 수 있게 한다.
    /// </summary>
    internal static void AttachTooltip(
        IShellActions actions, Border button, string name, string? hotkeyId, ToolbarFlyouts? flyouts = null)
    {
        var title = new TextBlock
        {
            Text = name,
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
        button.ToolTip = tooltip;
        flyouts?.RegisterTooltip(tooltip);
        ToolTipService.SetInitialShowDelay(button, 300);
        // 사용자 조타: 툴팁이 옆 메뉴/플라이아웃을 가리지 않게 버튼 아래에 표시.
        ToolTipService.SetPlacement(button, PlacementMode.Bottom);
        if (combo is not null && hotkeyId is not null)
        {
            // 열릴 때마다 현재 유효 조합으로 갱신 (재지정 즉시 반영).
            button.ToolTipOpening += (_, _) =>
            {
                string? label = actions.HotkeyLabel(hotkeyId);
                combo.Text = label is null ? string.Empty : $"({label})";
                combo.Visibility = label is null ? Visibility.Collapsed : Visibility.Visible;
            };
        }
    }

    /// <summary>보드 버튼 우상단 활성 보드 스와치 배지 (없음이면 숨김).</summary>
    internal static Border MakeBoardBadge() => new()
    {
        Width = 9,
        Height = 9,
        CornerRadius = new CornerRadius(2),
        Background = Brushes.White,
        BorderBrush = Brushes.White,
        BorderThickness = new Thickness(1),
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 2, 2, 0),
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
    };
}
