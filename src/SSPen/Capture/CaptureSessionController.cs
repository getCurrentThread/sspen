using System.Windows.Threading;
using SSPen.Diagnostics;
using SSPen.Interop;
using SSPen.Pin;
using SSPen.Shell;

namespace SSPen.Capture;

/// <summary>
/// 캡처 세션 수명 관리 (WI-11, AppController에서 분리). 툴바 숨김·오버레이 생성·결과 처리·복원을
/// 하나의 시퀀스로 소유한다. 셸은 툴바 가시성/PinManager/경고/저장 경로/z-밴드를 델리게이트로 주입한다.
/// UI 디스패처는 생성자로 주입받는다 (LD-4): <c>Application.Current</c>에 의존하면 통합 테스트가
/// STA 스레드마다 AppDomain 단일 <c>Application</c> 제약에 걸려 무너진다 (R24).
/// </summary>
public sealed class CaptureSessionController
{
    private readonly Dispatcher _dispatcher;
    private readonly Func<bool> _toolbarVisible;
    private readonly Action<bool> _setToolbarVisible;
    private readonly Func<PinManager?> _pins;
    private readonly Action<string> _warn;
    private readonly Func<string?> _saveFolder;
    private readonly Action _applyZBand;
    private readonly Action<bool> _setDecorationsVisible;
    private readonly Action<bool> _setSurfacesSuspended;

    private CaptureOverlayWindow? _captureOverlay;
    private bool _captureSessionActive;
    private bool _toolbarRestoreAfterCapture;

    public CaptureSessionController(
        Dispatcher dispatcher,
        Func<bool> toolbarVisible,
        Action<bool> setToolbarVisible,
        Func<PinManager?> pins,
        Action<string> warn,
        Func<string?> saveFolder,
        Action applyZBand,
        Action<bool> setDecorationsVisible,
        Action<bool> setSurfacesSuspended)
    {
        _dispatcher = dispatcher;
        _toolbarVisible = toolbarVisible;
        _setToolbarVisible = setToolbarVisible;
        _pins = pins;
        _warn = warn;
        _saveFolder = saveFolder;
        _applyZBand = applyZBand;
        _setDecorationsVisible = setDecorationsVisible;
        _setSurfacesSuspended = setSurfacesSuspended;
    }

    /// <summary>캡처 세션 진행 중 여부 (툴바 토글 가드 등에서 참조).</summary>
    public bool IsActive => _captureSessionActive;

    /// <summary>
    /// <see cref="IsActive"/>가 바뀌었다 — 이 상태를 게이트로 쓰는 조건부 훅을 다시 판정시키기 위한 신호.
    ///
    /// 없으면 <c>SelectionKeyMonitor</c>가 비대칭이 된다: 세션 시작으로 훅이 해제되는 계기는
    /// 세션 중의 상태 변화뿐이고, 세션 종료에는 아무 계기가 없어 <b>ESC/Delete가 조용히 죽은 채로 남는다</b>.
    /// </summary>
    public event Action? ActiveChanged;

    /// <summary>현재 캡처 오버레이 HWND (z-밴드 삽입용, 없으면 0).</summary>
    public nint OverlayHwnd => _captureOverlay?.Hwnd ?? 0;

