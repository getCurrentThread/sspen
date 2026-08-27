using System.IO;
using SSPen.Annotation;
using SSPen.Settings;
using Xunit;

namespace SSPen.Tests;

/// <summary>
/// WI-14: 설정 직렬화 — 왕복, 파일 없음 ⇒ 기본값, 손상 ⇒ .bad 격리 + 기본값,
/// 알 수 없는 속성 무시, 핫키 맵 왕복 (플랜 유닛테스트 계약).
/// </summary>
public class SettingsServiceTests : IDisposable
{
    private readonly string _dir;

    public SettingsServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "sspen-tests-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private SettingsService NewService() => new(_dir);

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var settings = NewService().Load();
        Assert.True(settings.RunAtLogin);
        Assert.True(settings.BoardAllMonitors);
        Assert.Equal(FadingDurations.Default, settings.FadingSeconds);
        // 도구별 개별 스타일 기본값 (사용자 조타): 펜(자유선·페이딩) 빨강 / 형광펜 노랑 / 도형 초록, 동기화 꺼짐.
        Assert.False(settings.SyncToolStyles);
        Assert.Equal("#E74C3C", settings.PenColor);
        Assert.Equal("#FEF200", settings.HighlighterColor);
        Assert.Equal("#1FD430", settings.ShapeColor);
        Assert.Equal(2, settings.PenThickness);
        Assert.Null(settings.ToolbarLeft);
        Assert.Empty(settings.Hotkeys);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var service = NewService();
        var settings = new AppSettings
        {
            ToolbarLeft = 1888,
            ToolbarTop = 300,
            SaveFolder = @"D:\캡처",
            RunAtLogin = false,
            CheckUpdateOnStart = true,
            WheelAdjustsPenSize = false,
            BoardAllMonitors = false,
            FadingSeconds = 5,
            HighlightCursor = true,
            SyncToolStyles = true,
            PenColor = "#FF0B88",
            PenThickness = 4,
            HighlighterColor = "#1FD430",
            HighlighterThickness = 0,
            ShapeColor = "#000000",
            ShapeThickness = 3,
        };
        service.Save(settings);

        var loaded = NewService().Load();
        Assert.Equal(1888, loaded.ToolbarLeft);
        Assert.Equal(300, loaded.ToolbarTop);
        Assert.Equal(@"D:\캡처", loaded.SaveFolder);
        Assert.False(loaded.RunAtLogin);
        Assert.True(loaded.CheckUpdateOnStart);
        Assert.False(loaded.WheelAdjustsPenSize);
        Assert.False(loaded.BoardAllMonitors);
        Assert.Equal(5, loaded.FadingSeconds);
        Assert.True(loaded.HighlightCursor);
        Assert.True(loaded.SyncToolStyles);
        Assert.Equal("#FF0B88", loaded.PenColor);
        Assert.Equal(4, loaded.PenThickness);
        Assert.Equal("#1FD430", loaded.HighlighterColor);
        Assert.Equal(0, loaded.HighlighterThickness);
        Assert.Equal("#000000", loaded.ShapeColor);
        Assert.Equal(3, loaded.ShapeThickness);
    }

    [Fact]
    public void HotkeyMap_RoundTrips()
    {
        var service = NewService();
        var settings = new AppSettings();
        settings.Hotkeys["pen"] = new HotkeyDef(0x0001 | 0x0004, 0x50);      // Alt+Shift+P
        settings.Hotkeys["capture"] = new HotkeyDef(0x0002 | 0x0004, 0x43);  // Ctrl+Shift+C
        service.Save(settings);

        var loaded = NewService().Load();
        Assert.Equal(2, loaded.Hotkeys.Count);
        Assert.Equal(new HotkeyDef(0x0005, 0x50), loaded.Hotkeys["pen"]);
        Assert.Equal(new HotkeyDef(0x0006, 0x43), loaded.Hotkeys["capture"]);
    }

    [Fact]
    public void Load_CorruptJson_QuarantinesAndReturnsDefaults()
    {
        var service = NewService();
        Directory.CreateDirectory(_dir);
        File.WriteAllText(service.SettingsPath, "{ 이것은 JSON이 아니다 ::::");

        var loaded = service.Load();
        Assert.True(loaded.RunAtLogin); // 기본값
        Assert.True(File.Exists(service.SettingsPath + ".bad"));
        Assert.False(File.Exists(service.SettingsPath));

        // 재생성 가능: 저장 후 다시 로드.
        service.Save(loaded);
        Assert.True(File.Exists(service.SettingsPath));
    }

    [Fact]
    public void Load_UnknownProperties_AreIgnored()
    {
        var service = NewService();
        Directory.CreateDirectory(_dir);
        File.WriteAllText(service.SettingsPath, """
            {
              "FadingSeconds": 0.5,
              "미래에추가된속성": { "값": 1 },
              "AnotherUnknown": [1, 2, 3]
            }
            """);

        var loaded = service.Load();
        Assert.Equal(0.5, loaded.FadingSeconds);
        Assert.True(loaded.RunAtLogin); // 나머지는 기본값 유지
    }
}
