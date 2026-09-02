using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SSPen.Annotation;

namespace SSPen.Shell;

/// <summary>
/// 툴바 스트립 조립 (god file 분할, ARCH-11 후속): <see cref="ToolbarLayout"/> 스펙(순수 데이터, 51단계)을 Realize 루프가
/// 시각 트리로 실현한다 — MakeButton/BuildPreviewButton/BuildQuickColors/MakeBoardBadge — 산출물은 <see cref="ToolbarParts"/>.
/// 툴팁은 <see cref="ToolbarTooltips.Attach"/>가 만들고 <see cref="ToolbarFlyouts.RegisterTooltip"/>에 등록한다 (37단계).
/// </summary>
public static class ToolbarStripBuilder
{
    /// <summary>
    /// 스트립(로고+버튼 스택+플라이아웃 호스트)을 조립해 host UI(Grid)와 <see cref="ToolbarParts"/>를 반환한다.
    /// 항목 순서·버튼 속성은 <see cref="ToolbarLayout"/>이 들고, 클릭 동작(ActionFor)과 플라이아웃 종류→Popup(PopupFor) 연결은
    /// 여기 두 스위치가 확정한다 — 스펙에 델리게이트·WPF 객체를 싣지 않기 위해서다. 빠진 팔은 Build 시점에 던진다 (X7/R9).
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

