namespace SSPen.Annotation;

// 28단계: AppState.cs 머리에 있던 도구 어휘 4종을 자기 파일로 옮겼다 (글자 그대로). 상태(AppState)와 어휘(enum)는 수명이
// 다르다 — 어휘는 ToolbarStateMap(29회)·SurfaceInputRouter(13회) 등 11파일이 참조하는 리프다. 열거 순서와 주석은 계약이다
// (ToolKind.Select는 말단에 추가 — SEL-4; ToolKindRulesTests가 리플렉션으로 고정).

public enum ToolKind
{
    None,
    Pen,
    Highlighter,
    Eraser,
    Line,
    Arrow,
    Rectangle,
    Ellipse,
    Table,
    Text,
    Select,    // 필기내용선택 (SEL-4): 어떤 ToolStyleGroup에도 속하지 않는다 (f12). 열거 말단에 추가.
}

/// <summary>굵기 5단계 (사용자 조타: Epic Pen 크기 선택기 5점 대응).</summary>
public enum ThicknessStep
{
    XSmall,
    Small,
    Medium,
    Large,
    XLarge,
}

public enum BoardMode
{
    None,
    White,
    Black,
}

/// <summary>색·굵기를 개별 보유하는 도구 그룹 (사용자 조타: 펜/형광펜/도형 개별 스타일).</summary>
public enum ToolStyleGroup
{
    Pen,
    Highlighter,
    Shape,
}
