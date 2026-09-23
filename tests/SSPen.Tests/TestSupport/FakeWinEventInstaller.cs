using SSPen.Interop;

namespace SSPen.Tests;

/// <summary>
/// <see cref="IWinEventInstaller"/>의 가짜 (54단계 L3, FakeHookInstaller 선례). 설치/해제 호출을 기록하고, 잡아 둔 프로시저로
/// <see cref="Fire"/>가 합성 WinEvent를 쏜다. <see cref="NextHandle"/>을 0으로 두면 설치 실패를 흉내 낸다.
///
/// 72단계: 한 설치기를 두 훅(REORDER, FOREGROUND)이 나눠 쓰는 <c>ZBandVerifier</c>를 위해 <b>이벤트 범위별 라우팅</b>을 한다 —
/// <see cref="Fire"/>는 설치 범위(Min..Max)가 그 이벤트를 덮는 프로시저에만 간다(OS와 같다). 설치별 결과는
/// <see cref="NextHandles"/> 큐로 따로 줄 수 있다(비어 있으면 <see cref="NextHandle"/>). 기존 단일 훅 API(<see cref="Proc"/>·
/// <see cref="LiveHandle"/>·<see cref="IsInstalled"/>)는 그대로다 — 마지막 설치를 가리킨다.
/// </summary>
internal sealed class FakeWinEventInstaller : IWinEventInstaller
{
    // 프로시저 인스턴스(= WinEventWatch 하나, GC 고정)별 경로. 재설치는 같은 경로의 범위·핸들을 갱신한다.
    private readonly List<Route> _routes = [];

    public List<(uint Min, uint Max, WinEventProc Proc)> Installs { get; } = [];

    public List<nint> Uninstalls { get; } = [];

    public nint NextHandle { get; set; } = 0x2000;

    /// <summary>설치별 결과 큐 — 비어 있지 않으면 Install이 <see cref="NextHandle"/> 대신 하나를 꺼낸다 (0 = 그 설치만 실패).</summary>
    public Queue<nint> NextHandles { get; } = new();

    public nint LiveHandle { get; private set; }

    public bool IsInstalled => LiveHandle != 0;

    public WinEventProc? Proc { get; private set; }

    /// <summary>지금 설치돼 있는(해제되지 않은) 훅의 이벤트 범위 — 설치 순서.</summary>
    public IReadOnlyList<(uint Min, uint Max)> LiveRanges =>
        [.. _routes.Where(r => r.Handle != 0).Select(r => (r.Min, r.Max))];

    public nint Install(uint eventMin, uint eventMax, WinEventProc proc)
    {
        Installs.Add((eventMin, eventMax, proc));
        Proc = proc;
        nint handle = NextHandles.Count > 0 ? NextHandles.Dequeue() : NextHandle;

        var route = _routes.Find(r => ReferenceEquals(r.Proc, proc));
        if (route is null)
        {
            route = new Route(proc);
            _routes.Add(route);
        }
        route.Min = eventMin;
        route.Max = eventMax;
        route.Handle = handle;

        if (handle == 0)
        {
            return 0;
        }
        LiveHandle = handle;
        return LiveHandle;
    }

    public void Uninstall(nint handle)
    {
        Uninstalls.Add(handle);
        var route = _routes.Find(r => r.Handle == handle && handle != 0);
        if (route is not null)
        {
            route.Handle = 0;
        }
        if (handle == LiveHandle)
        {
            LiveHandle = 0;
        }
    }

    /// <summary>
    /// 설치 범위가 <paramref name="eventType"/>을 덮는 프로시저에 합성 이벤트를 쏜다 — OS가 콜백을 부르는 것과 같은 경로.
    /// 해제·설치 실패 뒤에도 프로시저는 남는다(큐 잔여 이벤트 흉내) — 버릴지는 <see cref="WinEventWatch"/>가 판정한다.
    /// </summary>
    public void Fire(uint eventType, nint hwnd, int idObject = 0, int idChild = 0)
    {
        if (Proc is null)
        {
            throw new InvalidOperationException("설치된 적 없는 훅이다 — 먼저 Install이 한 번은 불려야 한다.");
        }
        foreach (var route in _routes.ToList())
        {
            if (eventType >= route.Min && eventType <= route.Max)
            {
                route.Proc(route.Handle, eventType, hwnd, idObject, idChild, 0, 0);
            }
        }
    }

    private sealed class Route(WinEventProc proc)
    {
        public WinEventProc Proc { get; } = proc;

        public uint Min { get; set; }

        public uint Max { get; set; }

        public nint Handle { get; set; }
    }
}
