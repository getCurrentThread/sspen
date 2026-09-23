using SSPen.Diagnostics;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 획 단위 스타일러스 채널 계수의 증인 (64단계, A2-5). 54단계가 붙인 재발 탐지기(AGENTS L40: 처음 5획, 그 뒤 50획마다)는
/// <c>StylusProbe</c>의 정적 상태와 <c>Log</c> 부작용에 묶여 증인이 없었다 — 판정을 <see cref="StrokeChannelTally"/>로 빼서 여기서 잠근다.
/// 순수 정수 계수라 xUnit 기본 MTA 쓰레드에서 돈다.
/// </summary>
public class StrokeChannelTallyTests
{
    /// <summary>스타일러스 배치 하나짜리 획 (다운 → 배치 → 업).</summary>
    private static StrokeChannelSummary? StylusStroke(StrokeChannelTally tally, int packets = 3)
    {
        tally.Begin();
        tally.CountStylusBatch(packets);
        return tally.End();
    }

    [Fact]
    public void End_MouseOnlyStroke_ReturnsNull_AndDoesNotAdvanceNumbering()
    {
        var tally = new StrokeChannelTally();

        tally.Begin();
        Assert.Null(tally.End());

        var first = StylusStroke(tally);
        Assert.NotNull(first);
        Assert.Equal(1, first!.Value.Stroke);
    }

    [Fact]
    public void End_SamplesFirstFiveThenEvery50th()
    {
        var tally = new StrokeChannelTally();

        var summarized = Enumerable.Range(0, 100)
            .Select(_ => StylusStroke(tally))
            .Where(s => s is not null)
            .Select(s => s!.Value.Stroke)
            .ToArray();

        Assert.Equal([1, 2, 3, 4, 5, 50, 100], summarized);
    }

    /// <summary>업 없이 끝난 획(캡처 핫키·클릭 통과 전환)의 계수는 다음 다운에서 버려진다 — 다음 획 요약에 섞이지 않는다.</summary>
    [Fact]
    public void Begin_DiscardsCountsOfAnUplessStroke()
    {
        var tally = new StrokeChannelTally();
        tally.Begin();
        tally.CountStylusBatch(40);
        tally.CountPromotedSkipped();

        tally.Begin(); // 업 없이 새 획
        tally.CountStylusBatch(2);
        var summary = tally.End();

        Assert.Equal(new StrokeChannelSummary(1, StylusMoves: 1, StylusPackets: 2, PromotedSkipped: 0), summary);
    }

    /// <summary>패킷 0개에 건너뜀만 있는 획은 "스타일러스 채널이 죽었다"는 신호라 요약한다 (마우스 전용 획과 다르다).</summary>
    [Fact]
    public void End_PromotedSkipsOnly_IsSummarized()
    {
        var tally = new StrokeChannelTally();
        tally.Begin();
        tally.CountPromotedSkipped();
        tally.CountPromotedSkipped();
        tally.CountPromotedSkipped();

        var summary = tally.End();

        Assert.NotNull(summary);
        Assert.Equal(0, summary!.Value.StylusPackets);
        Assert.Equal(3, summary.Value.PromotedSkipped);
    }

    [Fact]
    public void End_ReportsMovesAndPacketsOfThisStrokeOnly()
    {
        var tally = new StrokeChannelTally();
        tally.Begin();
        tally.CountStylusBatch(4);
        tally.CountStylusBatch(6);
        tally.CountPromotedSkipped();
        Assert.Equal(new StrokeChannelSummary(1, StylusMoves: 2, StylusPackets: 10, PromotedSkipped: 1), tally.End());

        // End가 계수를 비웠으므로 Begin 없이 이어져도 앞 획의 값이 섞이지 않는다.
        tally.CountStylusBatch(5);
        Assert.Equal(new StrokeChannelSummary(2, StylusMoves: 1, StylusPackets: 5, PromotedSkipped: 0), tally.End());
    }
}
