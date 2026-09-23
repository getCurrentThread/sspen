using SSPen.Shell;

namespace SSPen.Tests;

/// <summary>
/// <see cref="IShellActions"/>의 기록형 가짜 (58단계, A8-5 — <see cref="FakeSettingsHost"/> 선례를 따라 승격).
/// ToolbarStripBuilderTests의 호출 기록판과 ToolbarTooltipsTests의 HotkeyLabel 사전판을 하나로 합쳤다 — 55단계에서
/// HideToolbar/RequestExit가 늘 때 두 사본을 모두 고쳐야 했던 것이 승격 이유다. 호출 기록 문자열은 원본과 글자 그대로 같다.
/// </summary>
internal sealed class FakeShellActions : IShellActions
{
    /// <summary>
    /// 호출 순서 기록 (ToolbarStripBuilderTests 출신 이름: undo, clear-all, capture, settings, hide-toolbar, request-exit, fading:…, status).
    /// ToolbarStripBuilderTests.BuildStrip은 창 콜백도 같은 목록에 적는다 (59단계): toggle-menu, rotate-shapes, rotate-pen, select:…,
    /// toggle-fading, rotate-board.
    /// </summary>
    public List<string> Calls { get; } = [];

    /// <summary>HotkeyLabel이 돌려줄 핫키 id → 표시 문자열 사전 (ToolbarTooltipsTests 출신). 없는 id는 null이다.</summary>
    public Dictionary<string, string> Labels { get; } = [];

    /// <summary>HotkeyLabel 호출 횟수 — 툴팁 둘째 줄을 열릴 때 읽는지(Attach 시점에는 0) 확인한다.</summary>
    public int LabelCalls { get; private set; }

    public double FadingSeconds { get; private set; } = 1.0;

    public void Undo() => Calls.Add("undo");

    public void ClearAll() => Calls.Add("clear-all");

    public void StartCapture() => Calls.Add("capture");

    public void OpenSettings() => Calls.Add("settings");

    public void HideToolbar() => Calls.Add("hide-toolbar");

    public void RequestExit() => Calls.Add("request-exit");

    /// <summary>호출 기록(<see cref="Calls"/>)에는 남기지 않는다 — 툴팁이 여는 시점마다 읽는 조회이지 동작이 아니다.</summary>
    public string? HotkeyLabel(string hotkeyId)
    {
        LabelCalls++;
        return Labels.TryGetValue(hotkeyId, out var label) ? label : null;
    }

    public void SetFadingDuration(double seconds)
    {
        FadingSeconds = seconds;
        Calls.Add($"fading:{seconds}");
    }

    public void ShowStatusReadout() => Calls.Add("status");
}
