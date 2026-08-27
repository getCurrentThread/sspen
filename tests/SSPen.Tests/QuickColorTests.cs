using System.Windows.Media;
using SSPen.Annotation;
using SSPen.Settings;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// 바로가기 색상 편집 (사용자 요청 17차). 이전에는 static 상수 배열이라 사용자가 바꿀 수 없었다.
/// </summary>
public sealed class QuickColorTests
{
    private static readonly Color Purple = (Color)ColorConverter.ConvertFromString("#7F00FF");

    [Fact]
    public void SetQuickColor_ChangesSlot_AndRaisesChanged()
    {
        var state = new AppState();
        int changes = 0;
        state.Changed += () => changes++;

        state.SetQuickColor(2, Purple);

        Assert.Equal(Purple, state.QuickColors[2]);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void SetQuickColor_SameValue_DoesNotRaiseChanged()
    {
        // 설정 적용은 6칸을 모두 훑는다. 같은 값에도 Changed가 나가면
        // 창을 열고 확인만 눌러도 저장 디바운스가 무의미하게 돈다.
        var state = new AppState();
        int changes = 0;
        state.Changed += () => changes++;

        state.SetQuickColor(0, state.QuickColors[0]);

        Assert.Equal(0, changes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(AppState.QuickColorCount)]
    [InlineData(99)]
    public void SetQuickColor_OutOfRangeIndex_IsIgnored(int index)
    {
        // 손상된 설정 파일이 칸 수보다 많은 항목을 담고 있어도 죽지 않아야 한다.
        var state = new AppState();

        state.SetQuickColor(index, Purple);

        Assert.Equal(AppState.QuickColorCount, state.QuickColors.Count);
        Assert.DoesNotContain(Purple, state.QuickColors);
    }

    [Fact]
    public void DefaultQuickColors_MatchSlotCount()
    {
        Assert.Equal(AppState.QuickColorCount, ColorPalette.DefaultQuickColors.Length);
        Assert.Equal(AppState.QuickColorCount, new AppState().QuickColors.Count);
    }

    [Fact]
    public void QuickColors_RoundTripThroughSettingsHex()
    {
        // 저장은 #AARRGGBB 문자열, 로드는 파싱. 왕복에서 색이 변하면 재시작 때 팔레트가 달라진다.
        var state = new AppState();
        state.SetQuickColor(4, Purple);

        string[] saved = [.. state.QuickColors.Select(ColorPalette.ToHex)];
        var restored = new AppState();
        for (int i = 0; i < saved.Length; i++)
        {
            restored.SetQuickColor(i, ColorPalette.Parse(saved[i], Colors.Magenta));
        }

        Assert.Equal(state.QuickColors, restored.QuickColors);
        Assert.Equal(Purple, restored.QuickColors[4]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-color")]
    [InlineData("#GGGGGG")]
    public void Parse_CorruptHex_FallsBackWithoutThrowing(string hex)
    {
        // 손상된 한 칸이 앱 기동을 막으면 안 된다 (설정 파일은 사람이 손댈 수 있다).
        Assert.Equal(Colors.Magenta, ColorPalette.Parse(hex, Colors.Magenta));
    }

    [Fact]
    public void AppSettings_DefaultQuickColors_AreNotSharedAcrossInstances()
    {
        // 기본값이 같은 배열 인스턴스를 공유하면 한 설정 객체를 고칠 때 다른 객체까지 바뀐다.
        var a = new AppSettings();
        var b = new AppSettings();

        a.QuickColors[0] = "#FFFFFFFF";

        Assert.NotEqual(a.QuickColors[0], b.QuickColors[0]);
    }
}
