using System.Windows.Interop;
using SSPen.Diagnostics;
using SSPen.Interop;

namespace SSPen.Shell;

/// <summary>전역 핫키 1건: 수식키 + 가상 키 + 동작.</summary>
public sealed record HotkeyBinding(string Name, uint Modifiers, uint VirtualKey, Action Action);

/// <summary>
/// 전역 핫키 서비스 (WI-4, 프리모템 3).
/// - 개별 등록: 일부 실패(예: Epic Pen 동시 실행)를 허용하고 실패 목록을 로그/노출한다.
/// - 재등록 API: 설정 재지정(AC-23)·트레이 "판서 켜기" 시 재시도.
/// - 억제/복원 API: 설정 창 모달 "키 조합을 누르세요" 동안 라이브 맵 정지 (ARCH-8).
/// 캡처(Alt+Shift+S)는 Epic Pen과 충돌하지 않는 스펙 보장 조합이다.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource _source;
    private readonly List<HotkeyBinding> _bindings = [];
    private readonly List<string> _failed = [];
    private bool _suppressed;
    private bool _disposed;

    public HotkeyService()
    {
        // 메시지 전용 창 (HWND_MESSAGE 부모).
        var parameters = new HwndSourceParameters("SSPen.Hotkeys")
        {
            HwndSourceHook = WndProc,
            ParentWindow = -3, // HWND_MESSAGE
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
    }

    /// <summary>마지막 등록 시도에서 실패한 조합 이름 (사용자 경고용, 한국어).</summary>
    public IReadOnlyList<string> FailedBindings => _failed;

    public event Action<IReadOnlyList<string>>? RegistrationFailuresChanged;

    public void SetBindings(IEnumerable<HotkeyBinding> bindings)
    {
        UnregisterAll();
        _bindings.Clear();
        _bindings.AddRange(bindings);
        RegisterAll();
    }

    /// <summary>전체 재등록 (재지정/재시도 경로).</summary>
    public void RegisterAll()
    {
        if (_suppressed)
        {
            return;
        }
        UnregisterAll();
        _failed.Clear();
        for (int i = 0; i < _bindings.Count; i++)
        {
            var binding = _bindings[i];
            bool ok = NativeMethods.RegisterHotKey(
                _source.Handle, i, binding.Modifiers | NativeMethods.MOD_NOREPEAT, binding.VirtualKey);
            Log.Info($"RegisterHotKey [{binding.Name}] vk=0x{binding.VirtualKey:X2} mods=0x{binding.Modifiers:X} → {(ok ? "성공" : "실패")}");
            if (!ok)
            {
                _failed.Add(binding.Name);
            }
        }
        if (_failed.Count > 0)
        {
            Log.Warn(Strings.HotkeyConflictWarning + string.Join(", ", _failed));
        }
        RegistrationFailuresChanged?.Invoke(_failed);
    }

    /// <summary>모달 핫키 캡처 동안 라이브 맵 정지 (ARCH-8).</summary>
    public void Suppress()
    {
        if (_suppressed)
        {
            return;
        }
        _suppressed = true;
        UnregisterAll();
        Log.Info("핫키 맵 억제 (모달 캡처)");
    }

    /// <summary>모달 종료 시 복원.</summary>
    public void Restore()
    {
        if (!_suppressed)
        {
            return;
        }
        _suppressed = false;
        RegisterAll();
        Log.Info("핫키 맵 복원");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        UnregisterAll();
        _source.Dispose();
    }

    private void UnregisterAll()
    {
        for (int i = 0; i < _bindings.Count; i++)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, i);
        }
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = (int)wParam;
            if (id >= 0 && id < _bindings.Count && !_suppressed)
            {
                try
                {
                    _bindings[id].Action();
                }
                catch (Exception ex)
                {
                    Log.Error($"핫키 [{_bindings[id].Name}] 처리 중 오류", ex);
                }
                handled = true;
            }
        }
        return 0;
    }
}

/// <summary>가상 키 코드 (기본 맵에서 사용).</summary>
public static class VirtualKeys
{
    public const uint D0 = 0x30;
    public const uint D1 = 0x31;
    public const uint D2 = 0x32;
    public const uint D3 = 0x33;
    public const uint D4 = 0x34;
    public const uint D5 = 0x35;
    public const uint D6 = 0x36;
    public const uint D7 = 0x37;
    public const uint A = 0x41;
    public const uint B = 0x42;
    public const uint D = 0x44;
    public const uint E = 0x45;
    public const uint F = 0x46;
    public const uint L = 0x4C;
    public const uint R = 0x52;
    public const uint S = 0x53;
    public const uint T = 0x54;
    public const uint V = 0x56;
    public const uint W = 0x57;
    public const uint OemOpenBracket = 0xDB;  // [
    public const uint OemCloseBracket = 0xDD; // ]
}
