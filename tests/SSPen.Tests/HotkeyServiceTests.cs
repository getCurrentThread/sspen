using SSPen.Shell;
using Xunit;
using static SSPen.Tests.StaThread;

namespace SSPen.Tests;

/// <summary>
/// <see cref="HotkeyService"/> 등록 정책의 헤드리스 증인 (76단계 C-3; WI-4, 프리모템 3, ARCH-8, AC-23). OS 등록은
/// <see cref="FakeHotkeyRegistrar"/>가 대신하고, 메시지 전용 창(HwndSource)은 STA에서 헤드리스로 만들어지므로 본문은 <c>RunSta</c> 안에서 돈다.
/// 잠그는 것: id = 바인딩 인덱스와 <c>MOD_NOREPEAT</c> 합성(바이트 단위), 부분 실패 허용과 실패 목록, 재등록, 억제 중 무등록,
/// 복원 시 현재 바인딩 재등록, 해제, <see cref="HotkeyService.Dispatch"/>의 handled 판정과 예외 격리,
/// 그리고 실패 목록이 그대로여도 매번 발화하는 <see cref="HotkeyService.RegistrationFailuresChanged"/>(특성화 — 보존이지 승인이 아니다).
/// 실제 RegisterHotKey 경로의 증인은 통합 <c>HotkeyServiceTests</c>(Native)가 그대로 맡는다.
/// </summary>
public class HotkeyServiceTests
{
    private const uint Alt = 0x0001;
    private const uint Control = 0x0002;
    private const uint Shift = 0x0004;
    private const uint AltShift = Alt | Shift;
    private const uint NoRepeat = 0x4000; // MOD_NOREPEAT — OS 규약 값이라 리터럴로 잠근다.
    private const uint KeyA = 0x41;
    private const uint KeyB = 0x42;
    private const uint KeyC = 0x43;

    private static HotkeyBinding Bind(string name, uint vk, Action? action = null) =>
        new(name, AltShift, vk, action ?? (() => { }));

    private static void WithService(Action<HotkeyService, FakeHotkeyRegistrar> body) => RunSta(() =>
    {
        var fake = new FakeHotkeyRegistrar();
        using var service = new HotkeyService(fake);
        body(service, fake);
    });

    [Fact]
    public void SetBindings_AllCombinationsFree_RegistersEachAndReportsNoFailures() => WithService((service, fake) =>
    {
        service.SetBindings([Bind("가", KeyA), Bind("나", KeyB)]);

        Assert.Empty(service.FailedBindings);
        Assert.Equal([0, 1], fake.LiveIds);
    });

    [Fact]
    public void SetBindings_OneCombinationOccupied_ReportsOnlyThatName() => WithService((service, fake) =>
    {
        fake.Occupied.Add((AltShift, KeyB)); // 다른 앱(Epic Pen 등)이 선점 — 프리모템 3.

        service.SetBindings([Bind("가", KeyA), Bind("나", KeyB), Bind("다", KeyC)]);

        Assert.Equal(["나"], service.FailedBindings);
        Assert.Equal([0, 2], fake.LiveIds); // 부분 실패는 허용된다 — 나머지는 등록된 채 남는다.
    });

    [Fact]
    public void RegisterAll_EachBinding_AddsNoRepeatModifierAndUsesIndexAsId() => WithService((service, fake) =>
    {
        service.SetBindings(
        [
            new HotkeyBinding("가", AltShift, KeyA, () => { }),
            new HotkeyBinding("나", Control | Shift, KeyB, () => { }),
        ]);

        Assert.Equal(2, fake.Registers.Count);
        nint hwnd = fake.Registers[0].Hwnd;
        Assert.NotEqual(0, hwnd); // 메시지 전용 창의 실제 핸들이 넘어간다.
        Assert.Equal((hwnd, 0, AltShift | NoRepeat, KeyA, true), fake.Registers[0]);
        Assert.Equal((hwnd, 1, Control | Shift | NoRepeat, KeyB, true), fake.Registers[1]);
    });

