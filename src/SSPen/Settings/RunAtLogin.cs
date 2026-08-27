using Microsoft.Win32;
using SSPen.Diagnostics;

namespace SSPen.Settings;

/// <summary>
/// 로그인 시 자동 시작 (AC-26): HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// C2 예외 항목 — 이 값 하나만 레지스트리를 쓴다 (설정 서비스의 부수 효과로 소유).
/// 언인스톨러가 이 키를 제거한다 (R9).
/// </summary>
public static class RunAtLogin
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SS Pen";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled)
            {
                string exe = Environment.ProcessPath
                    ?? throw new InvalidOperationException("실행 파일 경로를 확인할 수 없습니다.");
                key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            Log.Info($"로그인 시 시작: {(enabled ? "켜짐" : "꺼짐")}");
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Error("로그인 시 시작 설정 실패", ex);
        }
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is not null;
    }
}
