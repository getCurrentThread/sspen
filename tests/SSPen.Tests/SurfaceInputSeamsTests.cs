using System.Runtime.CompilerServices;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="SurfaceInputSeams"/> 형태의 트립와이어 (27단계, R5/R7). SurfaceBounds·IdleScheduler는 창이 유일 소유자라
/// 충실한 프로덕션 기본값이 없다 → <c>required</c>. Now는 프로덕션 값(UtcNow)을 충실히 감싸므로 기본값이 정당하다.
/// 편의상 기본값을 붙이는 리팩터가 들어오면 여기서 빨갛다 — Rect.Empty는 '경계 없음'이 아니라 다른 코드 경로다 (12단계).
/// </summary>
public class SurfaceInputSeamsTests
{
    [Theory]
    [InlineData(nameof(SurfaceInputSeams.SurfaceBounds), true)]
    [InlineData(nameof(SurfaceInputSeams.IdleScheduler), true)]
    [InlineData(nameof(SurfaceInputSeams.Now), false)]
    public void RequiredMembers_AreExactlyTheOnesWithoutAFaithfulDefault_ByReflection(string property, bool required)
    {
        var info = typeof(SurfaceInputSeams).GetProperty(property)!;

        Assert.Equal(required, info.GetCustomAttributes(typeof(RequiredMemberAttribute), inherit: false).Length == 1);
    }

    [Fact]
    public void Seams_HasExactlyThreeProperties()
    {
        var names = typeof(SurfaceInputSeams).GetProperties()
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name).OrderBy(n => n).ToArray();

        Assert.Equal(["IdleScheduler", "Now", "SurfaceBounds"], names);
    }

    [Fact]
    public void ISurfaceHost_IsTheMinimalHandshakeSurface()
    {
        var names = typeof(ISurfaceHost).GetMethods().Select(m => m.Name).OrderBy(n => n).ToArray();

        // ARCH-2 NOACTIVATE 핸드셰이크 + ARCH-6 캡처 + DPI 조회 — 그 외 창 조작은 컨트롤러가 알 필요가 없다.
        Assert.Equal(["ActivateWindow", "CaptureMouse", "GetDpi", "ReleaseMouseCapture", "SetNoActivate"], names);
    }
}
