using SSPen.Annotation;
using SSPen.Interop;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="SurfaceProjection"/>의 증인 (32단계, CRIT-06, AGENTS L17). 이관 후보의 사각형은 작업 영역이지 모니터 경계가 아니다.
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
}
