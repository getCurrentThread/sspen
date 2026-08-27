using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32.SafeHandles;

namespace SSPen.Interop;

/// <summary>
/// 커스텀 커서 팩토리 (사용자 조타: 도구별 커서 UX).
/// 지우개 커서 (사용자 조타 13차): 불투명 아이콘 대신 삭제 지점을 그대로 보여주는
/// 링 커서 — 핫스팟 중심의 원형 링(히트테스트 반경 시각화) + 중심점 + 우하단 미니 지우개 글리프.
/// 링은 흰색 외곽 + 진회색 본선 이중 스트로크라 밝든 어둡든 어떤 배경 위에서도 보인다.
/// CreateIconIndirect(fIcon=false, 핫스팟=링 중심)로 HCURSOR를 만든다.
/// </summary>
internal static class CursorFactory
{
    private static Cursor? _eraser;

    /// <summary>지우개 커서 (지연 생성, 프로세스 수명 공유). 실패 시 십자 커서 폴백.</summary>
    public static Cursor Eraser => _eraser ??= CreateEraserCursor() ?? Cursors.Cross;

    private static Cursor? CreateEraserCursor()
    {
        try
        {
            using var bmp = new System.Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                var dark = System.Drawing.Color.FromArgb(0x1F, 0x1F, 0x1F);

                // 삭제 반경 링: EraseAt 허용 오차(6px + 굵기/2)를 시각화하는 반경 8px 원.
                // 흰 두꺼운 링 위에 진회색 얇은 링 — 이중 스트로크로 배경 무관 가시성 확보.
                var ring = new System.Drawing.RectangleF(8f, 8f, 16f, 16f);
                using (var halo = new System.Drawing.Pen(System.Drawing.Color.White, 3.4f))
                using (var main = new System.Drawing.Pen(dark, 1.5f))
                {
                    g.DrawEllipse(halo, ring);
                    g.DrawEllipse(main, ring);
                }

                // 중심점: 정확한 삭제 지점 표시 (흰 후광 + 진회색 점).
                using (var haloDot = new System.Drawing.SolidBrush(System.Drawing.Color.White))
                using (var dot = new System.Drawing.SolidBrush(dark))
                {
                    g.FillEllipse(haloDot, 13.5f, 13.5f, 5f, 5f);
                    g.FillEllipse(dot, 14.5f, 14.5f, 3f, 3f);
                }

                // 우하단 미니 지우개 글리프: 어떤 도구인지 잊지 않게 하는 꼬리표 (링을 가리지 않는 위치).
                var body = new System.Drawing.Rectangle(21, 23, 10, 7);
                using (var fill = new System.Drawing.SolidBrush(System.Drawing.Color.White))
                using (var band = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x00, 0xAD, 0xEF)))
                using (var line = new System.Drawing.Pen(dark, 1.2f))
                {
                    g.FillRectangle(fill, body);
                    g.FillRectangle(band, new System.Drawing.Rectangle(21, 27, 10, 3));
                    g.DrawRectangle(line, body);
                }
            }

            nint hIcon = bmp.GetHicon();
            try
            {
                if (!NativeMethods.GetIconInfo(hIcon, out var info))
                {
                    return null;
                }
                info.fIcon = 0; // BOOL: 0 = cursor
                info.xHotspot = 16; // 링 중심 = 삭제 지점
                info.yHotspot = 16;
                nint hCursor = NativeMethods.CreateIconIndirect(ref info);
                // CreateIconIndirect가 비트맵을 복사하므로 원본은 해제.
                NativeMethods.DeleteObject(info.hbmColor);
                NativeMethods.DeleteObject(info.hbmMask);
                if (hCursor == 0)
                {
                    return null;
                }
                return CursorInteropHelper.Create(new SafeCursorHandle(hCursor));
            }
            finally
            {
                NativeMethods.DestroyIcon(hIcon);
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    private sealed class SafeCursorHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeCursorHandle(nint handle) : base(ownsHandle: true) => SetHandle(handle);

        protected override bool ReleaseHandle() => NativeMethods.DestroyIcon(handle);
    }
}
