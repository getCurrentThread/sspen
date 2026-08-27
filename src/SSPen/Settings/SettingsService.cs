using System.IO;
using System.Text.Json;
using SSPen.Diagnostics;

namespace SSPen.Settings;

/// <summary>
/// 설정 영속화 (WI-14, C1 확정): JSON 단일 파일, 시작 시 로드 / 변경·종료 시 저장.
/// 파일 없음 ⇒ 기본값. 손상 ⇒ `.bad`로 이름 바꾸고 기본값 재생성.
/// 알 수 없는 속성은 무시된다 (추가 속성 + 기본값 전략).
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _directory;

    public SettingsService(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SS Pen");
    }

    public string SettingsPath => Path.Combine(_directory, "settings.json");

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }
        try
        {
            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // 손상 파일 격리 (C1): settings.json → settings.json.bad, 기본값 재생성.
            try
            {
                string quarantine = SettingsPath + ".bad";
                File.Delete(quarantine);
                File.Move(SettingsPath, quarantine);
                Log.Warn($"손상된 설정 파일을 격리했습니다: {quarantine}");
            }
            catch (IOException moveEx)
            {
                Log.Error("손상 설정 격리 실패", moveEx);
            }
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Options));
    }
}
