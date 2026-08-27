using SSPen.Capture;
using Xunit;

namespace SSPen.Tests;

/// <summary>WI-12: 파일명 포매터 — 정확한 패턴, 로컬 시간, 동일 초 충돌 접미사 (CRIT-8 오라클).</summary>
public class CaptureFileNamingTests
{
    private static readonly DateTime Sample = new(2026, 8, 25, 21, 40, 5);

    [Fact]
    public void BaseFileName_MatchesSpecPattern()
    {
        Assert.Equal("SSPen_20260825_214005.png", CaptureFileNaming.BaseFileName(Sample));
    }

    [Fact]
    public void BaseFileName_PadsSingleDigits()
    {
        var early = new DateTime(2026, 1, 2, 3, 4, 5);
        Assert.Equal("SSPen_20260102_030405.png", CaptureFileNaming.BaseFileName(early));
    }

    [Fact]
    public void ResolveFileName_NoCollision_UsesBaseName()
    {
        string name = CaptureFileNaming.ResolveFileName(Sample, _ => false);
        Assert.Equal("SSPen_20260825_214005.png", name);
    }

    [Fact]
    public void ResolveFileName_SameSecondCollision_AppendsSuffix2()
    {
        var taken = new HashSet<string> { "SSPen_20260825_214005.png" };
        string name = CaptureFileNaming.ResolveFileName(Sample, taken.Contains);
        Assert.Equal("SSPen_20260825_214005_2.png", name);
    }

    [Fact]
    public void ResolveFileName_MultipleCollisions_IncrementsSuffix()
    {
        var taken = new HashSet<string>
        {
            "SSPen_20260825_214005.png",
            "SSPen_20260825_214005_2.png",
            "SSPen_20260825_214005_3.png",
        };
        string name = CaptureFileNaming.ResolveFileName(Sample, taken.Contains);
        Assert.Equal("SSPen_20260825_214005_4.png", name);
    }
}
