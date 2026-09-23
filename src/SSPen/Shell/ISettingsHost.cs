using SSPen.Settings;

namespace SSPen.Shell;

// 40단계: SettingsWindow.cs 머리에서 자기 파일로 옮겼다 (IShellActions.cs와 대칭). 창은 이 계약만 보고 AppController를 모른다.
/// <summary>설정 창이 셸에 위임하는 계약 (AppController가 구현).</summary>
public interface ISettingsHost
{
    AppSettings Settings { get; }

    IReadOnlyList<(string Id, string Name, HotkeyDef Effective)> RemappableHotkeys { get; }

    /// <summary>
    /// 설정 창 확인 시 보류분 일괄 반영 — 전부 쓴 뒤 저장·재등록 1회 (AC-23; 79단계, A6-3). 순서의 본체는
    /// <see cref="HotkeyRemapFlow.ApplyBatch"/>다. 건별로 부르면 맞바꾸기 도중의 중간 충돌이 가짜 트레이 경고를 띄우고
    /// 저장·재등록이 N회 따라온다. 빈 묶음은 무동작이다.
    /// </summary>
    void RemapHotkeys(IReadOnlyList<(string Id, HotkeyDef Def)> batch);

    void SuppressHotkeys();

    void RestoreHotkeys();

    /// <summary>일반 설정 적용 + 저장 (확인 버튼).</summary>
    void ApplyGeneralSettings(AppSettings updated);

    /// <summary>업데이트 확인 및 안내 대화상자 표시.</summary>
    void CheckForUpdates();
}
