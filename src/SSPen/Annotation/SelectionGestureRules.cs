using System.Windows;

namespace SSPen.Annotation;

/// <summary>
/// 선택 제스처의 순수 판정 규칙과 입력 거리 임계 (R2/R5/R6/R7). UI와 분리되어 헤드리스 유닛 테스트 대상이다.
///
/// 이 파일의 존재 이유는 <b>R5의 트리거 위치</b>다. "선택이 취소되면 클릭 통과"를
/// <see cref="SelectionModel.SelectionChanged"/>에 걸면 안 된다 — 그 이벤트는 원인을 싣지 않는
/// 무인자 이벤트인데 선택집합이 비는 경로는 6개(빈 곳 클릭·ESC·선택 삭제·도구 전환·전체 지우기·
/// 요소 소멸)나 되고, 그중 <b>도구 전환</b>에 걸리면 펜 버튼을 눌러도 곧바로
/// <c>ClickThrough=true → ActiveTool=None</c>으로 되돌아가 도구를 아예 고를 수 없게 된다.
/// 따라서 트리거는 사용자의 <b>명시적 해제 제스처 3곳</b>(제자리 클릭 / ESC / 선택 삭제 완료)에만 둔다.
/// </summary>
public static class SelectionGestureRules
{
    /// <summary>
    /// '제자리 클릭'과 '드래그'를 가르는 논리 픽셀 거리 (R2).
    /// 도형 커밋이 쓰던 3px 임계와 같은 값이었고, 이제 <c>SurfaceInputController.CommitShape</c>가
    /// 이 상수를 직접 읽는다 — 두 값이 다시 갈라질 수 없다.
    /// </summary>
    public const double ClickThresholdPixels = 3;

    /// <summary>
    /// 선택 히트 허용 오차 (지우개와 같은 감각으로 통일). 논리 픽셀 (R6).
    /// <see cref="SelectionGeometry.HitForSelect"/>에 먹인다.
    ///
    /// <see cref="EraseHitTolerancePixels"/>와 <b>오늘은 값이 같지만 같은 상수가 아니다.</b>
    /// 두 경로는 랭킹 규칙이 의도적으로 다르다 — 지우개는 '가장 가까운 것',
    /// 선택은 '가장 위 → 면적 최소'다 (<see cref="SelectionGeometry.HitTopmost"/> 문서).
    /// 하나로 합치면 지우개 감도를 만질 때 선택 감도가 조용히 따라 움직인다.
    /// </summary>
    public const double SelectHitTolerancePixels = 6;

    /// <summary>
    /// 지우개 히트 허용 오차. 논리 픽셀.
    /// <see cref="AnnotationDocument.HitTestNearest"/>에 먹인다.
    /// 선택 쪽 값(<see cref="SelectHitTolerancePixels"/>)과 오늘 같은 이유는 감각 통일이지
    /// 같은 양이기 때문이 아니다 — 독립 조정 가능해야 한다.
    /// </summary>
    public const double EraseHitTolerancePixels = 6;

    /// <summary>
    /// 눌렀다 뗀 것이 <b>드래그가 아니라 제자리 클릭</b>인가.
    ///
    /// 왜 마우스 다운이 아니라 업에서 판정하는가: 마퀴 선택은 빈 곳 마우스 다운으로 시작한다.
    /// 다운 시점에 클릭 통과를 켜면 <c>IsInteractive</c>가 false로 떨어져 진행 중인 마퀴가
    /// 얼어붙고(입력 이동 가드), 버튼 업은 이미 서피스에 도달하지 못해 <b>보이지 않는 사각형이
    /// 조작 불가능한 선택을 만든다</b>. 업에서 판정하면 마퀴가 온전히 살아남는다.
    /// </summary>
    public static bool IsStationaryClick(Point down, Point up) =>
        (up - down).Length < ClickThresholdPixels;

    /// <summary>
    /// 빈 곳 제스처가 끝났을 때 클릭 통과로 전환해야 하는가 (R2 + R5).
    /// <b>선택이 실제로 있었을 때만</b> 참이다 — 아무것도 안 고른 상태에서 빈 화면을 톡 누른 것까지
    /// 통과로 흡수하면, 선택 도구를 켜자마자 도구가 해제되어 아무것도 고를 수 없게 된다.
    /// </summary>
    public static bool ShouldEngageClickThrough(bool hadSelection, Point down, Point up) =>
        hadSelection && IsStationaryClick(down, up);

