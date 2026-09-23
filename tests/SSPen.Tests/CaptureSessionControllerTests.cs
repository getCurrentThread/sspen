using System.Windows.Threading;
using SSPen.Capture;
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
/// 협력자가 모두 델리게이트로 주입되므로 <c>AppController</c> 없이 구동한다. 스냅샷·오버레이는 <c>ContextIdle</c> 연속체라
/// 여기서는 돌지 않는다: <see cref="RunSta"/>에는 펌프가 없어 큐에 쌓인 <c>BeginInvoke</c> 작업이 스레드와 함께 버려지므로
/// 실제 BitBlt도 <c>CaptureOverlayWindow</c>도 생기지 않는다. 캡처가 동기로 바뀌면 시작 기록에 "zband"가 끼어 여기가 빨개진다.
/// 74단계(A7-3)가 스냅샷·오버레이 이음매를 더하며 이 파일을 확장한다.
/// </summary>
public class CaptureSessionControllerTests
{
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

    /// <summary>
    /// 컨트롤러 1대 + 기록하는 가짜 협력자. 주입 델리게이트의 호출을 <see cref="Calls"/>에 순서대로 적고,
    /// <c>ActiveChanged</c>는 "active"로 적으면서 그 순간의 <c>IsActive</c>를 <see cref="ActiveAtEvent"/>에 남긴다.
    /// 핀 관리자·저장 폴더는 결과 처리(<c>OnCaptureComplete</c>) 경로에서만 쓰이므로 null로 둔다.
    /// </summary>
    private sealed class Rig
    {
        private Rig(bool toolbarVisible)
        {
            ToolbarVisible = toolbarVisible;
            Controller = new CaptureSessionController(
                Dispatcher.CurrentDispatcher,
                () => ToolbarVisible,
                v =>
                {
                    Calls.Add($"toolbar:{v}");
                    ToolbarVisible = v;
                },
                () => null,
                _ => Calls.Add("report"),
                () => null,
                () => Calls.Add("zband"),
                v => Calls.Add($"decorations:{v}"),
                v => Calls.Add($"surfaces:{v}"),
                v => Calls.Add($"toast:{v}"));
            Controller.ActiveChanged += () =>
            {
                Calls.Add("active");
                ActiveAtEvent.Add(Controller.IsActive);
            };
        }

        public CaptureSessionController Controller { get; }

        public List<string> Calls { get; } = [];

        public List<bool> ActiveAtEvent { get; } = [];

        public bool ToolbarVisible { get; private set; }

        public static Rig Create(bool toolbarVisible) => new(toolbarVisible);
    }
}
