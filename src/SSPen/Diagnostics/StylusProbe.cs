using System.Windows.Input;

namespace SSPen.Diagnostics;

/// <summary>
/// 펜(스타일러스) 입력 도달 여부 진단 발자국 (R8 1단계).
///
/// 왜 구현보다 측정이 먼저인가: 서피스 창은 <c>AllowsTransparency=true</c> + <c>WS_EX_LAYERED</c> +
/// <c>WS_EX_TOOLWINDOW</c> + <c>WS_EX_NOACTIVATE</c> + <c>ShowActivated=false</c>라는, 스타일러스
/// 입력이 막히는 것으로 알려진 조합이다. 같은 스타일 조합의 빈 창으로 확인한 결과 WPF가
/// <b>PenContext는 정상 등록</b>했고(네이티브 통신 핸들 non-zero) 이 머신의 와콤은 지우개 꼭지를
/// <c>Inverted=true</c>인 <b>별도 커서</b>로 보고했다. 하지만 <b>등록되었다</b>는 것과
/// <b>실제 패킷이 앱까지 온다</b>는 것은 별개이고, 합성 포인터 주입은 WISP 파이프라인에 도달하지
/// 못해 검증에 쓸 수 없었다. 그래서 실물 펜 1획이 남기는 로그로 채널을 확정한다.
///
/// 로그는 <b>채널·장치·뒤집힘 조합마다 최초 1회</b>만 남는다 — 펜 이동은 초당 수십~수백 건이라
/// 무조건 기록하면 로그가 그것만으로 가득 찬다.
///
/// 54단계(자유선 부풀음 회귀) 뒤 추가: 획 하나당 <b>채널별 패킷 수</b>를 세어 펜 업 때 요약한다.
/// 두 채널이 같은 패킷을 주입하던 결함은 채널별 최초 1회 로그로는 보이지 않았다 — 같은 획에서 스타일러스 채널과
/// 승격 채널이 <b>둘 다</b> 주입했다는 사실은 개수 비교로만 드러난다. 요약은 처음 몇 획과 그 뒤 드문드문만 남긴다.
/// </summary>
internal static class StylusProbe
{
    private static readonly HashSet<string> Seen = [];
    private static bool _tabletsLogged;

    // 획 단위 채널 계수 (UI 스레드 전용).
    private static int _strokeStylusPackets;
    private static int _strokeStylusMoves;
    private static int _strokePromotedMovesSkipped;
    private static int _strokesSummarized;

    /// <summary>처음 이만큼의 획은 매번, 그 뒤로는 <see cref="SummaryEvery"/>획마다 요약한다.</summary>
    internal const int SummaryAlwaysFirst = 5;
    internal const int SummaryEvery = 50;

    /// <summary>시동 시 1회: 태블릿과 커서 목록. 'Eraser' 항목의 <c>Inverted=True</c>가 R8의 판별 신호다.</summary>
    internal static void LogTablets()
    {
        if (_tabletsLogged)
        {
            return;
        }
        _tabletsLogged = true;
        try
        {
            var tablets = Tablet.TabletDevices;
            Log.Info($"[R8] 태블릿 {tablets.Count}대");
            foreach (TabletDevice tablet in tablets)
            {
                Log.Info($"[R8]   태블릿 '{tablet.Name}' 종류={tablet.Type}");
                foreach (StylusDevice stylus in tablet.StylusDevices)
                {
                    Log.Info($"[R8]     커서 '{stylus.Name}' id={stylus.Id} 뒤집힘={stylus.Inverted}");
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or NotSupportedException
            or System.Runtime.InteropServices.COMException)
        {
            // 태블릿 스택이 없거나 초기화 전이면 진단만 포기한다 — 판서 기능에는 영향이 없다.
            Log.Warn($"[R8] 태블릿 열거 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 입력 채널 1건 관측. <paramref name="source"/>는 어느 경로로 들어왔는지의 식별자다
    /// (승격된 마우스 / 스타일러스 라우팅 이벤트). 채널·장치 id·뒤집힘 조합마다 최초 1회만 기록한다.
    /// </summary>
    internal static void Observe(string source, StylusDevice? device, bool? invertedOverride = null)
    {
        bool? inverted = invertedOverride ?? device?.Inverted;
        string key = $"{source}|{device?.Id}|{inverted}";
        if (!Seen.Add(key))
        {
            return;
        }
        Log.Info(device is null
            ? $"[R8] {source}: 스타일러스 없음 (실제 마우스이거나 승격 정보 미포함)"
            : $"[R8] {source}: 커서 '{device.Name}' id={device.Id} 태블릿='{device.TabletDevice?.Name}' 뒤집힘={inverted} 범위내={device.InRange}");
    }

    /// <summary>
    /// 펜/마우스 다운. 계수를 비운다 — 요약은 펜 업에서만 나는데, 캡처 핫키·클릭 통과 전환으로 획이 업 없이 끝나면
    /// 전 획의 계수가 다음 획 요약에 섞이므로 시작에서도 비워야 획 단위가 보장된다.
    /// </summary>
    internal static void BeginStroke()
    {
        _strokeStylusPackets = 0;
        _strokeStylusMoves = 0;
        _strokePromotedMovesSkipped = 0;
    }

    /// <summary>스타일러스 채널이 <c>PointerMove</c>에 주입한 패킷 배치 1건 (크기 <paramref name="packets"/>).</summary>
    internal static void CountStylusBatch(int packets)
    {
        _strokeStylusMoves++;
        _strokeStylusPackets += packets;
    }

    /// <summary>승격 마우스 채널이 (정책대로) 주입을 건너뛴 이동 이벤트 1건.</summary>
    internal static void CountPromotedMoveSkipped() => _strokePromotedMovesSkipped++;

    /// <summary>
    /// 펜 업(승격된 왼쪽 버튼 업). 이 획에 스타일러스 패킷이 있었으면 채널 요약을 남기고 계수를 비운다.
    /// 마우스 전용 획(패킷 0)은 아무것도 남기지 않는다.
    /// </summary>
    internal static void EndStroke()
    {
        int packets = _strokeStylusPackets;
        int moves = _strokeStylusMoves;
        int skipped = _strokePromotedMovesSkipped;
        _strokeStylusPackets = 0;
        _strokeStylusMoves = 0;
        _strokePromotedMovesSkipped = 0;
        if (packets == 0 && skipped == 0)
        {
            return;
        }
        _strokesSummarized++;
        if (_strokesSummarized <= SummaryAlwaysFirst || _strokesSummarized % SummaryEvery == 0)
        {
            Log.Info($"[R8] 획 #{_strokesSummarized} 채널 요약: 스타일러스 이동 {moves}건/패킷 {packets}개, 승격 마우스 이동 {skipped}건 건너뜀");
        }
    }
}
