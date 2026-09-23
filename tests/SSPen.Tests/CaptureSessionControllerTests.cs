using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SSPen.Capture;
using SSPen.Interop;
using SSPen.Shell;
using Xunit;

using static SSPen.Tests.StaThread;

namespace SSPen.Tests;

/// <summary>
/// <see cref="CaptureSessionController"/>의 순서 계약 헤드리스 증인 (60단계, A8-3 — ARCH-4, ARCH-10, SEL-17, R12, AGENTS L35).
///
/// 시작의 동기 접두부: <c>ActiveChanged</c> → 툴바 숨김 → 장식 숨김 → 서피스 입력 중단 → 토스트 중단.
/// 종료(<c>CancelCaptureSession</c>): 툴바는 세션 전에 보였을 때만 복원하고, 장식·서피스·토스트는 <b>무조건</b> 복원한 뒤(R12)
/// z-밴드를 재적용하고 마지막에 <c>ActiveChanged</c>를 낸다 — <c>SelectionKeyMonitor</c>가 세션 종료를 재판정할 유일한 계기다.
///
/// 75단계(A7-3)가 스냅샷·오버레이를 이음매(<see cref="CaptureSnapshot"/> 델리게이트, <see cref="ICaptureOverlay"/>)로 빼서
/// 리그는 internal 생성자로 기록하는 가짜를 꽂는다 — 실제 BitBlt도 <c>CaptureOverlayWindow</c>도 생기지 않는다.
/// 스냅샷·오버레이는 <c>ContextIdle</c> 연속체다: <see cref="RunSta"/>에는 펌프가 없어 <see cref="Rig.Pump"/>
/// (<see cref="DispatcherPump.Drain"/> — ApplicationIdle Invoke라 ContextIdle 작업이 먼저 돈다)를 부르기 전에는 돌지 않는다.
/// 그래서 동기 접두부 증인들의 기록에 "snapshot"이 없다는 것이 곧 "숨김이 합성에 반영되기 전에는 찍지 않는다"(ARCH-4)의 증인이다 —
/// 캡처가 동기로 바뀌면 시작 기록에 "snapshot"이 끼어 여기가 빨개진다.
/// </summary>
public class CaptureSessionControllerTests
{
    /// <summary>목표 토폴로지 3×1920×1080, 원점 −1920 (AGENTS "Coordinate spaces").</summary>
    private static readonly PhysicalRect VirtualScreen = new(-1920, 0, 5760, 1080);

    /// <summary>가상 스크린 왼쪽 위 4×4 — 8×8 가짜 스냅샷 안에서 잘린다(크롭 오프셋 = 영역 − 가상 스크린 원점).</summary>
    private static readonly PhysicalRect CornerRegion = new(-1920, 0, 4, 4);

    [Fact]
    public void StartCapture_HidesToolbarDecorationsThenSuspendsSurfacesAndToast_InOrder()
    {
        RunSta(() =>
        {
            var rig = Rig.Create(toolbarVisible: true);

            rig.Controller.StartCapture();

            Assert.Equal(["active", "toolbar:False", "decorations:False", "surfaces:True", "toast:True"], rig.Calls);
        });
    }

    /// <summary>아키텍트 B2: 오버레이가 뜨기 전(ContextIdle 연속체 대기 중)의 재입력은 동기 플래그가 막는다 — 두 번째 호출은 아무것도 하지 않는다.</summary>
    [Fact]
    public void StartCapture_WhileActive_IsIgnored_AndActiveChangedFiresOnce()
    {
        RunSta(() =>
        {
            var rig = Rig.Create(toolbarVisible: true);

            rig.Controller.StartCapture();
            rig.Controller.StartCapture();

            Assert.Equal(["active", "toolbar:False", "decorations:False", "surfaces:True", "toast:True"], rig.Calls);
            Assert.Single(rig.Calls, c => c == "active");
        });
    }

    [Fact]
    public void CancelCaptureSession_ToolbarWasVisible_RestoresToolbar()
    {
        RunSta(() =>
        {
            var rig = Rig.Create(toolbarVisible: true);
            rig.Controller.StartCapture();
            Assert.False(rig.ToolbarVisible);

            rig.Controller.CancelCaptureSession();

            Assert.True(rig.ToolbarVisible);
            Assert.Single(rig.Calls, c => c == "toolbar:True");
        });
    }

