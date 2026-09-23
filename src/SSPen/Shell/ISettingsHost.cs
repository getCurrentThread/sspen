using SSPen.Settings;

namespace SSPen.Shell;

// 40단계: SettingsWindow.cs 머리에서 자기 파일로 옮겼다 (IShellActions.cs와 대칭). 창은 이 계약만 보고 AppController를 모른다.
/// <summary>설정 창이 셸에 위임하는 계약 (AppController가 구현).</summary>
public interface ISettingsHost
{
    AppSettings Settings { get; }

    IReadOnlyList<(string Id, string Name, HotkeyDef Effective)> RemappableHotkeys { get; }

    /// <summary>설정 창 확인 버튼에서 보류분마다 호출 — 저장 + 즉시 재등록 (AC-23).</summary>
    void RemapHotkey(string id, HotkeyDef def);

    void SuppressHotkeys();

    void RestoreHotkeys();

    /// <summary>일반 설정 적용 + 저장 (확인 버튼).</summary>
    void ApplyGeneralSettings(AppSettings updated);

    /// <summary>업데이트 확인 및 안내 대화상자 표시.</summary>
    void CheckForUpdates();
}
