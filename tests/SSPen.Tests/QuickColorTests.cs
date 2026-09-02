using System.IO;
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

    // ---- 39단계: 복원 규칙의 단일 소유 지점 ColorPalette.RestoreQuickColors ----

    [Fact]
    public void RestoreQuickColors_Null_ReturnsDefaults_AsFreshArray()
    {
        var restored = ColorPalette.RestoreQuickColors(null);

        Assert.Equal(ColorPalette.DefaultQuickColors, restored);
        Assert.NotSame(ColorPalette.DefaultQuickColors, restored); // 공유 배열을 돌려주면 드래프트가 전역 기본값을 덮어쓴다
    }

    [Fact]
    public void RestoreQuickColors_ShortArray_FillsRemainderWithDefaults()
    {
        var restored = ColorPalette.RestoreQuickColors(["#7F00FF", "#123456"]);

        Assert.Equal(Purple, restored[0]);
        Assert.Equal((Color)ColorConverter.ConvertFromString("#123456"), restored[1]);
        Assert.Equal(ColorPalette.DefaultQuickColors.Skip(2), restored.Skip(2));
    }

    [Fact]
    public void RestoreQuickColors_CorruptSlot_OnlyThatSlotFallsBack()
    {
        var restored = ColorPalette.RestoreQuickColors(["#7F00FF", "not-a-color", "", "#7F00FF", "#7F00FF", "#7F00FF"]);

        Assert.Equal(Purple, restored[0]);
        Assert.Equal(ColorPalette.DefaultQuickColors[1], restored[1]);
        Assert.Equal(ColorPalette.DefaultQuickColors[2], restored[2]);
        Assert.Equal(Purple, restored[3]);
    }

    [Fact]
    public void RestoreQuickColors_ExtraSlots_AreIgnored()
    {
        var restored = ColorPalette.RestoreQuickColors(Enumerable.Repeat("#7F00FF", 9).ToArray());

        Assert.Equal(AppState.QuickColorCount, restored.Length);
        Assert.All(restored, c => Assert.Equal(Purple, c));
    }

    [Fact]
    public void DefaultQuickColors_Count_MatchesAppStateQuickColorCount() =>
        Assert.Equal(AppState.QuickColorCount, ColorPalette.DefaultQuickColors.Length);

    /// <summary>바인더 경로 특성화: 설정 파일의 깨진 칸만 기본색으로 돌아온다 (SettingsBinder는 ColorPalette 규칙에 위임).</summary>
    [Fact]
    public void SettingsBinder_ApplyToState_RestoresQuickColorsThroughColorPalette()
    {
        string dir = Path.Combine(Path.GetTempPath(), "SSPenTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var service = new SettingsService(dir);
            service.Save(new AppSettings { QuickColors = ["#7F00FF", "broken"] });
            var state = new AppState();
            var binder = new SettingsBinder(state, new FadingInkController(new FadeSchedulerCore()), service);

            binder.Load();
            binder.ApplyToState();

            Assert.Equal(Purple, state.QuickColors[0]);
            Assert.Equal(ColorPalette.DefaultQuickColors[1], state.QuickColors[1]);
            Assert.Equal(ColorPalette.DefaultQuickColors[5], state.QuickColors[5]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
