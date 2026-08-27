using System.Windows;

namespace SSPen.Annotation;

/// <summary>
/// 요소의 누적 기하 변형 상태 (SEL-1, 리더 결정 LD-1 / 옵션 A3).
/// 자유 <see cref="System.Windows.Media.Matrix"/>가 아니라 구조화 4-튜플로 들고 다닌다:
/// 일반 2×2 행렬은 회전·비등방 스케일로 유일 분해되지 않아 전단(shear)이 끼어들 수 있고
/// (반례 R(30)·S(2,1)·R(30)), 그러면 분해/재합성 왕복이 깨진다. 이 표현에서는 전단이
/// **표현 자체가 불가능**하므로 불변식이 테스트가 아니라 타입으로 참이 된다.
/// 행렬은 렌더·힌트 직전 <see cref="TransformMath.ToMatrix"/> 한 곳에서만 조립되는 파생물이다.
/// Office도 임의 행렬이 아니라 (각, 폭, 높이, 위치)를 저장한다 — MI-1의 직접 근거.
/// </summary>
/// <param name="ScaleX">로컬 X축 배율. 부호는 좌우 뒤집기를 표현한다 (R14).</param>
/// <param name="ScaleY">로컬 Y축 배율. 부호는 상하 뒤집기를 표현한다 (R14).</param>
/// <param name="AngleDegrees">로컬 경계 중심 기준 회전각(도). DPI 이관에서 불변 (ARCH-20).</param>
/// <param name="Translation">월드 공간 평행이동. 위치가 아니라 **변위**다 (ARCH-20).</param>
public readonly record struct ElementTransformState(
    double ScaleX,
    double ScaleY,
    double AngleDegrees,
    Vector Translation)
{
    /// <summary>변형 없음. 신규 요소의 기본값이며 이 상태에서 런타임 동작은 변형 도입 이전과 동일하다.</summary>
    public static readonly ElementTransformState Identity = new(1, 1, 0, default);

    /// <summary>
    /// 등방 근사 배율. 허용 오차를 화면 공간으로 올릴 때 쓴다 (R7).
    /// 극단 종횡비(5:1 이상)에서는 근사이며, QA-5 체감 결과에 따라 축별 보정으로 승격할 수 있다.
    /// </summary>
    public double MeanScale => (Math.Abs(ScaleX) + Math.Abs(ScaleY)) / 2;
}
