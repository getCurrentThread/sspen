using SSPen.Interop;

namespace SSPen.Tests;

/// <summary>
/// <see cref="IHotkeyRegistrar"/>의 가짜 (76단계 C-3, FakeHookInstaller·FakeWinEventInstaller 선례). 다른 앱이 쥔 조합은
/// <see cref="Occupied"/>에 넣고, 살아 있는 등록은 (hwnd, id)별로 <see cref="Live"/>에 둔다 — 등록은 두 곳 어디와 겹쳐도 실패한다(OS와 같다).
/// 조합 비교는 <c>MOD_NOREPEAT</c>(0x4000)를 뺀 수식키 + 가상 키다 — 그 비트는 반복 억제 옵션이지 조합의 일부가 아니다.
/// 호출은 인자 그대로 <see cref="Registers"/>/<see cref="Unregisters"/>에, 순서는 <see cref="Calls"/>("reg:id"/"fail:id"/"unreg:id")에 남는다.
/// </summary>
internal sealed class FakeHotkeyRegistrar : IHotkeyRegistrar
{
    private const uint ModNoRepeat = 0x4000;

    private readonly Dictionary<(nint Hwnd, int Id), (uint Modifiers, uint Vk)> _live = [];

    /// <summary>다른 소유자가 점유한 조합 (<c>MOD_NOREPEAT</c> 없는 수식키, 가상 키). 테스트가 넣고 뺀다.</summary>
    public HashSet<(uint Modifiers, uint Vk)> Occupied { get; } = [];

    public List<(nint Hwnd, int Id, uint Modifiers, uint Vk, bool Ok)> Registers { get; } = [];

    public List<(nint Hwnd, int Id)> Unregisters { get; } = [];

    /// <summary>등록·해제 호출 순서 — "reg:id"(성공), "fail:id"(실패), "unreg:id".</summary>
    public List<string> Calls { get; } = [];

    /// <summary>지금 살아 있는 등록 — (hwnd, id) → 정규화한 조합.</summary>
    public IReadOnlyDictionary<(nint Hwnd, int Id), (uint Modifiers, uint Vk)> Live => _live;

    /// <summary>살아 있는 등록의 id (오름차순).</summary>
    public IReadOnlyList<int> LiveIds => [.. _live.Keys.Select(k => k.Id).Order()];

    public bool Register(nint hwnd, int id, uint modifiers, uint vk)
    {
        var combo = (modifiers & ~ModNoRepeat, vk);
        bool ok = !Occupied.Contains(combo)
            && !_live.Any(entry => entry.Key != (hwnd, id) && entry.Value == combo);
        Registers.Add((hwnd, id, modifiers, vk, ok));
        Calls.Add($"{(ok ? "reg" : "fail")}:{id}");
        if (ok)
        {
            _live[(hwnd, id)] = combo;
        }
        return ok;
    }

    public void Unregister(nint hwnd, int id)
    {
        Unregisters.Add((hwnd, id));
        Calls.Add($"unreg:{id}");
        _live.Remove((hwnd, id));
    }
}
