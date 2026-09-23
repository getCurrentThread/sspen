using SSPen.Interop;

namespace SSPen.Pin;

/// <summary>
/// 핀 휠 확대/축소를 창에 적용하는 흐름 (82단계, 사용자 신고 2026-09-23: "휠로 확대/축소할 때마다 덜덜 떨린다").
/// 수학은 순수 <see cref="PinZoom"/>이 갖고, OS 경계(창 사각형·커서 읽기, 창 적용, 디스패처 예약)는 주입 이음매다 —
/// <see cref="PinWindow"/>는 이음매를 이어 주는 얇은 어댑터로 남고, 흐름 전체는 헤드리스로 증인을 세운다.
///
/// 떨림의 실측 원인과 대응:
/// <list type="number">
///   <item>Left·Top·Width·Height를 따로 대입해 칸마다 <c>SetWindowPos</c>가 네 번(이동 X, 이동 Y, 폭, 높이) 나갔고,
///     레이어드 창에 이동만·크기만 바뀐 중간 사각형이 비쳤다 → 적용은 위치+크기 한 번(<c>moveResize</c>)이다.</item>
///   <item>WPF가 WM_MOVE로 되쓴 반올림된 Left/Top에서 다음 칸을 계산해 오차가 쌓였다(10칸에 3px) → 상태는 반올림하지 않은
///     이상적 사각형(<see cref="Ideal"/>)이고 정수 사각형은 적용할 때만 만든다.</item>
///   <item>물리 px인 캡처 크기를 DIP로 썼다(100%가 아닌 배율에서 첫 칸이 배율만큼 튀고 원래 크기 복귀가 어긋남 — 이 PC는 전
///     모니터 96 DPI라 실측 불가, 헤드리스 증인) → 모든 값은 물리 픽셀이다. 커서는 화면 물리 좌표(<c>GetCursorPos</c>),
///     창은 <c>GetWindowRect</c>에서 읽는다. <c>e.GetPosition</c>은 WPF가 캐시한 클라이언트 좌표라(입력 공급자는 휠 좌표가
///     마지막 명시 이동과 같으면 캐시를 갱신하지 않는다 — 소스 판독) 창이 커서 아래에서 움직이는 이 흐름에서는 기대지 않는다.</item>
///   <item>대기 중인 휠 여러 칸은 예약 하나로 묶는다(<c>postCoalesced</c> — 어댑터가 입력이 남아 있으면 기다리는 우선순위로 예약한다).
///     칸마다의 배율·고정점 계산은 그대로 칸마다 하고, 창 적용만 한 번이다.</item>
/// </list>
/// 줌 밖의 창 변화(드래그 이동, DPI가 다른 모니터로 옮길 때의 크기 조정)는 다음 입력 때 마지막 적용 사각형과
/// 실제 창을 비교해 흡수한다(<see cref="PinZoom.Resync"/>). 적용 자체가 DPI 경계를 넘긴 경우(95단계)는 적용 직후 같은
/// Resync로 보이는 배율을 채택한다 — 두 경로가 같은 의미다. 적용 뒤 크기 차이를 OS 크기 제한으로 보는 것은 DPI가 그대로일 때뿐이다.
/// </summary>
public sealed class PinZoomController
{
    private readonly double _baseWidth;
    private readonly double _baseHeight;
    private readonly Func<PhysicalRect?> _windowRect;
    private readonly Func<(int X, int Y)?> _cursor;
    private readonly Func<PhysicalRect, bool> _moveResize;
    private readonly Action<Action> _postCoalesced;
    private readonly Action _applied;
    private PinZoomResult _ideal;
    private PhysicalRect _lastApplied;
    private bool _applyPending;

