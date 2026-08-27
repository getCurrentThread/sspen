using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using SSPen.Diagnostics;
using SSPen.Shell;

namespace SSPen.Capture;

/// <summary>
/// 캡처 출력 (WI-12): 클립보드 복사(제한 재시도) / PNG 파일 저장.
/// 복사와 저장은 강제 동시 실행이 아니라 사용자 선택 (스펙 고정).
/// </summary>
public static class CaptureOutputs
{
    /// <summary>
    /// ARCH-9: 일시적 CLIPBRD_E_CANT_OPEN 경합에 3회 x 100ms 제한 재시도.
    /// 소진 시 한국어 실패 메시지를 로그로 남기고 false.
    /// </summary>
    public static bool CopyToClipboard(BitmapSource image)
    {
        const int attempts = 3;
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                Clipboard.SetImage(image);
                Log.Info("클립보드 복사 성공");
                return true;
            }
            catch (System.Runtime.InteropServices.COMException ex) when (attempt < attempts)
            {
                Log.Warn($"클립보드 열기 경합 (시도 {attempt}/{attempts}): 0x{ex.HResult:X8}");
                Thread.Sleep(100);
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                Log.Error(Strings.ClipboardCopyFailed + $" (0x{ex.HResult:X8})");
                return false;
            }
        }
        return false;
    }

    /// <summary>PNG 저장: 사진\SS Pen\SSPen_yyyyMMdd_HHmmss.png (+충돌 접미사). 저장 경로 반환.</summary>
    public static string SavePng(BitmapSource image, string? folder = null, DateTime? localTime = null)
    {
        string directory = folder ?? CaptureFileNaming.DefaultSaveFolder();
        Directory.CreateDirectory(directory);
        string fileName = CaptureFileNaming.ResolveFileName(
            localTime ?? DateTime.Now,
            candidate => File.Exists(Path.Combine(directory, candidate)));
        string path = Path.Combine(directory, fileName);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.Create(path))
        {
            encoder.Save(stream);
        }
        Log.Info($"캡처 저장: {path}");
        return path;
    }
}