    /// <summary>ARCH-10: 세션 전에 숨겨져 있던 툴바는 세션이 끝나도 숨긴 채로 둔다 — 복원은 "세션 전 상태로"이지 "무조건 표시"가 아니다.</summary>
    [Fact]
    public void CancelCaptureSession_ToolbarWasHidden_KeepsItHidden()
    {
        RunSta(() =>
        {
            var rig = Rig.Create(toolbarVisible: false);
            rig.Controller.StartCapture();

            rig.Controller.CancelCaptureSession();

            Assert.False(rig.ToolbarVisible);
            Assert.DoesNotContain("toolbar:True", rig.Calls);
        });
    }

    /// <summary>
    /// R12: 장식·서피스·토스트 복원은 무조건이고, 밴드 재적용과 <c>ActiveChanged</c>는 그 <b>뒤</b>다 —
    /// 복원이 빠지면 선택은 살아있는데 핸들이 안 보이고, 알림이 빠지면 ESC/Delete 훅이 세션 뒤에도 죽은 채로 남는다 (AGENTS L35).
    /// </summary>
    [Fact]
    public void CancelCaptureSession_RestoresDecorationsSurfacesToast_ThenAppliesZBand_ThenRaisesActiveChanged()
    {
        RunSta(() =>
        {
            var rig = Rig.Create(toolbarVisible: true);
            rig.Controller.StartCapture();
            rig.Calls.Clear();

            rig.Controller.CancelCaptureSession();

            Assert.Equal(["decorations:True", "surfaces:False", "toast:False", "zband", "active"], rig.Calls.TakeLast(5));
        });
    }

    /// <summary>
    /// <c>ActiveChanged</c> 구독자는 <b>바뀐 뒤의</b> <see cref="CaptureSessionController.IsActive"/>를 읽는다 —
    /// <c>SelectionKeyMonitor.Refresh</c>가 이 이벤트에서 게이트를 다시 계산하므로, 옛 값을 보면 훅이 한 박자 늦게 남거나 사라진다.
    /// </summary>
    [Fact]
    public void IsActive_FollowsStartAndCancel()
    {
        RunSta(() =>
        {
            var rig = Rig.Create(toolbarVisible: true);
            Assert.False(rig.Controller.IsActive);

            rig.Controller.StartCapture();
            Assert.True(rig.Controller.IsActive);

            rig.Controller.CancelCaptureSession();
            Assert.False(rig.Controller.IsActive);
            Assert.Equal([true, false], rig.ActiveAtEvent);
        });
    }

    // ── 75단계(A7-3): 스냅샷·오버레이 이음매 뒤의 연속체와 결과 처리.

    /// <summary>
    /// ARCH-4 확정 시퀀스의 뒤 절반: 숨김(동기 접두부) → 스냅샷 → 오버레이 생성 → 표시 → z-밴드 재적용.
    /// 밴드 재적용이 표시 <b>뒤</b>여야 오버레이 HWND가 밴드 목록(툴바 위)에 들어간다.
    /// </summary>
    [Fact]
    public void StartCapture_ContinuationRuns_SnapshotThenOverlayShowThenApplyZBand()
    {
        RunSta(() =>
        {
            var rig = Rig.Create(toolbarVisible: true);

            rig.Controller.StartCapture();
            rig.Pump();

            Assert.Equal(
                ["active", "toolbar:False", "decorations:False", "surfaces:True", "toast:True",
                 "snapshot", "overlay:create", "overlay:Show", "zband"],
                rig.Calls);
            Assert.True(rig.Controller.IsActive);
        });
    }

    /// <summary>오버레이는 스냅샷의 이미지와 그 이미지를 찍은 가상 스크린을 한 쌍으로 받는다 — 크롭이 같은 쌍을 기준으로 영역을 환산한다.</summary>
    [Fact]
    public void StartCapture_OverlayReceivesSnapshotImageAndVirtualScreen()
    {
        RunSta(() =>
        {
            var rig = Rig.Create(toolbarVisible: true);

            rig.Controller.StartCapture();
            rig.Pump();

            Assert.Same(rig.SnapshotImage, rig.OverlayImage);
            Assert.Equal(VirtualScreen, rig.OverlayVirtualScreen);
        });
    }

