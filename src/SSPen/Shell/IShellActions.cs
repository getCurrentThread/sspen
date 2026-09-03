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

    /// <summary>
    /// 현재 도구·굵기·색을 한 줄로 알린다 (<see cref="StatusReadout"/>). 문구 조립과 표시는 합성 루트가
    /// 하고 툴바는 '지금 알려라'만 말한다 — 휠처럼 화면에 흔적이 거의 없는 변경의 유일한 확인 경로다.
    /// </summary>
    void ShowStatusReadout();
}