    /// <summary>
    /// Alt+Shift+S 캡처 세션 (WI-11, ARCH-4 확정 시퀀스):
    /// 툴바 숨김 → 렌더 틱 + DwmFlush(합성 안정화) → BitBlt → 고정 스냅샷 오버레이 표시.
    /// 콘텐츠 서피스·핀·후광은 보이는 채로 찍는다 (as-seen 인텐트: 잉크 포함, 툴바 제외).
    /// 툴바는 세션 내내 숨김, 복사/저장/핀/취소/Esc 종료 시 복원 (ARCH-10).
    /// </summary>
    public void StartCapture()
    {
        // 아키텍트 B2: 오버레이 생성은 ContextIdle 연속체 안이므로, 그 사이 재입력(Alt+Shift+S 연타)을
        // 동기 플래그로 막는다 — 이중 세션/고아 오버레이 방지.
        if (_captureSessionActive)
        {
            return; // 세션 중복 방지.
        }
        _captureSessionActive = true;
        ActiveChanged?.Invoke();
        Log.Info("캡처 세션 시작: 툴바·장식 숨김 → 서피스 입력 중단 → 렌더 패스 대기 → 합성 플러시 → BitBlt");
        _toolbarRestoreAfterCapture = _toolbarVisible();
        _setToolbarVisible(false);
        // SEL-17: 장식은 잉크가 아니라 UI다 — 결과물에 들어가면 안 된다.
        // 서피스 창 자체를 숨기면 잉크까지 사라져 as-seen 인텐트가 깨지므로 장식 레이어만 숨긴다.
        _setDecorationsVisible(false);
        _setSurfacesSuspended(true);

        // 숨김이 다음 합성에 반영된 뒤 BitBlt (ARCH-4: DWM 경합 제거).
        _dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            try
            {
                WaitForRenderPass();
                NativeMethods.DwmFlush();
                var virtualScreen = MonitorTopology.VirtualScreen();
                var snapshot = CaptureService.CaptureVirtualScreen();
                Log.Info($"캡처 스냅샷 완료: {virtualScreen}");
                _captureOverlay = new CaptureOverlayWindow(snapshot, virtualScreen, (action, region) =>
                    OnCaptureComplete(action, region, snapshot, virtualScreen));
                _captureOverlay.Show();
                _applyZBand();
            }
            catch (Exception ex)
            {
                Log.Error("캡처 세션 시작 실패", ex);
                EndCaptureSession();
            }
        });
    }

    private void OnCaptureComplete(
        CaptureAction action,
        PhysicalRect region,
        System.Windows.Media.Imaging.BitmapSource snapshot,
        PhysicalRect virtualScreen)
    {
        try
        {
            if (action != CaptureAction.Cancel && !region.IsEmpty)
            {
                var cropped = CaptureService.Crop(snapshot, region, virtualScreen);
                switch (action)
                {
                    case CaptureAction.Copy:
                        if (!CaptureOutputs.CopyToClipboard(cropped))
                        {
                            // 클리너 B1: 실패를 로그에만 두지 않고 사용자에게 알린다 (잠금 문자열).
                            _warn(Strings.ClipboardCopyFailed);
                        }
                        break;
                    case CaptureAction.Save:
                        var folder = _saveFolder();
                        CaptureOutputs.SavePng(cropped, string.IsNullOrEmpty(folder) ? null : folder);
                        break;
                    case CaptureAction.Pin:
                        _pins()?.CreatePin(cropped, region);
                        break;
                }
            }
        }
        finally
        {
            EndCaptureSession();
        }
        Log.Info($"캡처 세션 종료: {action} {region}");
    }

    /// <summary>
    /// 렌더 패스 1회 완료를 기다린다 (ARCH-14).
    ///
    /// 왜 필요한가: 툴바는 네이티브 <c>ShowWindow</c>라 <c>DwmFlush</c> 한 번으로 확정적이지만,
    /// 장식은 **이미 보이는 창 내부의 Canvas 변경**이라 WPF 렌더 제출과 DWM 반영이 비동기다.
    /// <c>DwmFlush</c>는 '다음 합성까지 대기'이지 '방금 만든 프레임 반영까지 대기'가 아니므로,
    /// 이것이 없으면 장식이 남은 프레임이 BitBlt되는 race가 생긴다.
    ///
    /// async 금지 규약에 맞춰 일회성 <c>CompositionTarget.Rendering</c> 후크로 동기 대기한다
    /// (<c>AppController.OnRenderTick</c>가 같은 패턴의 선례).
    /// </summary>
    private void WaitForRenderPass()
    {
        var frame = new DispatcherFrame();
        EventHandler? hook = null;
        // 만약 렌더 틱이 오지 않으면(서피스가 전부 숨겨져 갱신할 게 없는 경우) 영원히 멈추므로
        // 상한을 둔다. 무한 대기로 캡처 자체가 죽는 것보다 장식 한 프레임이 남는 편이 낫다.
        var timeout = new DispatcherTimer(
            TimeSpan.FromMilliseconds(120), DispatcherPriority.Send, (_, _) => frame.Continue = false, _dispatcher);
        hook = (_, _) =>
        {
            System.Windows.Media.CompositionTarget.Rendering -= hook;
            frame.Continue = false;
        };
        System.Windows.Media.CompositionTarget.Rendering += hook;
        timeout.Start();
        Dispatcher.PushFrame(frame);
        timeout.Stop();
        System.Windows.Media.CompositionTarget.Rendering -= hook;
    }

    internal void CancelCaptureSession() => EndCaptureSession();

    private void EndCaptureSession()
    {
        if (_captureOverlay is not null)
        {
            // 마우스가 오버레이(또는 그 액션바 버튼) 위에 있는 채로 HWND를 파괴하면
            // WPF 입력 계층이 죽은 창을 계속 가리키다 다음 마우스 이동에서 Win32 1400으로 터진다.
            // 캐프처는 반드시 버튼 클릭으로 끝나므로 이 경로가 정확히 그 상황이다 (WindowLifetime 참조).
            Shell.WindowLifetime.HideThenClose(_captureOverlay);
        }
        _captureOverlay = null;
        _captureSessionActive = false;
        if (_toolbarRestoreAfterCapture)
        {
            _setToolbarVisible(true);
        }
        // R12: 예외 경로를 포함해 **무조건** 복원한다 — 장식이 안 돌아오면 선택은 살아있는데
        // 핸들이 안 보여 조작할 수 없는 상태가 된다.
        _setDecorationsVisible(true);
        _setSurfacesSuspended(false);
        Log.Info("캡처 세션 정리: 선택 장식 및 서피스 입력 복원");
        _applyZBand();
        ActiveChanged?.Invoke();
    }
}
