using System.IO;
using System.Text;

namespace SSPen.Diagnostics;

/// <summary>
/// 롤링 파일 로그: %APPDATA%\SS Pen\logs\sspen-yyyyMMdd.log (플랜 Observability).
/// 시작 토폴로지 덤프, RegisterHotKey 결과, exstyle 전이, z-밴드 재적용, 캡처 시퀀스가 프리모템 탐지 신호다.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _directory;

    public static void Initialize()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SS Pen", "logs");
        Directory.CreateDirectory(_directory);
        Info("=== SS Pen 시작 ===");
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        if (_directory is null)
        {
            return;
        }
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (Gate)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(_directory, $"sspen-{DateTime.Now:yyyyMMdd}.log"),
                    line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
                // 로그 실패가 앱을 죽여서는 안 된다.
            }
        }
    }
}
