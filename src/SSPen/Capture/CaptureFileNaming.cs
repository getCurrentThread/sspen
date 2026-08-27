using System.IO;

namespace SSPen.Capture;

/// <summary>
/// 캡처 파일명 규칙 (WI-12, 스펙 고정): `SSPen_yyyyMMdd_HHmmss.png`, 로컬 시간.
/// 동일 초 충돌 시 숫자 접미사 `_2`, `_3`, … (CRIT-8 확정 오라클).
/// </summary>
public static class CaptureFileNaming
{
    public static string BaseFileName(DateTime localTime) =>
        $"SSPen_{localTime:yyyyMMdd_HHmmss}.png";

    /// <summary>
    /// 저장 폴더 안에서 충돌하지 않는 파일명 결정.
    /// exists 콜백으로 파일 존재를 추상화해 순수 로직으로 검증한다.
    /// </summary>
    public static string ResolveFileName(DateTime localTime, Func<string, bool> exists)
    {
        string baseName = BaseFileName(localTime);
        if (!exists(baseName))
        {
            return baseName;
        }
        string stem = Path.GetFileNameWithoutExtension(baseName);
        for (int suffix = 2; ; suffix++)
        {
            string candidate = $"{stem}_{suffix}.png";
            if (!exists(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>기본 저장 폴더: 사진\SS Pen (스펙 고정).</summary>
    public static string DefaultSaveFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "SS Pen");
}
