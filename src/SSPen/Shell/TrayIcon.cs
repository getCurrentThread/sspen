using System.Drawing;
using System.Windows.Forms;
using SSPen.Annotation;

namespace SSPen.Shell;

/// <summary>
/// 트레이 아이콘 (WI-15, AC-22): 판서 켜기/끄기, 설정, 종료.
/// 구현 선택 (플랜 인터롭 인벤토리에서 WI-15로 이연된 결정): WinForms NotifyIcon —
/// 컨텍스트 메뉴·수명 관리가 안정적이고 플랜이 허용한 두 대안 중 유지보수가 단순한 쪽.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly AppState _state;
    private readonly ToolStripMenuItem _toggleItem;

    public TrayIcon(AppState state, Action openSettings, Action exitApp)
    {
        _state = state;

        _toggleItem = new ToolStripMenuItem();
        _toggleItem.Click += (_, _) => _state.SurfacesVisible = !_state.SurfacesVisible;

        var settingsItem = new ToolStripMenuItem(Strings.TraySettings);
        settingsItem.Click += (_, _) => openSettings();

        var exitItem = new ToolStripMenuItem(Strings.TrayExit);
        exitItem.Click += (_, _) => exitApp();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(exitItem);
        menu.Opening += (_, _) => RefreshToggleText();

        _icon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = Strings.AppName,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => _state.SurfacesVisible = !_state.SurfacesVisible;
        RefreshToggleText();
    }

    /// <summary>핫키 등록 실패 경고 (프리모템 3: 사용자 가시 한국어 경고).</summary>
    public void WarnHotkeyConflicts(IReadOnlyList<string> failed)
    {
        if (failed.Count > 0)
        {
            ShowWarning(Strings.HotkeyConflictWarning + string.Join(", ", failed));
        }
    }

    /// <summary>일반 경고 풍선 (클리너 B1: 클립보드 실패 등 사용자 가시 알림 경로).</summary>
    public void ShowWarning(string message) =>
        _icon.ShowBalloonTip(5000, Strings.AppName, message, ToolTipIcon.Warning);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private void RefreshToggleText() =>
        _toggleItem.Text = _state.SurfacesVisible ? Strings.TrayDisable : Strings.TrayEnable;

    /// <summary>
    /// 트레이 아이콘: exe와 **같은** 메타데이터 리소스(Assets/AppIcon.ico)를 쓴다.
    /// 런타임 그리기를 걱어낸 이유: 작업 표시줄·탐색기·트레이가 서로 다른 그림을 보이면
    /// 같은 앱으로 안 보인다. 또 트레이는 DPI에 따라 16/20/24px를 골라야 선명하다 —
    /// 단일 32px를 축소하면 글자 획이 병신된다.
    /// </summary>
    private static Icon CreateIcon()
    {
        var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("SSPen.AppIcon.ico")
            ?? throw new InvalidOperationException("내장 아이콘 리소스를 찾지 못했습니다: SSPen.AppIcon.ico");
        using (stream)
        {
            // 현재 DPI의 알림 영역 권장 크기에 가장 가까운 프레임을 ICO 안에서 고른다.
            var wanted = SystemInformation.SmallIconSize;
            return new Icon(stream, wanted);
        }
    }
}