    /// <summary>z-밴드에 넣을 오버레이 HWND는 오버레이가 떠 있는 동안에만 0이 아니다 — 세션이 끝난 뒤 낡은 HWND가 밴드에 남지 않는다.</summary>
    [Fact]
    public void OverlayHwnd_FollowsOverlayLifetime()
    {
        RunSta(() =>
        {
            var rig = Rig.Create(toolbarVisible: true);

            rig.Controller.StartCapture();
            Assert.Equal((nint)0, rig.Controller.OverlayHwnd);

            rig.Pump();
            Assert.Equal(FakeOverlay.FakeHwnd, rig.Controller.OverlayHwnd);

            rig.Complete(CaptureAction.Cancel, default);
            Assert.Equal((nint)0, rig.Controller.OverlayHwnd);
        });
    }

    /// <summary>아키텍트 B2의 뒤 절반: 오버레이가 떠 있는 동안의 재입력도 무시된다 — 두 번째 스냅샷·고아 오버레이가 생기지 않는다.</summary>
    [Fact]
    public void StartCapture_WhileOverlayIsUp_IsIgnored_NoSecondSnapshot()
    {
        RunSta(() =>
        {
            var rig = Rig.Create(toolbarVisible: true);
            rig.Controller.StartCapture();
            rig.Pump();

            rig.Controller.StartCapture();
            rig.Pump();

            Assert.Single(rig.Calls, c => c == "snapshot");
            Assert.Single(rig.Calls, c => c == "overlay:create");
            Assert.Single(rig.Calls, c => c == "active");
        });
    }

    /// <summary>
    /// R12의 예외 경로: 스냅샷이 던지면 세션을 끝내고 전부 복원한다 — 툴바(세션 전에 보였음)·장식·서피스·토스트, 그 뒤 밴드와
    /// <c>ActiveChanged</c>. 오버레이는 만들어지지도 닫히지도 않고, 알림도 없다(결과물이 없다).
    /// </summary>
    [Fact]
    public void StartCapture_SnapshotThrows_EndsSession_RestoresAll_ActiveChangedTwice()
    {
        RunSta(() =>
        {
            var rig = Rig.Create(toolbarVisible: true, snapshotThrows: true);

            rig.Controller.StartCapture();
            rig.Pump();

            Assert.False(rig.Controller.IsActive);
            Assert.Equal(
                ["active", "toolbar:False", "decorations:False", "surfaces:True", "toast:True",
                 "snapshot", "toolbar:True", "decorations:True", "surfaces:False", "toast:False", "zband", "active"],
                rig.Calls);
            Assert.Equal([true, false], rig.ActiveAtEvent);
            Assert.Empty(rig.Reports);
        });
    }

    /// <summary>
    /// 표시가 던져도 이미 만든 오버레이는 <see cref="ICaptureOverlay.Dismiss"/>로 닫는다(HideThenClose — 1400 방어) —
    /// 반쯤 뜬 전체화면 톱모스트 창이 세션 없이 남으면 화면 전체가 입력을 잃는다.
    /// </summary>
    [Fact]
    public void StartCapture_OverlayShowThrows_DismissesThatOverlay_AndEndsSession()
    {
        RunSta(() =>
        {
            var rig = Rig.Create(toolbarVisible: true, overlayShowThrows: true);

            rig.Controller.StartCapture();
            rig.Pump();

            Assert.False(rig.Controller.IsActive);
            Assert.Equal((nint)0, rig.Controller.OverlayHwnd);
            Assert.Equal(
                ["snapshot", "overlay:create", "overlay:Show", "overlay:Dismiss",
                 "toolbar:True", "decorations:True", "surfaces:False", "toast:False", "zband", "active"],
                rig.Calls.Skip(5));
        });
    }