    /// <summary>
    /// 선택 프레임 <b>내부</b> 클릭을 '이동'으로 볼 것인가 (R6).
    ///
    /// 프레임 내부를 무조건 이동으로 먹으면 <b>큰 선택이 그 안의 모든 요소를 영구히 가린다</b>:
    /// 화면을 가로지르는 대각선 획 하나만 골라도 그 축 정렬 프레임이 화면 대부분을 덮으므로,
    /// 그 아래 어떤 요소도 다시는 클릭으로 고를 수 없다. 그래서 프레임 안이라도 커서 밑에
    /// <b>선택되지 않은 다른 요소</b>가 있으면 그 요소를 고르는 쪽에 양보한다.
    /// 프레임 내부의 빈 자리와 이미 선택된 요소 위에서는 이동이 그대로 살아남는다.
    /// </summary>
    public static bool ShouldMoveFromFrameInterior(bool insideFrame, bool hitExists, bool hitIsSelected) =>
        insideFrame && (!hitExists || hitIsSelected);

    /// <summary>
    /// 휠 확대의 고정점 (R7, 하이브리드): 커서가 선택 프레임 안이면 커서, 밖이면 프레임 중심.
    ///
    /// 왜 하이브리드인가: 커서 고정만 쓰면(핀 줌과 같은 감각) 커서가 그룹에서 멀 때 몇 노치 만에
    /// 선택이 화면 밖으로 튀어나간다. 중심 고정만 쓰면 확대하려는 지점이 계속 달아난다.
    /// 프레임 포함 여부로 갈라야 두 실패 모드가 동시에 사라진다.
    /// </summary>
    public static Point WheelPivot(Rect frame, Point cursor) =>
        frame.Contains(cursor) ? cursor : new Point(frame.X + (frame.Width / 2), frame.Y + (frame.Height / 2));
}

/// <summary>
/// 휠 확대/축소 1회분 세션 (R7). 시계를 주입받는 순수 상태 머신이라 헤드리스로 검증된다
/// (<c>FadeSchedulerCore</c>와 같은 분리 관례).
///
/// 존재 이유는 <b>실행취소 단위</b>다. <see cref="UndoLedger"/>는 append 전용이고 항목 수정 API가
/// 없으므로, 노치마다 <c>RecordTransform</c>을 부르면 휠 10노치가 실행취소 10번이 되어
/// "변형 1회 = 원장 1항목"(f3/SEL-12) 규약이 깨진다. 그래서 연속 노치를 하나의 세션으로 모으고
/// 휠이 멎은 뒤 <see cref="DueToCommit"/>가 참이 될 때 한 항목으로 싣는다.
/// </summary>
public sealed class WheelScaleSession
{
    /// <summary>노치 1회 배율. 핀 줌(<c>PinZoom</c>)과 같은 감각으로 맞춘다.</summary>
    public const double NotchFactor = 1.1;

    /// <summary>마지막 노치 이후 이만큼 조용하면 한 번의 변형으로 확정한다.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMilliseconds(450);

    private DateTime _lastStep;

    /// <summary>세션 진행 중인가.</summary>
    public bool Active { get; private set; }

    /// <summary>세션 시작 이후 누적 배율 (확정 시 이 값이 시작 상태에 곱해진 결과가 이미 화면에 있다).</summary>
    public double Factor { get; private set; } = 1;

    /// <summary>세션 시작 시점에 <b>동결</b>된 고정점. 노치마다 다시 계산하면 커서가 흔들릴 때 선택이 표류한다.</summary>
    public Point Pivot { get; private set; }

    /// <summary>세션 시작. 이미 진행 중이면 고정점을 유지한 채 무시한다 (동결 규약).</summary>
    public void Begin(Point pivot, DateTime now)
    {
        if (Active)
        {
            return;
        }
        Active = true;
        Factor = 1;
        Pivot = pivot;
        _lastStep = now;
    }

    /// <summary>노치 적용. 반환값은 세션 시작 상태에 곱해야 할 <b>누적</b> 배율이다.</summary>
    public double Step(int notches, DateTime now)
    {
        if (!Active)
        {
            return Factor;
        }
        Factor *= Math.Pow(NotchFactor, notches);
        _lastStep = now;
        return Factor;
    }

    /// <summary>
    /// 클램프된 누적 배율을 세션에 <b>되먹인다</b> (R7).
    ///
    /// 없으면 데드존이 생긴다: <c>Factor</c>는 한계를 모른 채 계속 누적되는데 화면에 적용되는 값은
    /// <see cref="TransformMath.ClampGroupFactor"/>가 자른 값이라, 천장에 닿은 뒤 20노치를 더 굴리면
    /// 반대로 20노치를 굴려야 비로소 반응이 돌아온다. 되먹이면 천장에서 첫 역방향 노치부터 즉시 반응한다.
    /// </summary>
    public void SetFactor(double factor)
    {
        if (Active)
        {
            Factor = factor;
        }
    }

    /// <summary>휠이 멎어 확정할 때가 되었는가.</summary>
    public bool DueToCommit(DateTime now) => Active && now - _lastStep >= IdleTimeout;

    /// <summary>세션 종료 (확정 또는 취소).</summary>
    public void End()
    {
        Active = false;
        Factor = 1;
    }
}
