using System.Windows;
using SSPen.Annotation;
using SSPen.Interop;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="SurfaceProjection"/>의 증인 (32단계, CRIT-06, AGENTS L17). 이관 후보의 사각형은 작업 영역이지 모니터 경계가 아니다.
/// 64단계(A2-4)부터는 이관 판정의 입력 쪽(드롭 지점 <see cref="SurfaceProjection.DropPointToVirtual"/>)도 여기서 본다 — 원장·이관
/// 테스트는 이미 물리로 환산된 드롭 값을 받으므로, 원점을 Bounds로 바꾸거나 오프셋을 DPI로 나누는 결함을 이 파일만 잡는다.
/// </summary>
public class SurfaceProjectionTests
{
    [Fact]
    public void ToTransferSurface_UsesWorkAreaNotBounds()
    {
        var monitor = new MonitorSurfaceInfo(
            @"\\.\DISPLAY9", new PhysicalRect(-1920, 0, 1920, 1080), new PhysicalRect(-1920, 0, 1920, 1040), IsPrimary: false);
        var document = new AnnotationDocument(monitor.DeviceName);

        var (projectedDocument, rect, dpi) = SurfaceProjection.ToTransferSurface(document, monitor, 1.5);

        Assert.Same(document, projectedDocument);
        Assert.Equal(monitor.WorkArea, rect);
        Assert.NotEqual(monitor.Bounds, rect);
        Assert.Equal(1.5, dpi);
    }

    [Fact]
    public void DropPointToVirtual_UsesWorkAreaOrigin_NotBounds()
    {
        // 작업 표시줄이 위에 있는 모니터: Bounds와 WorkArea의 원점이 40px 다르다.
        var monitor = new MonitorSurfaceInfo(
            @"\\.\DISPLAY1", new PhysicalRect(0, 0, 1920, 1080), new PhysicalRect(0, 40, 1920, 1040), IsPrimary: true);

        Assert.Equal((10, 50), SurfaceProjection.DropPointToVirtual(monitor, new Point(10, 10), 1.0));
        Assert.Equal((0, 40), SurfaceProjection.DropPointToVirtual(monitor, new Point(0, 0), 1.0));
    }

    /// <summary>순서 계약: DPI를 곱해 반올림한 <b>뒤</b> 정수 원점을 더한다 (원점은 DPI로 나누지 않는다).</summary>
    [Fact]
    public void DropPointToVirtual_NegativeOrigin150Percent_RoundsThenOffsets()
    {
        var monitor = new MonitorSurfaceInfo(
            @"\\.\DISPLAY9", new PhysicalRect(-1920, 0, 1920, 1080), new PhysicalRect(-1920, 0, 1920, 1080), IsPrimary: false);

        Assert.Equal((-1770, 300), SurfaceProjection.DropPointToVirtual(monitor, new Point(100, 200), 1.5));
        Assert.Equal((-1770, 75), SurfaceProjection.DropPointToVirtual(monitor, new Point(100, 50), 1.5));
        // -0.4×1.5 = -0.6 → -1, 100.3×1.5 = 150.45 → 150.
        Assert.Equal((-1921, 150), SurfaceProjection.DropPointToVirtual(monitor, new Point(-0.4, 100.3), 1.5));
    }

    /// <summary>
    /// 왕복 증인: 서피스 안의 논리점을 드롭 지점으로 바꿔 이관 후보 목록(<see cref="SurfaceProjection.ToTransferSurface"/>)에 넣으면
    /// 그 서피스 자신이 나온다 — 두 함수가 같은 사각형을 쓴다는 계약 (AGENTS L17). 음수 원점·혼합 DPI·위쪽 작업 표시줄을 한 토폴로지에 둔다.
    /// </summary>
    [Fact]
    public void DropPointToVirtual_InsideSurface_ResolvesToSameTransferSurface()
    {
        (MonitorSurfaceInfo Monitor, double Dpi)[] topology =
        [
            (new(@"\\.\DISPLAY2", new PhysicalRect(-1920, 0, 1920, 1080), new PhysicalRect(-1920, 0, 1920, 1040), IsPrimary: false), 1.0),
            (new(@"\\.\DISPLAY1", new PhysicalRect(0, 0, 1920, 1080), new PhysicalRect(0, 40, 1920, 1040), IsPrimary: true), 1.5),
            (new(@"\\.\DISPLAY3", new PhysicalRect(1920, 0, 1920, 1080), new PhysicalRect(1920, 0, 1920, 1040), IsPrimary: false), 1.25),
        ];
        var documents = topology.Select(t => new AnnotationDocument(t.Monitor.DeviceName)).ToArray();
        var candidates = topology.Select((t, i) => SurfaceProjection.ToTransferSurface(documents[i], t.Monitor, t.Dpi)).ToList();

        for (int i = 0; i < topology.Length; i++)
        {
            var (monitor, dpi) = topology[i];
            // 서피스 논리 크기 = 작업 영역 / DPI. 네 모서리 안쪽과 중앙.
            double width = monitor.WorkArea.Width / dpi;
            double height = monitor.WorkArea.Height / dpi;
            Point[] inside = [new(0, 0), new(width - 1, 0), new(0, height - 1), new(width - 1, height - 1), new(width / 2, height / 2)];

            foreach (var logical in inside)
            {
                var (x, y) = SurfaceProjection.DropPointToVirtual(monitor, logical, dpi);
                var target = SelectionTransfer.ResolveTarget(candidates, x, y);

                Assert.NotNull(target);
                Assert.Same(documents[i], target!.Value.Document);
            }
        }
    }
}