    /// <summary>
    /// 취소 완료: 오버레이를 Dismiss(닫기는 이 길뿐)하고 복원·밴드·<c>ActiveChanged</c>를 낸 <b>뒤</b>에 알린다 —
    /// 토스트는 밴드 멤버라 알림을 먼저 띄우면 오버레이가 사라지는 순간 밴드가 다시 계산되며 한 프레임 흔들린다.
    /// 취소는 결과물이 없으므로 알림 식별자는 None이다(아무 일도 하지 않은 조작은 말을 걸지 않는다).
    /// </summary>
    [Fact]
    public void Complete_Cancel_DismissesOverlay_ReportsAfterActiveChanged()
    {
        RunSta(() =>
        {
            var rig = Rig.Create(toolbarVisible: true);
            rig.Controller.StartCapture();
            rig.Pump();
            rig.Calls.Clear();

            rig.Complete(CaptureAction.Cancel, CornerRegion);

            Assert.Equal(
                ["overlay:Dismiss", "toolbar:True", "decorations:True", "surfaces:False", "toast:False", "zband", "active", "report"],
                rig.Calls);
            Assert.Equal(CaptureMessageId.None, Assert.Single(rig.Reports).Message);
            Assert.False(rig.Controller.IsActive);
        });
    }

    /// <summary>빈 영역은 결과물이 없다 — 핀 관리자를 묻지도(크롭도 하지 않는다) 알릴 말을 만들지도 않고 세션만 끝낸다.</summary>
    [Fact]
    public void Complete_PinWithEmptyRegion_DoesNotAskForPins_EndsSessionThenReportsNone()
    {
        RunSta(() =>
        {
            var rig = Rig.Create(toolbarVisible: true);
            rig.Controller.StartCapture();
            rig.Pump();
            rig.Calls.Clear();

            rig.Complete(CaptureAction.Pin, new PhysicalRect(-1920, 0, 0, 4));

            Assert.DoesNotContain("pins", rig.Calls);
            Assert.Equal("report", rig.Calls[^1]);
            Assert.Equal(CaptureMessageId.None, Assert.Single(rig.Reports).Message);
        });
    }

    /// <summary>
    /// 핀 관리자가 없으면(시동 전·종료 중) 핀 실패를 경고로 알린다 — 침묵하면 사용자는 캡처 자체가 안 된 줄 안다.
    /// 알림은 여기서도 세션 정리 뒤다: 크롭 → 핀 관리자 조회 → Dismiss → 복원 → 밴드 → <c>ActiveChanged</c> → 알림.
    /// </summary>
    [Fact]
    public void Complete_Pin_WithNullPins_ReportsPinFailedAfterSessionEnds()
    {
        RunSta(() =>
        {
            var rig = Rig.Create(toolbarVisible: true);
            rig.Controller.StartCapture();
            rig.Pump();
            rig.Calls.Clear();

            rig.Complete(CaptureAction.Pin, CornerRegion);

            Assert.Equal(
                ["pins", "overlay:Dismiss", "toolbar:True", "decorations:True", "surfaces:False", "toast:False", "zband", "active", "report"],
                rig.Calls);
            var outcome = Assert.Single(rig.Reports);
            Assert.Equal(CaptureMessageId.PinFailed, outcome.Message);
            Assert.Equal(ToastKind.Warning, outcome.Kind);
        });
    }

    /// <summary>ARCH-10을 완료 경로에서도: 세션 전에 숨겨져 있던 툴바는 오버레이가 끝나도 숨긴 채로 둔다.</summary>
    [Fact]
    public void StartCapture_ToolbarWasHidden_CompleteDoesNotShowToolbar()
    {
        RunSta(() =>
        {
            var rig = Rig.Create(toolbarVisible: false);
            rig.Controller.StartCapture();
            rig.Pump();

            rig.Complete(CaptureAction.Cancel, default);

            Assert.False(rig.ToolbarVisible);
            Assert.DoesNotContain("toolbar:True", rig.Calls);
            Assert.Contains("overlay:Dismiss", rig.Calls);
        });
    }

    /// <summary>
    /// 기록하는 가짜 오버레이. <c>Close</c>가 없는 <see cref="ICaptureOverlay"/>라 닫기는 <see cref="Dismiss"/>로만 기록된다.
    /// </summary>
    private sealed class FakeOverlay(List<string> calls, bool showThrows) : ICaptureOverlay
    {
        public const nint FakeHwnd = 0x5150;

