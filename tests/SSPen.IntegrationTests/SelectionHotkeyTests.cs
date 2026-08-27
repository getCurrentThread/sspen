using SSPen.Shell;
using Xunit;

namespace SSPen.IntegrationTests;

/// <summary>
/// 선택 도구 핫키 실등록 검증 (SEL-16, X8/R8). 맵 **구성**은 유닛(<c>ShellHotkeyMapTests</c>)이 덮으므로
/// 여기서는 실제 HWND와 <c>RegisterHotKey</c>가 필요한 부분만 남긴다.
///
/// 바인딩 성격 주의: 이 군은 모니터 토폴로지가 아니라 **타 앱의 Alt+Shift 점유**에 의존한다.
/// 실패하면 코드 결함이 아니라 다른 앱(예: Epic Pen)이 같은 조합을 선점한 것일 수 있다.
/// </summary>
public class SelectionHotkeyTests
{
    private const uint ModAlt = 0x0001;
    private const uint ModShift = 0x0004;
    private const uint AltShift = ModAlt | ModShift;

    [Fact]
    public void Register_SelectToolHotkey_Succeeds() => StaRunner.Run(() =>
    {
        using var service = new HotkeyService();
        bool fired = false;

        service.SetBindings([new HotkeyBinding("선택 도구", AltShift, VirtualKeys.V, () => fired = true)]);
        StaRunner.PumpMessages();

        Assert.Empty(service.FailedBindings);
        Assert.False(fired); // 등록만으로 발화하지 않는다.
    });

    [Fact]
    public void Register_DeleteSelectionHotkey_Succeeds() => StaRunner.Run(() =>
    {
        using var service = new HotkeyService();

        service.SetBindings([new HotkeyBinding("선택 삭제", AltShift, VirtualKeys.D, () => { })]);
        StaRunner.PumpMessages();

        Assert.Empty(service.FailedBindings);
    });

    /// <summary>
    /// 두 신규 조합이 서로도, 기존 조합과도 충돌하지 않는지 한 번에 등록해 확인한다.
    /// 한 번에 등록해야 의미가 있다: 개별 등록은 서로 간 충돌을 드러내지 못한다.
    /// </summary>
    [Fact]
    public void Register_SelectionHotkeysAlongsideExisting_ReportsNoNewConflicts() => StaRunner.Run(() =>
    {
        using var service = new HotkeyService();

        service.SetBindings(
        [
            new HotkeyBinding("실행 취소", AltShift, VirtualKeys.D6, () => { }),
            new HotkeyBinding("캡처", AltShift, VirtualKeys.S, () => { }),
            new HotkeyBinding("선 도구", AltShift, VirtualKeys.L, () => { }),
            new HotkeyBinding("선택 도구", AltShift, VirtualKeys.V, () => { }),
            new HotkeyBinding("선택 삭제", AltShift, VirtualKeys.D, () => { }),
        ]);
        StaRunner.PumpMessages();

        Assert.Empty(service.FailedBindings);
    });
}
