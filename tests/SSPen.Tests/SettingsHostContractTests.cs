using System.Reflection;
using SSPen.Settings;
using SSPen.Shell;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// <see cref="ISettingsHost"/> 계약의 트립와이어 (57단계, A1-5). 55단계에 설정 창의 종료 버튼이 툴바 설정 메뉴로 옮겨 간 뒤
/// <c>ExitApp</c>은 인터페이스를 거치는 호출자가 0건인 채 남아 있었다 — 트레이와 업데이트 재시작은 합성 루트가
/// <c>AppController.ExitApp</c> 메서드 그룹을 직접 넘긴다. 죽은 멤버가 되살아나면 가짜 호스트까지 다시 구현해야 하고,
/// 종료 경로 문서가 또 실제 배선과 어긋난다.
/// </summary>
public class SettingsHostContractTests
{
    [Fact]
    public void ISettingsHost_DoesNotDeclareExitApp()
    {
        var method = typeof(ISettingsHost).GetMethod("ExitApp", BindingFlags.Public | BindingFlags.Instance);

        Assert.Null(method);
    }

    /// <summary>
    /// 재지정 반영은 일괄 한 건이다 (79단계, A6-3). 건별 <c>RemapHotkey(id, def)</c>가 되살아나면 창이 다시 보류분마다 부르게 되고,
    /// 건마다 저장·전체 재등록이 따라오며 맞바꾸기의 중간 충돌이 가짜 트레이 경고를 띄운다.
    /// </summary>
    [Fact]
    public void ISettingsHost_RemapsAsOneBatch_NoPerItemMember()
    {
        var perItem = typeof(ISettingsHost).GetMethod("RemapHotkey", BindingFlags.Public | BindingFlags.Instance);
        var batch = typeof(ISettingsHost).GetMethod("RemapHotkeys", BindingFlags.Public | BindingFlags.Instance);

        Assert.Null(perItem);
        Assert.NotNull(batch);
        Assert.Equal(
            [typeof(IReadOnlyList<(string Id, HotkeyDef Def)>)],
            batch.GetParameters().Select(parameter => parameter.ParameterType));
    }
}
