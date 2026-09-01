using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32.SafeHandles;

namespace SSPen.Interop;

/// <summary>
/// 커스텀 커서 팩토리 (사용자 조타: 도구별 커서 UX).
/// 지우개 커서: 연필 뒤 달린 지우개(Pencil-top eraser) 형태의 45도 대각선 커서.
/// 핫스팟은 좌상단(2, 2) 지우개 팁 꼭짓점으로 정확하고 뾰족한 삭제 지점을 가리킨다.
/// 핑크 지우개 팁 + 은색 금속 페룰 + 노란 연필 바디 + 흰색/진회색 이중 외곽선으로 모든 배경에서 가시성 확보.
/// CreateIconIndirect(fIcon=false, 핫스팟=(2, 2))로 HCURSOR를 만든다.
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
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                var state = g.Save();
                g.TranslateTransform(2f, 2f);
                g.RotateTransform(-45f);

                // 1. 전체 외곽선 및 바디 경로
                using (var haloPen = new System.Drawing.Pen(System.Drawing.Color.White, 3.5f) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                using (var darkPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0x1F, 0x1F, 0x1F), 1.4f) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                {
                    using var path = new System.Drawing.Drawing2D.GraphicsPath();
                    // 팁 돔 (지우개 상단 끝)
                    path.AddArc(-3.5f, 0.5f, 7f, 7f, 180, 180);
                    // 연필 몸체 바닥까지
                    path.AddLine(3.5f, 4f, 3.5f, 25f);
                    path.AddLine(3.5f, 25f, -3.5f, 25f);
                    path.AddLine(-3.5f, 25f, -3.5f, 4f);
                    path.CloseFigure();

                    // 흰색 배경 후광 스트로크 (어두운 배경 대비)
                    g.DrawPath(haloPen, path);

                    // 2. 연필 몸체 채우기 (노란색 3면)
                    using (var leftBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xE0, 0x8E, 0x00)))
                    using (var midBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xFF, 0xC4, 0x00)))
                    using (var rightBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xFF, 0xDA, 0x5C)))
                    {
                        g.FillRectangle(leftBrush, -3.5f, 12f, 2.3f, 13f);
                        g.FillRectangle(midBrush, -1.2f, 12f, 2.4f, 13f);
                        g.FillRectangle(rightBrush, 1.2f, 12f, 2.3f, 13f);
                    }

                    // 3. 금속 페룰(Ferrule) 채우기 (은색 밴드)
                    using (var ferruleDark = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x9E, 0xA4, 0xAA)))
                    using (var ferruleMid = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xCF, 0xD4, 0xD9)))
                    using (var ferruleLight = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xEE, 0xF1, 0xF4)))
                    {
                        g.FillRectangle(ferruleDark, -3.8f, 7f, 2.5f, 5f);
                        g.FillRectangle(ferruleMid, -1.3f, 7f, 2.6f, 5f);
                        g.FillRectangle(ferruleLight, 1.3f, 7f, 2.5f, 5f);
                    }
                    // 페룰 리브 라인
                    using (var ribPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0x70, 0x75, 0x7A), 0.8f))
                    {
                        g.DrawLine(ribPen, -3.8f, 9.5f, 3.8f, 9.5f);
                    }

                    // 4. 핑크 지우개 팁 채우기 (연필 뒤 지우개)
                    using (var eraserDark = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xDE, 0x60, 0x78)))
                    using (var eraserMid = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xFF, 0x82, 0x9B)))
                    using (var eraserLight = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xFF, 0xA8, 0xBC)))
                    {
                        using var eraserPath = new System.Drawing.Drawing2D.GraphicsPath();
                        eraserPath.AddArc(-3.5f, 0.5f, 7f, 7f, 180, 180);
                        eraserPath.AddLine(3.5f, 4f, 3.5f, 7f);
                        eraserPath.AddLine(3.5f, 7f, -3.5f, 7f);
                        eraserPath.AddLine(-3.5f, 7f, -3.5f, 4f);
                        eraserPath.CloseFigure();

                        g.FillPath(eraserMid, eraserPath);
                        g.FillRectangle(eraserDark, -3.5f, 4f, 2.3f, 3f);
                        g.FillRectangle(eraserLight, 1.2f, 4f, 2.3f, 3f);
                    }

                    // 5. 어두운 본선 테두리
                    g.DrawPath(darkPen, path);
                    g.DrawLine(darkPen, -3.8f, 7f, 3.8f, 7f);
                    g.DrawLine(darkPen, -3.8f, 12f, 3.8f, 12f);
                }

                g.Restore(state);
            }

            nint hIcon = bmp.GetHicon();
            try
            {
                if (!NativeMethods.GetIconInfo(hIcon, out var info))
                {
                    return null;
                }
                info.fIcon = 0; // BOOL: 0 = cursor
                info.xHotspot = 2; // 지우개 팁 꼭짓점 = 정확한 삭제 지점
                info.yHotspot = 2;
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
