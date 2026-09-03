using System.Reflection;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 활성 글리프의 폰트 선택 증인 (<see cref="ToolbarStateMap.GlyphFontIsFilled"/>).
///
/// 아이콘 표에는 Filled 코드포인트가 없어 <c>Pair(x, x)</c>로 같은 값을 두 번 적은 항목이 여럿 있다.
/// 그 코드포인트를 Filled <b>폰트</b>로 그리면 그 폰트에 없는 글리프라 두부(.notdef)가 나온다 —
/// 활성일 때만 아이콘이 깨지는 증상이다. 리플렉션으로 표 전체를 훑어 규칙을 잠근다.
/// </summary>
public class IconGlyphFontTests
{
    private static IEnumerable<(string Name, (string Regular, string Filled) Icon)> Table() =>
        typeof(Icons)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof((string Regular, string Filled)))
            .Select(f => (f.Name, ((string Regular, string Filled))f.GetValue(null)!));

    [Fact]
    public void Table_IsNotEmpty() => Assert.NotEmpty(Table());

    /// <summary>같은 쌍이면 활성이어도 Regular 폰트를 유지한다 — 이것이 고친 버그다.</summary>
    [Fact]
    public void IdenticalPairs_NeverResolveToTheFilledFont()
    {
        var identical = Table().Where(e => e.Icon.Regular == e.Icon.Filled).ToList();

        Assert.NotEmpty(identical); // 전제: 실제로 그런 항목이 있다 (없어지면 이 감시가 무의미)
        Assert.All(identical, e => Assert.False(
            ToolbarStateMap.GlyphFontIsFilled(e.Icon, active: true),
            $"{e.Name}은(는) Regular==Filled인데 Filled 폰트로 해석됐다"));
    }

    /// <summary>다른 쌍은 활성일 때 Filled로 간다 (기존 동작 보존).</summary>
    [Fact]
    public void DistinctPairs_UseTheFilledFontWhenActive() =>
        Assert.All(
            Table().Where(e => e.Icon.Regular != e.Icon.Filled),
            e => Assert.True(ToolbarStateMap.GlyphFontIsFilled(e.Icon, active: true)));

    /// <summary>비활성이면 언제나 Regular다.</summary>
    [Fact]
    public void Inactive_IsAlwaysRegular() =>
        Assert.All(Table(), e => Assert.False(ToolbarStateMap.GlyphFontIsFilled(e.Icon, active: false)));
}