    /// <summary>
    /// 재지정(AC-23)은 옛 맵의 id를 모두 해제한 뒤 새 맵을 인덱스 순으로 등록한다. 새 바인딩 수만큼 한 번 더 해제하는 것은
    /// RegisterAll 머리의 UnregisterAll이다(이미 해제된 id라 무해) — 호출 순서까지 특성화한다.
    /// </summary>
    [Fact]
    public void SetBindings_ReplacingMap_UnregistersOldIdsBeforeRegisteringNew() => WithService((service, fake) =>
    {
        service.SetBindings([Bind("가", KeyA), Bind("나", KeyB)]);
        fake.Calls.Clear();

        service.SetBindings([Bind("다", KeyC)]);

        Assert.Equal(["unreg:0", "unreg:1", "unreg:0", "reg:0"], fake.Calls);
        Assert.Equal((AltShift, KeyC), Assert.Single(fake.Live).Value);
    });

    [Fact]
    public void RegisterAll_AfterOccupantReleased_ClearsFailure() => WithService((service, fake) =>
    {
        fake.Occupied.Add((AltShift, KeyA));
        service.SetBindings([Bind("재시도 대상", KeyA)]);
        Assert.Equal(["재시도 대상"], service.FailedBindings);

        fake.Occupied.Remove((AltShift, KeyA)); // 선점 해제 뒤 재등록 (AC-23 재지정 / 트레이 재시도 경로).
        service.RegisterAll();

        Assert.Empty(service.FailedBindings);
        Assert.Equal([0], fake.LiveIds);
    });

    [Fact]
    public void Suppress_UnregistersAll_AndSetBindingsWhileSuppressed_DoesNotRegister() => WithService((service, fake) =>
    {
        int raised = 0;
        service.RegistrationFailuresChanged += _ => raised++;
        service.SetBindings([Bind("가", KeyA), Bind("나", KeyB)]);
        int registersBefore = fake.Registers.Count;
        raised = 0;

        service.Suppress();

        Assert.Empty(fake.Live); // 억제 중에는 같은 조합을 다른 소유자(캡처 대화상자)가 받을 수 있어야 한다 — ARCH-8.

        service.SetBindings([Bind("다", KeyC)]);
        service.RegisterAll();

        Assert.Equal(registersBefore, fake.Registers.Count);
        Assert.Empty(fake.Live);
        Assert.Equal(0, raised);
    });

    [Fact]
    public void Restore_RegistersCurrentBindings_NotStaleOnes() => WithService((service, fake) =>
    {
        service.SetBindings([Bind("가", KeyA), Bind("나", KeyB)]);
        service.Suppress();
        service.SetBindings([Bind("다", KeyC)]);
        int registersBefore = fake.Registers.Count;

        service.Restore();

        var restored = Assert.Single(fake.Registers.Skip(registersBefore));
        Assert.Equal((0, AltShift | NoRepeat, KeyC), (restored.Id, restored.Modifiers, restored.Vk));
        Assert.Equal((AltShift, KeyC), Assert.Single(fake.Live).Value);
    });

    [Fact]
    public void Restore_CombinationTakenWhileSuppressed_ReportsFailure() => WithService((service, fake) =>
    {
        service.SetBindings([Bind("억제 대상", KeyA)]);
        service.Suppress();
        fake.Occupied.Add((AltShift, KeyA)); // 억제 동안 다른 소유자가 가져갔다.

        service.Restore();

        Assert.Equal(["억제 대상"], service.FailedBindings);
    });

