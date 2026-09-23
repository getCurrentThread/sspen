using SSPen.Diagnostics;
using SSPen.Settings;

namespace SSPen.Shell;

/// <summary>
/// 핫키 재지정의 순서 오케스트레이터 (40단계, ARCH-8/AC-23): 라이브 핫키 맵 억제 → 캡처 대화상자 → <b>반드시</b> 복원.
/// 대화상자는 델리게이트로 받아 헤드리스로 검증한다 (CaptureFileNaming의 주입 exists 선례).
/// 이 순서가 계약인 이유: 억제 없이 모달을 띄우면 캡처 중 눌린 조합이 라이브 핫키로 발화하고, 복원을 빠뜨리면 창을 닫은 뒤
/// 전역 핫키가 죽은 채 남는다 — 예외가 나도 복원은 finally다.
///
/// <b>여기서 설정을 쓰지 않는다</b>: 예전에는 캡처 직후 (당시의 건별) <c>host.RemapHotkey</c>가 <c>SaveNow()</c>까지 해서,
/// 다른 모든 설정이 "확인을 눌러야 적용"인데 단축키만 <b>취소해도 이미 디스크에 남는</b> 비대칭이 있었다.
/// 재지정 반영은 창이 모아 두었다가 <c>Apply()</c>에서 <c>host.RemapHotkeys</c>로 한 번에 넘긴다 — 그 구현 본체가
/// <see cref="ApplyBatch"/>이고, AC-23의 재등록은 그 시점에 한 번 일어난다 (79단계, A6-3).
/// </summary>
public static class HotkeyRemapFlow
{
    /// <returns>확정된 조합, 취소면 null. 호출자가 보류 목록에 담았다가 확인 시 적용한다.</returns>
    /// <remarks>핫키 id는 받지 않는다 (57단계, A6-8): 이 흐름은 재지정을 쓰지 않으므로 id를 쓸 곳이 없다 — 보류 목록의 키는 호출자가 쥔다.</remarks>
    public static HotkeyDef? Run(ISettingsHost host, Func<HotkeyDef?> showDialog)
    {
        host.SuppressHotkeys();
        try
        {
            return showDialog();
        }
        finally
        {
            host.RestoreHotkeys();
        }
    }

    /// <summary>
    /// 설정 창 확인 시 보류분 일괄 반영 (79단계, A6-3 — <see cref="ISettingsHost.RemapHotkeys"/>의 구현 본체, 루트는 저장·재등록만 잇는다).
    /// 순서가 계약이다: 전부 <paramref name="hotkeys"/>에 쓴 뒤 <paramref name="save"/> 1회 → <paramref name="rebind"/> 1회. 빈 묶음은 무동작이다.
    /// <para>
    /// 예전에는 창이 보류분마다 건별 재지정을 불러 건마다 디스크 저장과 전체 재등록이 따라왔고(N회씩), 맞바꾸기
    /// (지우개 → 임시, 펜 → 지우개의 옛 조합, 지우개 → 펜의 옛 조합)의 첫 건 직후에는 두 항목이 같은 조합이라 RegisterHotKey 하나가
    /// 실패했다 — 루트가 그 실패 목록을 트레이 풍선에 이으므로 다음 건에서 바로 풀리는 가짜 경고가 떴다. 창의 충돌 검사는
    /// 보류분을 덮은 <b>최종</b> 표만 보증하므로(<see cref="HotkeyDraft.Overlay"/>), 등록은 최종 표로 한 번만 해야 한다.
    /// 실제 충돌(다른 앱 점유)은 그 한 번의 재등록에서 그대로 실패하고 경고된다.
    /// </para>
    /// <para>
    /// 예외: 쓰기는 저장·재등록보다 먼저 전부 끝나므로, <paramref name="save"/>가 던져도(디스크 IOException) 모든 건이 이미 설정 사전에 있다.
    /// 그래서 <see cref="HotkeyDraft.Drain"/>이 먼저 비워도 잃는 것이 없다 — 다음 저장(설정 창 확인·종료)이 그 값을 디스크에 쓰고,
    /// 창의 충돌 표와 캡처 대화상자 초기값도 설정을 읽는 <see cref="ISettingsHost.RemappableHotkeys"/>로 새 값을 본다(행 버튼은 이미 그 값이다).
    /// 재등록은 <c>finally</c>다(97단계, FINAL-REVIEW-REMAP-REBIND): 저장이 던져도 등록을 이미 최종인 사전에 맞춘 뒤 예외를 그대로 전파한다.
    /// 79단계 구현은 저장 예외 때 재등록을 건너뛰었는데, 보류분은 이미 비었으므로 다시 확인해도 재등록이 없어 설정·창의 표는 새 조합,
    /// 실제 등록은 옛 조합으로 갈라졌다. 기준 커밋(3c48786)의 건별 경로도 저장 예외 때 첫 건만 쓰고 재등록을 건너뛰었지만,
    /// 보류 목록을 루프 뒤에 비웠으므로 다시 확인하면 전부 다시 쓰고 저장·재등록했다 — 먼저 비우는 지금은 그 재시도가 없다.
    /// </para>
    /// </summary>
    /// <param name="batch">스테이징 순서의 보류분 (<see cref="HotkeyDraft.Drain"/>의 결과).</param>
    /// <param name="hotkeys">쓸 설정 사전 (<see cref="AppSettings.Hotkeys"/>).</param>
    /// <param name="save">즉시 저장 (루트: <c>SettingsBinder.SaveNow</c>).</param>
    /// <param name="rebind">최종 표로 전체 재등록 (루트: <c>HotkeyService.SetBindings(ShellHotkeys.BuildHotkeyMap())</c>).</param>
    public static void ApplyBatch(
        IReadOnlyList<(string Id, HotkeyDef Def)> batch,
        IDictionary<string, HotkeyDef> hotkeys,
        Action save,
        Action rebind)
    {
        if (batch.Count == 0)
        {
            return;
        }
        foreach (var (id, def) in batch)
        {
            hotkeys[id] = def;
            Log.Info($"핫키 재지정: {id} → {HotkeyFormatting.Format(def)}");
        }
        try
        {
            save();
        }
        finally
        {
            rebind(); // 최종 표로 한 번만 (AC-23) — 저장이 던져도 등록을 사전에 맞춘다 (97단계)
        }
    }
}
