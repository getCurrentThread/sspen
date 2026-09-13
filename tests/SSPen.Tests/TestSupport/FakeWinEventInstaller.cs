using SSPen.Interop;

namespace SSPen.Tests;

/// <summary>
/// <see cref="IWinEventInstaller"/>의 가짜 (54단계 L3, FakeHookInstaller 선례). 설치/해제 호출을 기록하고, 잡아 둔 프로시저로
/// <see cref="Fire"/>가 합성 WinEvent를 쏜다. <see cref="NextHandle"/>을 0으로 두면 설치 실패를 흉내 낸다.
/// </summary>
internal sealed class FakeWinEventInstaller : IWinEventInstaller
{
    public List<(uint Min, uint Max, WinEventProc Proc)> Installs { get; } = [];

    public List<nint> Uninstalls { get; } = [];

    public nint NextHandle { get; set; } = 0x2000;

    public nint LiveHandle { get; private set; }

    public bool IsInstalled => LiveHandle != 0;

    public WinEventProc? Proc { get; private set; }

    public nint Install(uint eventMin, uint eventMax, WinEventProc proc)
    {
        Installs.Add((eventMin, eventMax, proc));
        Proc = proc;
        if (NextHandle == 0)
        {
            return 0;
        }
        LiveHandle = NextHandle;
        return LiveHandle;
    }

    public void Uninstall(nint handle)
    {
        Uninstalls.Add(handle);
        if (handle == LiveHandle)
        {
            LiveHandle = 0;
        }
    }

    /// <summary>잡아 둔 프로시저에 합성 이벤트를 쏜다 — OS가 콜백을 부르는 것과 같은 경로. 해제 뒤에도 프로시저는 남는다(큐 잔여 이벤트 흉내).</summary>
    public void Fire(uint eventType, nint hwnd, int idObject = 0, int idChild = 0)
    {
        if (Proc is null)
        {
            throw new InvalidOperationException("설치된 적 없는 훅이다 — 먼저 Install이 한 번은 불려야 한다.");
        }
        Proc(LiveHandle, eventType, hwnd, idObject, idChild, 0, 0);
    }
}
