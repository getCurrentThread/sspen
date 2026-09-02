using SSPen.Interop;

namespace SSPen.Tests;

/// <summary>
/// <see cref="IHookInstaller"/>의 가짜 (52단계, FakeFrameSource 선례). 설치/해제/CallNext 호출을 기록하고, 잡아 둔 프로시저로
/// <see cref="Fire"/>가 합성 훅 이벤트를 쏜다 — 래퍼의 nCode 가드·소비/통과 판정·멱등성과 모니터의 게이트가 OS 없이 돈다.
/// <see cref="NextHandle"/>을 0으로 두면 설치 실패를 흉내 낸다.
/// </summary>
internal sealed class FakeHookInstaller : IHookInstaller
{
    public List<(int HookId, LowLevelHookProc Proc)> Installs { get; } = [];

    public List<nint> Uninstalls { get; } = [];

    public List<(nint Handle, int Code, nint WParam, nint LParam)> Nexts { get; } = [];

    public nint NextHandle { get; set; } = 0x1000;

    public nint CallNextResult { get; set; } = 42;

    /// <summary>지금 살아 있는 훅 핸들 (0 = 없음).</summary>
    public nint LiveHandle { get; private set; }

    public bool IsInstalled => LiveHandle != 0;

    /// <summary>마지막으로 설치된 프로시저 (해제돼도 남는다 — 재설치 동일성 비교용).</summary>
    public LowLevelHookProc? Proc { get; private set; }

    public nint Install(int hookId, LowLevelHookProc proc)
    {
        Installs.Add((hookId, proc));
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

    public nint CallNext(nint handle, int nCode, nint wParam, nint lParam)
    {
        Nexts.Add((handle, nCode, wParam, lParam));
        return CallNextResult;
    }

    /// <summary>살아 있는 훅에 합성 이벤트를 쏜다 — OS가 프로시저를 부르는 것과 같은 경로.</summary>
    public nint Fire(int nCode, nint wParam, nint lParam)
    {
        if (!IsInstalled || Proc is null)
        {
            throw new InvalidOperationException("설치된 훅이 없다 — 먼저 Install(모니터라면 Refresh)이 성공해야 한다.");
        }
        return Proc(nCode, wParam, lParam);
    }
}
