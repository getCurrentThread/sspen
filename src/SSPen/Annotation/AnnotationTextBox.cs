using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;

namespace SSPen.Annotation;

/// <summary>
/// 판서용 텍스트 입력 상자.
/// Windows 10/11에서 펜·스타일러스·터치 입력 시 자판 배열(터치 키보드/필기 패널)이
/// 자동으로 팝업되는 것을 방지하기 위해 <see cref=AutomationPeer/>를 일반 프레임워크 요소 피어로 오버라이드하고
/// 스타일러스 제스처/피드백을 비활성화한다.
/// </summary>
public sealed class AnnotationTextBox : TextBox
{
    public AnnotationTextBox()
    {
        Stylus.SetIsPressAndHoldEnabled(this, false);
        Stylus.SetIsFlicksEnabled(this, false);
        Stylus.SetIsTapFeedbackEnabled(this, false);
        Stylus.SetIsTouchFeedbackEnabled(this, false);
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new FrameworkElementAutomationPeer(this);
}
