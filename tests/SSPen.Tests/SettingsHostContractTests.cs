using System.Reflection;
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
}