        // 51단계: 클릭 동작은 스펙이 아니라 이 스위치가 잇는다 — 동작은 전부 Build 인자(state/actions/창 콜백)를 닫아야 하고,
        // Action은 비교할 수 없어 스펙에 실으면 스냅샷이 불가능하다. 빠진 팔은 던진다 (ToolbarStateMap.IsActive와 같은 X7/R9 트립와이어).
        Action ActionFor(ToolbarButtonId id) => id switch
        {
            ToolbarButtonId.Visibility => onToggleMenuCollapsed,
            ToolbarButtonId.ClickThrough => () => state.ClickThrough = !state.ClickThrough,
            ToolbarButtonId.Select => () => onSelectTool(ToolKind.Select),
            ToolbarButtonId.Shapes => onRotateShapes,
            ToolbarButtonId.Pen => onRotatePenGroup,
            ToolbarButtonId.Eraser => () => onSelectTool(ToolKind.Eraser),
            ToolbarButtonId.Fading => onToggleFading,
            ToolbarButtonId.Undo => actions.Undo,
            ToolbarButtonId.ClearAll => actions.ClearAll,
            ToolbarButtonId.Board => onRotateBoard,
            ToolbarButtonId.Capture => actions.StartCapture,
            ToolbarButtonId.Settings => actions.OpenSettings,
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "클릭 동작이 배선되지 않은 버튼 (X7/R9)"),
        };

        // 플라이아웃 종류 → Popup. 링크의 증인은 ToolbarStripBuilderTests.Build_FlyoutBearingEntries_AreThePlacementTargetsOfTheirFlyouts.
        Popup PopupFor(ToolbarFlyoutKind kind) => kind switch
        {
            ToolbarFlyoutKind.Shapes => flyouts.ShapesFlyout,
            ToolbarFlyoutKind.Pen => flyouts.PenFlyout,
            ToolbarFlyoutKind.Fading => flyouts.FadingFlyout,
            ToolbarFlyoutKind.Board => flyouts.BoardFlyout,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Popup이 배선되지 않은 플라이아웃 종류 (X7/R9)"),
        };

        Border MakeButton(ToolbarButtonEntry entry)
        {
            var onClick = ActionFor(entry.Id);
            var glyph = new TextBlock
            {
                Text = entry.Icon.Regular,
                FontFamily = Icons.Regular,
                FontSize = 20,
                Foreground = ToolbarTheme.IconBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var content = new Grid();
            content.Children.Add(glyph);

            System.Windows.Shapes.Polygon? flyoutMark = null;
            if (entry.HasFlyout)
            {
                // Epic Pen의 우하단 모서리 삼각형: 하위 메뉴 존재 어포던스.
                flyoutMark = ToolbarTheme.FlyoutMark();
                content.Children.Add(flyoutMark);
            }

            System.Windows.Shapes.Ellipse? badge = null;
            if (entry.BadgeGroup is not null)
            {
                // 색 사용 도구의 그룹별 색 배지 (Epic Pen의 펜 색 점 대응 — 사용자 조타: 도구별 개별 색).
                badge = new System.Windows.Shapes.Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(state.ColorOf(entry.BadgeGroup.Value)),
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
            ToolbarTooltips.Attach(actions, button, entry.Tooltip, entry.HotkeyId, flyouts.RegisterTooltip);
            button.MouseEnter += (_, _) =>
            {
                if (!entry.HasFlyout)
                {
                    // 플라이아웃 없는 버튼 호버 시 열린 서브메뉴 즉시 닫기 (빠릿한 전환).
                    flyouts.CloseFlyoutsExcept(null);
                }
                if (!ToolbarStateMap.IsActive(state, entry.Id, menuPanel is { Visibility: Visibility.Collapsed }))
                {
                    button.Background = ToolbarTheme.ButtonHoverBrush;
                }
            };
            button.MouseLeave += (_, _) => parts!.RefreshButton(state, entry.Id);
            button.MouseLeftButtonUp += (_, _) => onClick();
            buttons[entry.Id] = new ButtonParts(button, glyph, entry.Icon, flyoutMark, badge, entry.BadgeGroup);
            return button;
        }

        UIElement BuildPreviewButton(ToolbarPreviewEntry entry)
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
            ToolbarTooltips.Attach(actions, button, entry.Tooltip, entry.HotkeyId, flyouts.RegisterTooltip);
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
                ToolbarTooltips.Attach(actions, swatch, Strings.QuickColors, $"quickcolor:{slot + 1}", flyouts.RegisterTooltip);
                // 클릭 시점에 색을 읽는다 — 설정에서 바뀌면 바뀜 색이 추서된다.
                swatch.MouseLeftButtonUp += (_, _) => state.CurrentColor = state.QuickColors[slot];
                quickSwatches.Add((swatch, slot));
                grid.Children.Add(swatch);
            }

            grid.MouseWheel += (_, e) =>
            {
                // 휠 시점에 현재 칸을 찾는다 (없으면 0) — 판정은 ToolbarStateMap.CurrentQuickColorSlot.
                int currentIdx = ToolbarStateMap.CurrentQuickColorSlot(state.QuickColors, state.CurrentColor);
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

        // 도형/펜 버튼의 휠: 그룹 순환 (판정은 ToolbarStateMap.NextInCycle).
        void AttachToolCycleWheel(Border button, ToolKind[] cycle) =>
            button.MouseWheel += (_, e) =>
            {
                state.ActiveTool = ToolbarStateMap.NextInCycle(cycle, state.ActiveTool, e.Delta);
                e.Handled = true;
            };

        // 항목 하나를 시각 요소로 실현한다. 버튼의 문장 순서는 MakeButton → 휠 → 보드 배지 → 플라이아웃 연결 — 51단계 이전의
        // 조립 순서 그대로다 (MouseEnter 핸들러 순서: 호버 브러시 → HoverOpen; 보드 버튼 Grid 자식 순서: 글리프, 삼각형, 배지).
        UIElement Realize(ToolbarLayoutEntry entry)
        {
            switch (entry)
            {
                case ToolbarSeparatorEntry:
                    return ToolbarTheme.Separator();
                case ToolbarQuickColorsEntry:
                    return BuildQuickColors();
                case ToolbarPreviewEntry preview:
                    return BuildPreviewButton(preview);
                case ToolbarButtonEntry b:
                {
                    var button = MakeButton(b);
                    switch (b.Wheel)
                    {
                        case ToolbarWheel.None:
                            break;
                        case ToolbarWheel.ShapeCycle:
                            AttachToolCycleWheel(button, ToolbarStateMap.ShapeCycle);
                            break;
                        case ToolbarWheel.PenCycle:
                            AttachToolCycleWheel(button, ToolbarStateMap.PenCycle);
                            break;
                        case ToolbarWheel.FadingDuration:
                            button.MouseWheel += (_, e) =>
                            {
                                double nextSec = FadingDurations.StepByWheel(actions.FadingSeconds, e.Delta);
                                actions.SetFadingDuration(nextSec);
                                flyouts.HighlightFadingSelection();
                                e.Handled = true;
                            };
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(entry), entry, $"휠 동작 {b.Wheel}이 Realize에 배선되지 않았다 (X7/R9)");
                    }
                    if (b.Id == ToolbarButtonId.Board)
                    {
                        // 배지 부착은 데이터가 아니라 Id == Board로 잇는다 (ToolbarParts.RefreshButton과 같은 키) — ToolbarLayout의 보드 항목 주석 참조.
                        var boardBadge = MakeBoardBadge();
                        ((Grid)button.Child).Children.Add(boardBadge);
                        parts!.BoardBadge = boardBadge;
                    }
                    if (b.Flyout is { } kind)
                    {
                        var popup = PopupFor(kind);
                        popup.PlacementTarget = button;
                        flyouts.HoverOpen(button, popup);
                    }
                    return button;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(entry), entry, "Realize에 배선되지 않은 항목 종류 (X7/R9)");
            }
        }

        // 메뉴 패널을 먼저 만들어 ToolbarParts를 한 번만 구성하고, 이후 MakeButton 클로저가 이를 채운다.
        menuPanel = new StackPanel { Orientation = Orientation.Vertical };
        parts = new ToolbarParts(buttons, quickSwatches, menuPanel);

        var stack2 = new StackPanel { Orientation = Orientation.Vertical };

        // 눈 버튼은 접히는 메뉴 위에 남는다; 메뉴 항목 순서·그룹·구분선은 ToolbarLayout.Menu(순수 데이터, 51단계)가 든다.
        stack2.Children.Add(Realize(ToolbarLayout.Visibility));
        stack2.Children.Add(menuPanel);
        foreach (var entry in ToolbarLayout.Menu)
        {
            menuPanel.Children.Add(Realize(entry));
        }

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
