using SSPen.Interop;

namespace SSPen.Shell;

/// <summary>
/// 모니터별 서피스 로스터의 순수 판정 (23단계, WI-16, R17). "어느 모니터의 서피스를 닫고 어느 모니터에 새로 만드는가"만
/// 결정하고, 창을 만들고 닫는 순서(<c>DetachFrom → Detach → HideThenClose</c>, <c>AttachTo → new → Add → Show</c>)는
/// <c>AppController</c> 어댑터가 그대로 소유한다.
///
/// 시동은 "열린 서피스 없음"에서 출발하는 같은 diff다 — <c>Build([], monitors, disabled)</c>는 닫을 것이 없고
/// 활성 모니터 전부를 만든다. 설정 동기화는 현재 열린 이름 목록을 넘긴다.
///
/// 보존이지 승인이 아니다: 토폴로지에서 <b>사라진</b> 모니터의 서피스는 닫지 않는다 (오늘의 동작 —
/// <c>SyncSurfacesWithSettings</c>는 비활성화된 것만 닫았다). 모니터 핫플러그 요구가 생기면 fix로 다룬다.
/// </summary>
public static class SurfaceRosterPlan
{
    /// <summary>닫을 서피스의 장치 이름 집합과, 새로 만들 모니터 목록(토폴로지 순서).</summary>
    public readonly record struct Diff(IReadOnlySet<string> ToClose, IReadOnlyList<MonitorSurfaceInfo> ToCreate);

    /// <param name="existing">현재 열린 서피스의 장치 이름 (시동 시 빈 목록).</param>
    /// <param name="monitors">현재 토폴로지.</param>
    /// <param name="disabled">설정에서 비활성화한 장치 이름.</param>
    public static Diff Build(
        IReadOnlyList<string> existing,
        IReadOnlyList<MonitorSurfaceInfo> monitors,
        IReadOnlySet<string> disabled)
    {
        var toClose = new HashSet<string>(existing.Where(disabled.Contains));
        var present = new HashSet<string>(existing);
        var toCreate = monitors
            .Where(m => !disabled.Contains(m.DeviceName) && !present.Contains(m.DeviceName))
            .ToList();
        return new Diff(toClose, toCreate);
    }
}
