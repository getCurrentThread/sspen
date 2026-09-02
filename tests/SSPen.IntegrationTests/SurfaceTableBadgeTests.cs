using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using SSPen.Annotation;
using SSPen.Interop;
using Xunit;

namespace SSPen.IntegrationTests;

/// <summary>
/// 표(Table) 드래그 HUD 배지의 창 수준 증인 (리팩터링 18단계). 실제 <see cref="ContentSurfaceWindow"/>를 띄우고
/// 잉크 캔버스에 배지 <c>Border</c>가 드래그 중 정확히 1개, 마우스 업 뒤 0개임을 고정한다.
///
/// 24단계가 배지 소유권을 컨트롤러에서 창(<c>setTableBadge</c> 이음매)으로 옮길 때, 이음매 배선 누락은
/// 헤드리스 하네스로는 잡히지 않는다(하네스가 델리게이트를 세기만 한다). 이 증인이 그 구멍을 막는다.
/// <c>Application</c>은 만들지 않는다 (LD-4/R24).
/// </summary>
public class SurfaceTableBadgeTests
{
    private sealed record Rig(ContentSurfaceWindow Surface, AnnotationDocument Document, AppState State);

    private static Rig CreateSurface()
    {
        var monitor = MonitorTopology.Enumerate()[0];
        var state = new AppState { ActiveTool = ToolKind.Table };
        var document = new AnnotationDocument(monitor.DeviceName);
        var selection = new SelectionModel();
        selection.AttachTo(document);
        var ledger = new UndoLedger(
            e => document.Elements.Contains(e) ? document : null, selection);
        var surface = new ContentSurfaceWindow(
            monitor,
            state,
            document,
            ledger,
            new FadingInkController(new FadeSchedulerCore()),
            selection,
            e => document.Elements.Contains(e) ? document : null,
            _ => 1.0,
            (deltas, _) => ledger.RecordTransform(deltas),
            () => { },
            () => 0);
        return new Rig(surface, document, state);
    }

    [Fact]
    public void TableDrag_ShowsOneBadgeBorderInInkCanvas_RemovedOnPointerUp() => StaRunner.Run(() =>
    {
        var rig = CreateSurface();
        try
        {
            rig.Surface.Show();
            StaRunner.PumpMessages();

            var canvas = rig.Surface.InkCanvas;
            Assert.True(rig.Surface.Input.PointerDown(new Point(100, 100), shift: false));
            rig.Surface.Input.PointerMove(new Point(300, 250), shift: false, leftPressed: true);
            StaRunner.PumpMessages();

            Assert.Single(canvas.Children.OfType<Border>());
            Assert.Single(canvas.Children.OfType<Path>()); // 미리보기 격자

            rig.Surface.Input.PointerUp(new Point(300, 250), shift: false);
            StaRunner.PumpMessages();

            Assert.Empty(canvas.Children.OfType<Border>());
            var table = Assert.IsType<TableElement>(Assert.Single(rig.Document.Elements));
            Assert.Equal(3, table.Rows);
            // 커밋된 표의 시각물(Path)은 창이 ElementAdded로 붙인다 — 미리보기 Path는 사라지고 커밋 Path 1개만 남는다.
            Assert.Single(canvas.Children.OfType<Path>());
        }
        finally
        {
            rig.Surface.Close();
        }
    });

    [Fact]
    public void TableDrag_Cancelled_RemovesBadgeAndPreview() => StaRunner.Run(() =>
    {
        var rig = CreateSurface();
        try
        {
            rig.Surface.Show();
            StaRunner.PumpMessages();

            var canvas = rig.Surface.InkCanvas;
            rig.Surface.Input.PointerDown(new Point(100, 100), shift: false);
            rig.Surface.Input.PointerMove(new Point(300, 250), shift: false, leftPressed: true);
            StaRunner.PumpMessages();
            Assert.Single(canvas.Children.OfType<Border>());

            // 비인터랙티브 전환 → 창의 ApplyState가 CancelActiveInput을 동기로 부른다 (폐기).
            rig.State.ClickThrough = true;
            StaRunner.PumpMessages();

            Assert.Empty(canvas.Children.OfType<Border>());
            Assert.Empty(canvas.Children.OfType<Path>());
            Assert.Empty(rig.Document.Elements);
        }
        finally
        {
            rig.Surface.Close();
        }
    });
}
