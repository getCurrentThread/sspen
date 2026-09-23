using SSPen.Interop;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="WindowStyling.ApplyZBand(IReadOnlyList{nint}, Func{nint, nint, bool})"/> (74단계 A7-2) — 밴드 적용 루프의 헤드리스 증인.
/// MTA, WPF·OS 불필요: 배치는 (hwnd, insertAfter)를 기록하는 가짜 델리게이트다.
/// 잠그는 것 (54단계 L5, AGENTS L15): 첫 창은 <c>HWND_TOPMOST</c>에, 나머지는 <b>마지막으로 성공한</b> 창 뒤에 놓는다(실패 항목은 건너뜀),
/// 0 항목은 배치하지 않는다, 배치 중에는 <see cref="WindowStyling.IsApplyingBand"/>가 참이고 끝나면 — 예외여도 — 들어올 때의 값으로 돌아간다(중첩 포함).
/// 이 정적 플래그를 건드리는 유닛 테스트 클래스는 이것뿐이다 — 다른 클래스는 WindowStyling 훅을 쓰지 않는다.
/// </summary>
public class WindowStylingZBandTests
{
    private const nint A = 0x100;
    private const nint B = 0x200;
    private const nint C = 0x300;

    private sealed class PlaceRecorder
    {
        public List<(nint Hwnd, nint InsertAfter)> Calls { get; } = [];

        public HashSet<nint> Failing { get; } = [];

        public bool Place(nint hwnd, nint insertAfter)
        {
            Calls.Add((hwnd, insertAfter));
            return !Failing.Contains(hwnd);
        }
    }

    [Fact]
    public void ApplyZBand_AllSucceed_FirstAtTopmostThenEachAfterPrevious()
    {
        var recorder = new PlaceRecorder();

        WindowStyling.ApplyZBand([A, B, C], recorder.Place);

        Assert.Equal([(A, NativeMethods.HWND_TOPMOST), (B, A), (C, B)], recorder.Calls);
    }

    [Fact]
    public void ApplyZBand_FailedMiddleEntry_NextInsertsAfterLastSuccess()
    {
        var recorder = new PlaceRecorder();
        recorder.Failing.Add(B);

        WindowStyling.ApplyZBand([A, B, C], recorder.Place);

        // B가 실패하면 C는 B가 아니라 마지막 성공인 A 뒤로 간다 — 실패한 창 뒤 삽입은 그 창이 엉뚱한 자리에 있을 때 밴드를 끌고 간다.
        Assert.Equal([(A, NativeMethods.HWND_TOPMOST), (B, A), (C, A)], recorder.Calls);
    }

    [Fact]
    public void ApplyZBand_FailedFirstEntry_NextInsertsAtTopmost()
    {
        var recorder = new PlaceRecorder();
        recorder.Failing.Add(A);

        WindowStyling.ApplyZBand([A, B], recorder.Place);

        // 아직 성공한 창이 없으면 다음 항목이 밴드 최상단을 잇는다.
        Assert.Equal([(A, NativeMethods.HWND_TOPMOST), (B, NativeMethods.HWND_TOPMOST)], recorder.Calls);
    }

    [Fact]
    public void ApplyZBand_SkipsZeroEntries()
    {
        var recorder = new PlaceRecorder();

        WindowStyling.ApplyZBand([0, A, 0, B, 0], recorder.Place);

        // 0(아직 없는 창)은 배치하지 않고, 다음 항목의 삽입 기준도 바꾸지 않는다.
        Assert.Equal([(A, NativeMethods.HWND_TOPMOST), (B, A)], recorder.Calls);
    }

    [Fact]
    public void ApplyZBand_EmptyList_PlacesNothing_AndFlagStaysFalse()
    {
        int calls = 0;

        WindowStyling.ApplyZBand([], (_, _) => { calls++; return true; });

        Assert.Equal(0, calls);
        Assert.False(WindowStyling.IsApplyingBand);
    }

    [Fact]
    public void ApplyZBand_DuringPlace_IsApplyingBandTrue_AfterwardsFalse()
    {
        var seen = new List<bool>();
        Assert.False(WindowStyling.IsApplyingBand);

        WindowStyling.ApplyZBand([A, B], (_, _) => { seen.Add(WindowStyling.IsApplyingBand); return true; });

        // 배치(SetWindowPos)가 요청 단계 훅을 동기로 부르는 바로 그 순간에 억제가 켜져 있어야 한다 (AGENTS L15).
        Assert.Equal([true, true], seen);
        Assert.False(WindowStyling.IsApplyingBand);
    }

    [Fact]
    public void ApplyZBand_PlaceThrows_RestoresApplyingFlag()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            WindowStyling.ApplyZBand([A, B], (_, _) => throw new InvalidOperationException("배치 실패")));

        Assert.Equal("배치 실패", ex.Message);
        // 복원이 빠지면 이후 모든 상승 요청이 영구히 억제된다 — 서피스가 툴바를 덮는 "보이는데 안 눌림"이 된다.
        Assert.False(WindowStyling.IsApplyingBand);
    }

    [Fact]
    public void ApplyZBand_Nested_RestoresOuterFlag()
    {
        var outerSeen = new List<bool>();
        var innerCalls = new List<(nint, nint)>();

        WindowStyling.ApplyZBand([A, B], (hwnd, _) =>
        {
            if (hwnd == A)
            {
                // 배치 도중 동기 재진입(훅 → 합성 루트 → ApplyZBand)을 흉내 낸다.
                WindowStyling.ApplyZBand([C], (h, after) => { innerCalls.Add((h, after)); return true; });
            }
            outerSeen.Add(WindowStyling.IsApplyingBand);
            return true;
        });

        // 안쪽이 끝나며 false로 되돌려 버리면 바깥의 남은 배치가 억제 없이 돈다 — 안쪽은 들어올 때의 값(참)으로 복원해야 한다.
        Assert.Equal([true, true], outerSeen);
        Assert.Equal([(C, NativeMethods.HWND_TOPMOST)], innerCalls);
        Assert.False(WindowStyling.IsApplyingBand);
    }
}
