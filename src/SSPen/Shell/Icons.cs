using System.Windows.Media;

namespace SSPen.Shell;

/// <summary>
/// Fluent UI System Icons (Microsoft, MIT) 레지스트리 — 스펙 고정 F21.
/// Epic Pen 아이콘 자산은 저작권물이므로 절대 복사하지 않는다.
/// regular = 기본 상태, filled = 선택 상태.
/// 코드포인트는 동봉한 FluentSystemIcons TTF (main 브랜치)와 일치한다.
/// </summary>
public static class Icons
{
    // 폰트는 지연 생성: pack:// 스킴은 WPF Application 초기화가 등록하므로, 즉시 생성하면
    // 헤드리스 유닛테스트(Application 부재)에서 글리프 쌍 참조만으로 타입 초기화가 실패한다.
    private static FontFamily? _regular;
    private static FontFamily? _filled;

    public static FontFamily Regular =>
        _regular ??= new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#FluentSystemIcons-Regular");

    public static FontFamily Filled =>
        _filled ??= new(new Uri("pack://application:,,,/"), "./Assets/Fonts/#FluentSystemIcons-Filled");

    // (regular, filled) 코드포인트 쌍 — 스펙 아이콘 표.
    public static readonly (string Regular, string Filled) Eye = Pair(0xe5f3, 0xe600);          // 표시(펼침): eye-24
    public static readonly (string Regular, string Filled) EyeOff = Pair(0xe5f6, 0xe603);       // 표시(접힘): eye-off-24 — 토글 가시성 (사용자 조타)
    public static readonly (string Regular, string Filled) Cursor = Pair(0xe444, 0xe450);       // 클릭 통과: cursor-24
    public static readonly (string Regular, string Filled) Pen = Pair(0xe8d8, 0xe8ea);          // 펜: pen-24
    public static readonly (string Regular, string Filled) Highlight = Pair(0xf47d, 0xf481);    // 형광펜: highlight-24
    public static readonly (string Regular, string Filled) Eraser = Pair(0xe5e5, 0xe5f2);       // 지우개: eraser-24
    public static readonly (string Regular, string Filled) Select = Pair(0xf698, 0xf6a1);       // 필기내용 선택: select-object-24
    public static readonly (string Regular, string Filled) Shapes = Pair(0xf6ae, 0xf6b7);       // 도형: shapes-24
    public static readonly (string Regular, string Filled) TextT = Pair(0xed64, 0xed64);        // 텍스트: text-t-24
    public static readonly (string Regular, string Filled) ArrowUndo = Pair(0xf19a, 0xf19a);    // 실행 취소: arrow-undo-24
    public static readonly (string Regular, string Filled) Delete = Pair(0xf34d, 0xf34d);       // 전체 지우기: delete-24
    public static readonly (string Regular, string Filled) Whiteboard = Pair(0xf8ab, 0xf8c3);   // 화이트보드: whiteboard-24
    public static readonly (string Regular, string Filled) Camera = Pair(0xf255, 0xf255);       // 캡처: camera-24 — 식별성 (사용자 조타, Epic Pen도 카메라)
    // 설정: settings-24 (톱니바퀴). 사용자 요청 17차로 options-24(슬라이더 글리프)에서 교체 —
    // 슬라이더는 "조절", 톱니바퀴는 "설정"으로 읽힌다.
    public static readonly (string Regular, string Filled) Settings = Pair(0xf6aa, 0xf6b3);
    public static readonly (string Regular, string Filled) Timer = Pair(0xf827, 0xf840);        // 페이딩 잉크: timer-24

    // 도형 플라이아웃 보조 아이콘
    public static readonly (string Regular, string Filled) Line = Pair(0xe766, 0xe774);         // 선: line-24
    public static readonly (string Regular, string Filled) ArrowUpRight = Pair(0xf1a3, 0xf1a3); // 화살표: arrow-up-right-24
    public static readonly (string Regular, string Filled) Square = Pair(0xeb76, 0xeb7f);       // 사각형: square-24
    public static readonly (string Regular, string Filled) Circle = Pair(0xf2bc, 0xf2bc);       // 타원: circle-24

    private static (string, string) Pair(int regular, int filled) =>
        (char.ConvertFromUtf32(regular), char.ConvertFromUtf32(filled));
}
