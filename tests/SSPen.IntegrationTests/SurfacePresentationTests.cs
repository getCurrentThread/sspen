using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SSPen.Annotation;
using SSPen.Interop;
using Xunit;

namespace SSPen.IntegrationTests;

/// <summary>
/// 서피스 상태→표현의 창 수준 특성화 (44단계 선행, ARCH-1/D4). 실제 창에서 exstyle(클릭 통과)·히트테스트 배경·커서가
/// 상태 조합에 따라 함께 움직임을 고정한다 — 44단계가 판정을 SurfacePresentationRules로 뺀 뒤에도 이 증인이 초록이어야 한다.
/// <c>Application</c>은 만들지 않는다 (LD-4/R24).
/// </summary>
public class SurfacePresentationTests
{
    private sealed record Rig(ContentSurfaceWindow Surface, AppState State);

    private static Rig CreateSurface(ToolKind tool)
    {
        var monitor = MonitorTopology.Enumerate()[0];
        var state = new AppState { ActiveTool = tool };
        var document = new AnnotationDocument(monitor.DeviceName);
        var selection = new SelectionModel();
        selection.AttachTo(document);
        var ledger = new UndoLedger(e => document.Elements.Contains(e) ? document : null, selection);
        var surface = new ContentSurfaceWindow(
            monitor, state, document, ledger, new FadingInkController(new FadeSchedulerCore()), selection,
            e => document.Elements.Contains(e) ? document : null, _ => 1.0,
            (deltas, _) => ledger.RecordTransform(deltas), () => { }, () => 0, (r, c) => $"{r}x{c}");
        return new Rig(surface, state);
    }

    private static Grid Root(ContentSurfaceWindow surface) => (Grid)surface.Content;

    [Fact]
    public void ActiveTool_InteractiveSurface_HasHitTestBackground_NoClickThrough_ToolCursor() => StaRunner.Run(() =>
    {
        var rig = CreateSurface(ToolKind.Pen);
        try
        {
            rig.Surface.Show();
            StaRunner.PumpMessages();

            Assert.False(WindowStyling.IsClickThrough(rig.Surface.Hwnd));
            Assert.NotNull(Root(rig.Surface).Background);
            Assert.True(Root(rig.Surface).IsHitTestVisible);
            Assert.Equal(Cursors.Pen, rig.Surface.Cursor);
        }
        finally
        {
            rig.Surface.Close();
        }
    });

    [Fact]
    public void ClickThrough_State_TurnsSurfaceClickThrough_NoBackground_ArrowCursor() => StaRunner.Run(() =>
    {
        var rig = CreateSurface(ToolKind.Pen);
        try
        {
            rig.Surface.Show();
            StaRunner.PumpMessages();

            rig.State.ClickThrough = true;
            StaRunner.PumpMessages();

            Assert.True(WindowStyling.IsClickThrough(rig.Surface.Hwnd));
            Assert.Null(Root(rig.Surface).Background);
            Assert.False(Root(rig.Surface).IsHitTestVisible);
            Assert.Equal(Cursors.Arrow, rig.Surface.Cursor);
        }
        finally
        {
            rig.Surface.Close();
        }
    });

    [Fact]
    public void SetSuspended_True_IsClickThroughWithArrow_AndResumeRestoresTool() => StaRunner.Run(() =>
    {
        var rig = CreateSurface(ToolKind.Text);
        try
        {
            rig.Surface.Show();
            StaRunner.PumpMessages();
            Assert.Equal(Cursors.IBeam, rig.Surface.Cursor);

            rig.Surface.SetSuspended(true);
            StaRunner.PumpMessages();
            Assert.True(WindowStyling.IsClickThrough(rig.Surface.Hwnd));
            Assert.Null(Root(rig.Surface).Background);
            Assert.Equal(Cursors.Arrow, rig.Surface.Cursor);

            rig.Surface.SetSuspended(false);
            StaRunner.PumpMessages();
            Assert.False(WindowStyling.IsClickThrough(rig.Surface.Hwnd));
            Assert.NotNull(Root(rig.Surface).Background);
            Assert.Equal(Cursors.IBeam, rig.Surface.Cursor);
        }
        finally
        {
            rig.Surface.Close();
        }
    });
}
