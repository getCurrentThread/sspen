using System.Windows.Media;

namespace SSPen.Annotation;

/// <summary>
/// 색 테이블 단독 소유 (사용자 요청 17차: 바로가기 색상 편집).
///
/// 이 클래스가 생긴 이유: 바로가기 색상 기본값은 <see cref="AppState"/>가, 확장 팔레트는
/// 툴바 플라이아웃이 각자 배열 리터럴로 들고 있었다. 설정 창에서 바로가기 색을 고르려면
/// 같은 확장 팔레트를 한 번 더 적어야 했고, 그 순간 세 벌이 서로 어긋날 수 있게 된다.
/// 색 목록과 16진 변환은 전부 여기서만 나온다.
/// </summary>
public static class ColorPalette
{
    /// <summary>바로가기 색상 기본값 6칸 (스펙 고정): 하늘, 노랑, 핑크, 검정, 초록, 빨강.</summary>
    public static readonly Color[] DefaultQuickColors =
    [
        Parse("#00ADEF"),
        Parse("#FEF200"),
        Parse("#FF0B88"),
        Parse("#000000"),
        Parse("#1FD430"),
        Parse("#E74C3C"),
    ];

    /// <summary>확장 팔레트 24색 (툴바 팔레트 플라이아웃 + 설정 창 바로가기 색상 선택기 공용).</summary>
    public static readonly Color[] Extended =
    [
        Parse("#FFFFFF"), Parse("#C0C0C0"), Parse("#808080"), Parse("#404040"), Parse("#000000"), Parse("#7F3F00"),
        Parse("#FF0000"), Parse("#FF7F00"), Parse("#FFFF00"), Parse("#7FFF00"), Parse("#00FF00"), Parse("#00FF7F"),
        Parse("#00FFFF"), Parse("#007FFF"), Parse("#0000FF"), Parse("#7F00FF"), Parse("#FF00FF"), Parse("#FF007F"),
        Parse("#E74C3C"), Parse("#1FD430"), Parse("#00ADEF"), Parse("#FEF200"), Parse("#FF0B88"), Parse("#8B4513"),
    ];

    /// <summary>기본 바로가기 색 문자열 사본 (설정 POCO 기본값용 — 배열 공유 방지).</summary>
    public static string[] DefaultQuickColorHex() => [.. DefaultQuickColors.Select(ToHex)];

    /// <summary>
    /// 저장된 바로가기 색상 복원 (사용자 요청 17차) — 규칙의 <b>단일 소유 지점</b> (39단계): 칸 수가 모자라면 나머지를
    /// 기본색으로 채우고, 깨진 항목은 그 칸만 기본색으로 되돌린다 (한 칸이 상해도 나머지 설정은 살린다). 여분 칸은 무시.
    /// 항상 <b>새 배열</b>을 돌려준다 — 공유 배열을 돌려주면 설정 창의 드래프트가 전역 기본값을 덮어쓴다 (배열 팩토리 규칙).
    /// 이전에는 SettingsBinder.ApplyQuickColors와 SettingsWindow.ReadQuickColors가 같은 규칙을 두 벌로 갖고 있었다.
    /// </summary>
    public static Color[] RestoreQuickColors(string[]? saved)
    {
        var colors = new Color[DefaultQuickColors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            var fallback = DefaultQuickColors[i];
            colors[i] = saved is not null && i < saved.Length
                ? Parse(saved[i], fallback)
                : fallback;
        }
        return colors;
    }

    /// <summary>색 → 설정 파일 표기 (#AARRGGBB).</summary>
    public static string ToHex(Color color) => color.ToString();

    /// <summary>설정 파일 표기 → 색. 파싱 실패 시 <paramref name="fallback"/> (손상된 설정으로 앱이 죽지 않게).</summary>
    public static Color Parse(string hex, Color fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return fallback;
        }
        catch (InvalidOperationException)
        {
            // ColorConverter는 null/빈 문자열에 InvalidOperationException을 던진다.
            return fallback;
        }
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
