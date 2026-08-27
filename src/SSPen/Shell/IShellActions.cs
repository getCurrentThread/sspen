namespace SSPen.Shell;

/// <summary>툴바가 앱 셸에 위임하는 동작.</summary>
public interface IShellActions
{
    void Undo();

    void ClearAll();

    void StartCapture();

    void OpenSettings();

    /// <summary>핫키 id의 현재 유효 조합 표시 문자열 (재지정 반영). 없으면 null.</summary>
    string? HotkeyLabel(string hotkeyId);

    /// <summary>현재 페이딩 잉크 지속 시간 (초, 0.1~5).</summary>
    double FadingSeconds { get; }

    /// <summary>페이딩 잉크 지속 시간 변경 (툴바 플라이아웃, Epic Pen 대응).</summary>
    void SetFadingDuration(double seconds);
}