        public nint Hwnd => FakeHwnd;

        public void Show()
        {
            calls.Add("overlay:Show");
            if (showThrows)
            {
                throw new InvalidOperationException("가짜 오버레이 표시 실패");
            }
        }

        public void Dismiss() => calls.Add("overlay:Dismiss");
    }

    /// <summary>
    /// 컨트롤러 1대 + 기록하는 가짜 협력자. 주입 델리게이트의 호출을 <see cref="Calls"/>에 순서대로 적고,
    /// <c>ActiveChanged</c>는 "active"로 적으면서 그 순간의 <c>IsActive</c>를 <see cref="ActiveAtEvent"/>에 남긴다.
    /// 스냅샷은 8×8 이미지 + 3모니터 가상 스크린, 오버레이는 <see cref="FakeOverlay"/>이고 완료 콜백은 <see cref="Complete"/>로 부른다.
    /// 핀 관리자는 없고(null — "pins"로 기록), 저장 폴더는 결과 처리의 저장 경로에서만 쓰여 null로 둔다.
    /// </summary>
    private sealed class Rig
    {
        private Action<CaptureAction, PhysicalRect>? _onComplete;

        private Rig(bool toolbarVisible, bool snapshotThrows, bool overlayShowThrows)
        {
            ToolbarVisible = toolbarVisible;
            SnapshotImage = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, new byte[8 * 8 * 4], 8 * 4);
            SnapshotImage.Freeze();
            Controller = new CaptureSessionController(
                Dispatcher.CurrentDispatcher,
                () => ToolbarVisible,
                v =>
                {
                    Calls.Add($"toolbar:{v}");
                    ToolbarVisible = v;
                },
                () =>
                {
                    Calls.Add("pins");
                    return null;
                },
                outcome =>
                {
                    Calls.Add("report");
                    Reports.Add(outcome);
                },
                () => null,
                () => Calls.Add("zband"),
                v => Calls.Add($"decorations:{v}"),
                v => Calls.Add($"surfaces:{v}"),
                v => Calls.Add($"toast:{v}"),
                takeSnapshot: () =>
                {
                    Calls.Add("snapshot");
                    if (snapshotThrows)
                    {
                        throw new InvalidOperationException("가짜 BitBlt 실패");
                    }
                    return new CaptureSnapshot(SnapshotImage, VirtualScreen);
                },
                createOverlay: (image, virtualScreen, onComplete) =>
                {
                    Calls.Add("overlay:create");
                    OverlayImage = image;
                    OverlayVirtualScreen = virtualScreen;
                    _onComplete = onComplete;
                    return new FakeOverlay(Calls, overlayShowThrows);
                });
            Controller.ActiveChanged += () =>
            {
                Calls.Add("active");
                ActiveAtEvent.Add(Controller.IsActive);
            };
        }

        public CaptureSessionController Controller { get; }

        public List<string> Calls { get; } = [];

        public List<bool> ActiveAtEvent { get; } = [];

        public List<CaptureOutcome> Reports { get; } = [];

        public bool ToolbarVisible { get; private set; }

        public BitmapSource SnapshotImage { get; }

        public BitmapSource? OverlayImage { get; private set; }

        public PhysicalRect OverlayVirtualScreen { get; private set; }

        public static Rig Create(bool toolbarVisible, bool snapshotThrows = false, bool overlayShowThrows = false) =>
            new(toolbarVisible, snapshotThrows, overlayShowThrows);

        /// <summary>쌓인 <c>ContextIdle</c> 연속체(스냅샷 → 오버레이)를 돌린다.</summary>
        public void Pump() => DispatcherPump.Drain(Dispatcher.CurrentDispatcher);

        /// <summary>오버레이의 완료 콜백 — 실제 창에서는 액션바 버튼·Esc·Enter·바깥 클릭이 부른다.</summary>
        public void Complete(CaptureAction action, PhysicalRect region)
        {
            Assert.NotNull(_onComplete);
            _onComplete(action, region);
        }
    }
}
