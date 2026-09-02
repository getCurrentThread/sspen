namespace SSPen.Annotation;

/// <summary>
/// 변형 1건의 페이로드 (SEL-12): 요소 참조 + 전/후 상태 + 전/후 소유 문서.
/// <c>long Id</c>를 쓰지 않는 이유: 문서에 id 인덱스가 없고, 소유권 복귀에 필요한 것은
/// undo 시점의 소유자가 아니라 **기록 시점의 원래 소유자**라 <paramref name="BeforeOwner"/>를 직접 들어야 한다.
///
/// 수학 파일(<c>TransformMath.cs</c>)이 아니라 자기 파일에 있는 이유: 이 레코드는 <see cref="AnnotationElement"/>와
/// <see cref="AnnotationDocument"/>를 필드로 갖는 원장/이관 도메인의 값이라, 수학 파일에 얹혀 있으면
/// AnnotationElements↔TransformMath 2-사이클과 AnnotationDocument→AnnotationElements→TransformMath→AnnotationDocument
/// 3-사이클이 생긴다 (20단계). 그룹 각도 슬롯이 없는 것은 설계다 (SEL-LIM-6) — 필드는 정확히 다섯 개다.
/// </summary>
public readonly record struct TransformDelta(
    AnnotationElement Element,
    ElementTransformState Before,
    ElementTransformState After,
    AnnotationDocument BeforeOwner,
    AnnotationDocument AfterOwner);