    [Fact]
    public void SuppressAndRestore_Repeated_AreIdempotent() => WithService((service, fake) =>
    {
        service.SetBindings([Bind("가", KeyA)]);
        service.Restore(); // 억제 전 복원은 무동작.
        Assert.Single(fake.Registers);

        service.Suppress();
        int unregistersAfterFirst = fake.Unregisters.Count;
        service.Suppress();
        Assert.Equal(unregistersAfterFirst, fake.Unregisters.Count);

        service.Restore();
        service.Restore();
        Assert.Equal(2, fake.Registers.Count);
    });

    [Fact]
    public void Dispose_UnregistersEveryBindingId() => RunSta(() =>
    {
        var fake = new FakeHotkeyRegistrar();
        var service = new HotkeyService(fake);
        service.SetBindings([Bind("가", KeyA), Bind("나", KeyB)]);
        fake.Unregisters.Clear();

        service.Dispose();
        service.Dispose(); // 두 번째는 무동작.

        Assert.Equal([0, 1], fake.Unregisters.Select(u => u.Id));
        Assert.Empty(fake.Live);
    });

    [Fact]
    public void Dispatch_InRangeId_RunsActionAndIsHandled() => WithService((service, _) =>
    {
        var fired = new List<string>();
        service.SetBindings([Bind("가", KeyA, () => fired.Add("가")), Bind("나", KeyB, () => fired.Add("나"))]);

        bool handled = service.Dispatch(1);

        Assert.True(handled);
        Assert.Equal(["나"], fired);
    });

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void Dispatch_OutOfRangeId_IsNotHandled(int id) => WithService((service, _) =>
    {
        int fired = 0;
        service.SetBindings([Bind("가", KeyA, () => fired++), Bind("나", KeyB, () => fired++)]);

        bool handled = service.Dispatch(id);

        Assert.False(handled);
        Assert.Equal(0, fired);
    });

    [Fact]
    public void Dispatch_WhileSuppressed_IsNotHandled() => WithService((service, _) =>
    {
        int fired = 0;
        service.SetBindings([Bind("가", KeyA, () => fired++)]);
        service.Suppress();

        Assert.False(service.Dispatch(0));
        Assert.Equal(0, fired);

        service.Restore();

        Assert.True(service.Dispatch(0));
        Assert.Equal(1, fired);
    });

    /// <summary>동작이 던져도 handled이고 예외는 밖으로 새지 않는다(로그로 남긴다 — Log는 초기화 전이면 무동작이라 유닛에서는 관측하지 않는다).</summary>
    [Fact]
    public void Dispatch_ActionThrows_IsSwallowedAndHandled() => WithService((service, _) =>
    {
        int fired = 0;
        service.SetBindings([Bind("가", KeyA, () => throw new InvalidOperationException("시험")), Bind("나", KeyB, () => fired++)]);

        bool handled = service.Dispatch(0);

        Assert.True(handled);
        Assert.True(service.Dispatch(1)); // 한 핫키의 예외가 다음 핫키를 막지 않는다.
        Assert.Equal(1, fired);
    });

    /// <summary>
    /// 특성화(보존이지 승인이 아니다): RegisterAll은 실패 목록이 바뀌지 않았어도, 실패가 없어도 매번 발화한다. 루트는 이 이벤트를
    /// 트레이 풍선에 잇는다 — A6-3(보류 재지정 일괄 적용)의 근거 자료다.
    /// </summary>
    [Fact]
    public void RegisterAll_UnchangedFailures_StillRaisesEvent_Characterization() => WithService((service, fake) =>
    {
        var payloads = new List<string[]>();
        service.RegistrationFailuresChanged += failed => payloads.Add([.. failed]);
        fake.Occupied.Add((AltShift, KeyB));

        service.SetBindings([Bind("가", KeyA), Bind("나", KeyB)]);
        service.RegisterAll();
        service.RegisterAll();

        Assert.Equal(3, payloads.Count);
        Assert.All(payloads, p => Assert.Equal(["나"], p));

        fake.Occupied.Clear();
        service.RegisterAll();

        Assert.Equal(4, payloads.Count);
        Assert.Empty(payloads[3]);
    });
}
