namespace SSPen.Diagnostics;

/// <summary>획 하나의 채널 요약 — <see cref="StrokeChannelTally.End"/>가 샘플 대상 획에서만 돌려준다.</summary>
/// <param name="Stroke">요약 대상 획의 번호 (1부터, 스타일러스 흔적이 있는 획만 센다).</param>
/// <param name="StylusMoves">스타일러스 채널이 주입한 이동 이벤트(배치) 수.</param>
/// <param name="StylusPackets">그 배치들의 패킷 합.</param>
/// <param name="PromotedSkipped">승격 마우스 채널이 정책대로 건너뛴 이동 이벤트 수.</param>
internal readonly record struct StrokeChannelSummary(int Stroke, int StylusMoves, int StylusPackets, int PromotedSkipped);

/// <summary>
/// 획 단위 스타일러스 채널 계수의 순수 코어 (64단계, A2-5). <see cref="StylusProbe"/>는 이 인스턴스 하나에 위임하고
/// 로그 부작용만 쥔다 — 그래서 AGENTS L40의 재발 탐지 규칙(처음 5획, 그 뒤 50획마다)이 헤드리스 증인(<c>StrokeChannelTallyTests</c>)을 갖는다.
///
/// 판정 네 가지: 마우스 전용 획(패킷·건너뜀 모두 0)은 번호를 올리지 않고 요약도 없다. 업 없이 끝난 획의 계수는 다음
/// <see cref="Begin"/>에서 버린다. 패킷 0개에 건너뜀만 있는 획(스타일러스 채널이 죽었다는 신호)도 요약한다.
/// 요약은 <see cref="SummaryAlwaysFirst"/>번째 획까지는 매번, 그 뒤로는 <see cref="SummaryEvery"/>의 배수 번째 획에서만 낸다.
/// UI 스레드 전용이다 (잠금 없음).
/// </summary>
internal sealed class StrokeChannelTally
{
    /// <summary>처음 이만큼의 획은 매번, 그 뒤로는 <see cref="SummaryEvery"/>획마다 요약한다.</summary>
    internal const int SummaryAlwaysFirst = 5;
    internal const int SummaryEvery = 50;

    private int _stylusPackets;
    private int _stylusMoves;
    private int _promotedSkipped;
    private int _strokesSummarized;

    /// <summary>
    /// 펜/마우스 다운. 계수를 비운다 — 요약은 펜 업에서만 나는데, 캡처 핫키·클릭 통과 전환으로 획이 업 없이 끝나면
    /// 전 획의 계수가 다음 획 요약에 섞이므로 시작에서도 비워야 획 단위가 보장된다.
    /// </summary>
    public void Begin() => Reset();

    /// <summary>스타일러스 채널이 <c>PointerMove</c>에 주입한 패킷 배치 1건 (크기 <paramref name="packets"/>).</summary>
    public void CountStylusBatch(int packets)
    {
        _stylusMoves++;
        _stylusPackets += packets;
    }

    /// <summary>승격 마우스 채널이 (정책대로) 주입을 건너뛴 이동 이벤트 1건.</summary>
    public void CountPromotedSkipped() => _promotedSkipped++;

    /// <summary>
    /// 펜 업. 계수를 비우고, 이 획에 스타일러스 흔적(패킷 또는 건너뜀)이 있었으면 번호를 올린다. 요약은 샘플 대상 번호일 때만 돌려준다.
    /// 마우스 전용 획은 번호를 올리지 않고 <c>null</c>이다.
    /// </summary>
    public StrokeChannelSummary? End()
    {
        int packets = _stylusPackets;
        int moves = _stylusMoves;
        int skipped = _promotedSkipped;
        Reset();
        if (packets == 0 && skipped == 0)
        {
            return null;
        }
        _strokesSummarized++;
        return _strokesSummarized <= SummaryAlwaysFirst || _strokesSummarized % SummaryEvery == 0
            ? new StrokeChannelSummary(_strokesSummarized, moves, packets, skipped)
            : null;
    }

    private void Reset()
    {
        _stylusPackets = 0;
        _stylusMoves = 0;
        _promotedSkipped = 0;
    }
}
