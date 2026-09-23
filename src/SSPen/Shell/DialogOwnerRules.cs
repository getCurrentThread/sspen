namespace SSPen.Shell;

/// <summary>셸 안내 상자를 누구에게 물릴지 (77단계, A1-2).</summary>
public enum DialogOwner
{
    /// <summary>열린 설정창.</summary>
    Settings,

    /// <summary>보이는 툴바.</summary>
    Toolbar,

    /// <summary>owner 없음 — 일반 창으로 뜬다.</summary>
    None,
}

/// <summary>
/// 셸 안내 상자의 owner 선택 규칙 (77단계, A1-2 — 1.3.5가 루트에 더한 판정을 순수 표로 뺐다).
/// 앱 창은 전부 Topmost라 owner 없는 MessageBox는 그 밑으로 숨어 먹통처럼 보인다 — 그래서 설정창, 없으면 보이는 툴바를
/// owner로 물려 같은 최상단 층에 올린다. <b>숨겨진 툴바는 owner로 못 쓴다</b>(실측 교훈) — 누가 '툴바면 된다'로 단순화하면
/// <c>DialogOwnerRulesTests</c>가 잡는다. 창 조회(판정 → 실제 Window)는 AppController.DialogOwnerWindow가 한다.
/// </summary>
public static class DialogOwnerRules
{
    /// <summary>우선순위: 설정창 &gt; 보이는 툴바 &gt; 없음.</summary>
    public static DialogOwner Choose(bool settingsOpen, bool toolbarVisible) =>
        settingsOpen ? DialogOwner.Settings
        : toolbarVisible ? DialogOwner.Toolbar
        : DialogOwner.None;
}