    /// <param name="region">캡처 영역 (물리 px) — 핀이 처음 놓이는 사각형이자 배율 1.0의 기준 크기.</param>
    /// <param name="windowRect">지금 창 사각형 (물리 px, <c>GetWindowRect</c>). 창이 없으면 <c>null</c> — 아무것도 하지 않는다.</param>
    /// <param name="cursor">커서의 화면 물리 좌표 (<c>GetCursorPos</c>). 읽지 못하면 <c>null</c> — 그 칸은 버린다.</param>
    /// <param name="moveResize">위치+크기를 한 번에 적용하고(<see cref="WindowStyling.MoveResizePhysical"/>), 그 적용 안에서 창의
    /// DPI가 바뀌었는지 돌려준다(적용 전후 DPI 비교, 95단계). 적용 뒤 크기가 목표와 다를 때 DPI 전환(보이는 배율을 채택)과
    /// OS 크기 제한(이상적 배율을 지킴)을 가르는 유일한 근거다.</param>
    /// <param name="postCoalesced">적용을 예약한다. 대기 중인 입력이 모두 처리된 뒤에 돌아야 버스트가 묶인다.</param>
    /// <param name="applied">적용이 끝났다 — 어댑터가 크롬(배율 표시·크기 판정)을 새로 그린다.</param>
    public PinZoomController(
        PhysicalRect region,
        Func<PhysicalRect?> windowRect,
        Func<(int X, int Y)?> cursor,
        Func<PhysicalRect, bool> moveResize,
        Action<Action> postCoalesced,
        Action applied)
    {
        _baseWidth = Math.Max(region.Width, PinZoom.MinBaseExtent);
        _baseHeight = Math.Max(region.Height, PinZoom.MinBaseExtent);
        _windowRect = windowRect;
        _cursor = cursor;
        _moveResize = moveResize;
        _postCoalesced = postCoalesced;
        _applied = applied;
        _ideal = new PinZoomResult(1.0, region.X, region.Y, _baseWidth, _baseHeight);
        // PinManager.CreatePin이 캡처 영역 그대로 배치한다(PlacePhysical) — 그 사각형을 마지막 적용으로 본다.
        _lastApplied = region;
    }

    /// <summary>배율 1.0의 폭 (물리 px, 최소 <see cref="PinZoom.MinBaseExtent"/>).</summary>
    public double BaseWidth => _baseWidth;

    /// <summary>배율 1.0의 높이 (물리 px).</summary>
    public double BaseHeight => _baseHeight;

    /// <summary>지금 배율 — 적용 전이라도 마지막 칸까지 반영된 값이다.</summary>
    public double Scale => _ideal.Scale;

    /// <summary>반올림하지 않은 이상적 사각형 (물리 px).</summary>
    public PinZoomResult Ideal => _ideal;

    /// <summary>적용 예약이 걸려 있는가 (버스트 병합 중).</summary>
    public bool ApplyPending => _applyPending;

    /// <summary>휠 한 칸 — 커서 아래 이미지 점을 고정해 배율을 바꾸고 적용을 예약한다.</summary>
    public void Wheel(int delta)
    {
        if (_windowRect() is not { } window || _cursor() is not { } cursor)
        {
            return;
        }
        Resync(window);
        _ideal = PinZoom.ZoomAtCursor(
            _ideal, delta, _baseWidth, _baseHeight, cursor.X - _ideal.Left, cursor.Y - _ideal.Top);
        Schedule();
    }

    /// <summary>원래 크기(100%)로 — 중심 고정, 물리 기준 크기. 휠과 같은 적용 경로를 탄다.</summary>
    public void Reset()
    {
        if (_windowRect() is not { } window)
        {
            return;
        }
        Resync(window);
        _ideal = PinZoom.ResetToOriginal(_ideal, _baseWidth, _baseHeight);
        Schedule();
    }

    private void Resync(PhysicalRect window)
    {
        _ideal = PinZoom.Resync(_ideal, _lastApplied, window, _baseWidth);
        _lastApplied = window;
    }

    private void Schedule()
    {
        if (_applyPending)
        {
            return; // 이미 예약된 적용이 마지막 칸까지 한 번에 바른다.
        }
        _applyPending = true;
        _postCoalesced(Apply);
    }

    private void Apply()
    {
        _applyPending = false;
        if (_windowRect() is not { } window)
        {
            return; // 예약과 적용 사이에 창이 닫혔다.
        }
        // 예약과 적용 사이에 창이 밖에서 움직였으면(드래그가 막 시작됨 등) 그만큼 이상적 사각형을 옮긴 뒤 바른다.
        Resync(window);
        var target = PinZoom.ToPhysicalRect(_ideal);
        if (target != window)
        {
            bool dpiChanged = _moveResize(target);
            var landed = _windowRect() ?? target;
            if (dpiChanged)
            {
                // 이 적용이 핀을 DPI가 다른 모니터로 넘겼고 WPF가 권장 사각형(DPI 비율만큼 큰/작은 크기)을 적용했다 (95단계).
                // 그 크기는 보이는 배율이다 — 드래그로 경계를 넘은 경로가 다음 입력의 Resync에서 하는 것과 같이 보이는 창에서
                // 다시 잡는다. 크롬 새로 그리기(_applied)가 그 배율을 보도록 먼저 한다.
                _ideal = PinZoom.Resync(_ideal, target, landed, _baseWidth);
            }
            // DPI가 그대로인데 크기가 다르면 OS가 크기를 제한한 것이다 — 이상적 사각형은 지키고 실제 결과를 마지막 적용으로
            // 삼는다. 목표를 적으면 다음 칸이 그 차이를 "밖에서 바뀜"으로 오판해 이상적 사각형을 버린다.
            _lastApplied = landed;
        }
        _applied();
    }
}
