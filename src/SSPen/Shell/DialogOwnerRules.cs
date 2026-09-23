namespace SSPen.Shell;

/// <summary>셸 안내 상자를 누구에게 물릴지 (77단계, A1-2).</summary>
public enum DialogOwner
{
    /// <summary>열린 설정창.</summary>
    Settings,

    /// <summary>툴바 — 보이든 숨겨졌든 HWND가 있으면 (99단계).</summary>
    Toolbar,

    /// <summary>owner 없음 — 일반 창으로 뜬다. 툴바 창이 없을 때뿐이다.</summary>
    None,
}

/// <summary>owner 판정에 넘기는 툴바 상태 (99단계).</summary>
public enum ToolbarPresence
{
    /// <summary>툴바 창(HWND)이 없다 — 아직 안 만들었거나 이미 닫혔다.</summary>
    Absent,

    /// <summary>HWND는 살아 있지만 숨겨져 있다 (Alt+Shift+0, "도구 막대 닫기", 캡처 세션).</summary>
    Hidden,

    /// <summary>보인다.</summary>
    Visible,
}

/// <summary>
/// 셸 안내 상자의 owner 선택 규칙 (77단계, A1-2 — 1.3.5가 루트에 더한 판정을 순수 표로 뺐다).
/// 앱 창은 전부 Topmost라 owner 없는 MessageBox는 그 밑으로 숨어 먹통처럼 보인다 — 그래서 설정창, 없으면 툴바를
/// owner로 물려 같은 최상단 층에 올린다. 창 조회(판정 → 실제 Window)는 AppController.DialogOwnerWindow가 한다.
///
/// <b>숨겨진 툴바도 owner다</b> (99단계). 1.3.5의 "숨겨진 툴바는 owner로 못 쓴다"는 기록된 실측이 없는 규칙이었고,
/// 그 탓에 툴바를 숨긴 채 Alt+Shift+7을 누르면 확인 상자가 owner 없이 톱모스트 서피스·핀 밑에 떠 보이지도 눌리지도 않았다.
/// 숨긴 owner가 안전한 이유(스크래치 프로브로 실측 — 숨긴 톱모스트 창 + 전체 화면 NOACTIVATE 서피스 + 실제 z-방어 훅):
/// (1) Win32는 owner를 숨겨도 소유 창을 숨기지 않고(최소화만 숨긴다), owner가 톱모스트라 상자도 WS_EX_TOPMOST로 뜬다.
/// (2) 소유 창은 늘 owner 바로 위에 있다 — <c>WindowStyling.ApplyZBand</c>는 <c>SWP_NOOWNERZORDER</c> 없이 툴바를 놓으므로
/// OS가 상자를 툴바와 함께 옮기고(AGENTS L15의 "IME 창 바로 아래" 삽입과 같은 동작), 툴바는 핀·서피스보다 위인 밴드
/// 멤버라 상자는 밴드 적용 뒤에도 그 위에 남는다. 서피스를 최상단으로 올리는 교란도 AnchorBelow·KeepBelow가 툴바 아래로 돌린다.
/// (3) 상자는 밴드 멤버가 아니지만 <c>ZOrderInvariant.IsOrdered</c>는 멤버 사이에 남의 창이 끼는 것을 허용하므로
/// 검증기·폴러(<c>ZBandVerifier.Repair</c>/<c>ZBandPoller</c>)가 헛된 정정을 돌지 않는다.
/// (4) 모달이 끝나면 owner는 다시 Enable되고 숨김 그대로다(Win32 IsWindowVisible·WPF IsVisible 모두 거짓).
/// 캡처 세션 중에 뜬 상자는 툴바 바로 위라 오버레이 밑에 있다가 세션이 끝나면 보인다(추론 — 예전에는 끝난 뒤에도 서피스 밑이었다).
/// 누가 다시 '보이는 툴바만'으로 좁히면 <c>DialogOwnerRulesTests</c>가 잡는다.
/// </summary>
public static class DialogOwnerRules
{
    /// <summary>우선순위: 설정창 &gt; 툴바(보이든 숨겨졌든) &gt; 없음(툴바 창이 없을 때).</summary>
    public static DialogOwner Choose(bool settingsOpen, ToolbarPresence toolbar) =>
        settingsOpen ? DialogOwner.Settings
        : toolbar != ToolbarPresence.Absent ? DialogOwner.Toolbar
        : DialogOwner.None;
}
