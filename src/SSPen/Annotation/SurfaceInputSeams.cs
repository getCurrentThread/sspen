using System.Windows;

namespace SSPen.Annotation;

// 27단계: 컨트롤러 파일에 함께 있던 두 경계 타입을 자기 파일로 옮겼다 (글자 그대로). 둘 다 하네스가 직접 구현·생성하는
// 주입 경계라 컨트롤러 본문과 수명이 다르다. 'required' 는 설계다 — 편의상 기본값을 붙이면 배선 누락이 컴파일 에러가 아니라
// 조용한 다른 코드 경로(Rect.Empty)나 첫 노치의 NRE가 된다 (12·13단계 교훈). SurfaceInputSeamsTests 가 리플렉션으로 고정한다.

/// <summary>
/// <see cref="ContentSurfaceWindow"/>가 자신에게 위임해야 하는 최소 창 조작 집합.
/// ARCH-2 텍스트 도구 NOACTIVATE 핸드셰이크와 ARCH-6 마우스 캡처만 창에 위임하고,
/// 그 외 입력 상태 머신은 <see cref="SurfaceInputController"/>가 창 참조 없이 소유한다.
/// </summary>
public interface ISurfaceHost
{
    void SetNoActivate(bool on);
    void ActivateWindow();
    void CaptureMouse();
    void ReleaseMouseCapture();
    DpiScale GetDpi();
}

/// <summary>
/// 컨트롤러가 WPF 비주얼 트리 없이는 스스로 알 수 없는 값들의 주입 지점.
/// 시계는 <see cref="WheelScaleSession"/>와 <see cref="FadeSchedulerCore"/>가 이미 주입받는데
/// 컨트롤러만 그 경계에서 <c>DateTime.UtcNow</c>로 다시 하드코딩하고 있었고,
/// 서피스 논리 경계는 창이 유일 소유자인데 컨트롤러가 <b>따로 한 벌 더</b> 계산하고 있었다 (R5).
/// </summary>
public sealed record SurfaceInputSeams
{
    /// <summary>
    /// 주입 시계. 휠 노치 코얼레싱의 450ms 유휴 판정과 페이드 예약 마감이 전부 이 값에서 나온다 (R7).
    /// 프로덕션 값을 <b>충실히 감싸므로</b> 기본값이 정당하다 — 배선을 빠뜨려도 동작이 달라지지 않는다.
    /// </summary>
    public Func<DateTime> Now { get; init; } = () => DateTime.UtcNow;

    /// <summary>
    /// 서피스 논리 경계 (R5). 렌더(<c>SurfaceDecorationPlanner.Plan</c>, 창이 <c>RedrawDecorations</c>에서 값을 넘긴다)와 힌트(히트 테스트)가
    /// <b>같은 값</b>을 써야 "그려지는 위치 == 잡히는 위치"가 성립한다. 창이 유일 소유자이므로
    /// 충실한 프로덕션 기본값이 없다 — 그래서 <c>required</c>다.
    ///
    /// 기본값을 두면 안 되는 이유: <see cref="Rect.Empty"/>는 "경계 없음"이 아니라
    /// <see cref="TransformMath.ClampRotateHandle"/>의 <b>다른 코드 경로</b>("클램프하지 않음")다.
    /// 배선을 빠뜨린 테스트가 조용히 프로덕션과 다른 경로를 타는 대신 컴파일 에러가 나야 한다.
    ///
    /// <b><see cref="Rect"/> 값이 아니라 <see cref="Func{TResult}"/>인 이유</b>: 창 생성 시점에는
    /// 아직 measure/arrange가 돌지 않아 <c>ActualWidth/ActualHeight</c>가 0이다. 그때 값을 얼려 두면
    /// <c>new Rect(0,0,0,0)</c>이 되는데 이것은 <c>IsEmpty == false</c>라 클램프 경로로 들어가고,
    /// <c>left &gt; right</c> 분기가 중심점을 돌려주어 <b>모든 회전 핸들이 (0,0)으로 붕괴</b>한다.
    /// 매 히트 테스트마다 호출한다 — 필드에 캐시하지 말 것.
    /// </summary>
    public required Func<Rect> SurfaceBounds { get; init; }

    /// <summary>
    /// 휠 유휴 디바운스 (R7). 창이 유일 소유자이므로 충실한 프로덕션 기본값이 없다 — 그래서 <c>required</c>다.
    /// 기본값을 두면 배선 누락이 컴파일 에러가 아니라 첫 노치의 NRE가 된다.
    /// </summary>
    public required IIdleScheduler IdleScheduler { get; init; }
}
