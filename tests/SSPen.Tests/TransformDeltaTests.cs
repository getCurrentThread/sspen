using System.Reflection;
using SSPen.Annotation;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="TransformDelta"/> 형태의 트립와이어 (20단계, SEL-12, SEL-LIM-6).
/// 원장 페이로드는 요소 참조·전/후 상태·전/후 소유 문서의 다섯 필드이고 그룹 각도 슬롯이 없다 —
/// 그룹 회전 포즈를 여기 실으면 undo가 복원할 수 없는 값이 원장에 들어간다 (AGENTS "SEL-LIM-6").
/// </summary>
public class TransformDeltaTests
{
    [Fact]
    public void Shape_HasExactlyFiveFields_NoGroupAngleSlot_ByReflection()
    {
        var properties = typeof(TransformDelta)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => (p.Name, p.PropertyType))
            .OrderBy(p => p.Name)
            .ToArray();

        Assert.Equal(
            new[]
            {
                ("After", typeof(ElementTransformState)),
                ("AfterOwner", typeof(AnnotationDocument)),
                ("Before", typeof(ElementTransformState)),
                ("BeforeOwner", typeof(AnnotationDocument)),
                ("Element", typeof(AnnotationElement)),
            },
            properties);
        Assert.DoesNotContain(properties, p => p.PropertyType == typeof(double) || p.PropertyType == typeof(GroupFrame));
    }

    [Fact]
    public void TransformDelta_IsAValueRecord_NotHoldingAnId()
    {
        Assert.True(typeof(TransformDelta).IsValueType);
        Assert.Null(typeof(TransformDelta).GetProperty("Id"));
    }
}
