using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32.SafeHandles;

namespace SSPen.Interop;

/// <summary>
/// 커스텀 커서 팩토리 (사용자 조타: 도구별 커서 UX).
/// 지우개 커서: 연필 뒤 달린 지우개(Pencil-top eraser) 형태의 45도 대각선 커서.
/// Windows 시스템 커서 크기(SM_CXCURSOR/SM_CYCURSOR)에 맞춰 다른 도구(펜 등)와 1:1로 동일한 크기로 동기화된다.
/// 핫스팟은 좌상단 지우개 팁 꼭짓점(2*scale, 2*scale)으로 정확하고 뾰족한 삭제 지점을 가리킨다.
/// 핑크 지우개 팁 + 은색 금속 페룰 + 노란 연필 바디 + 흰색/진회색 이중 외곽선으로 모든 배경에서 가시성 확보.
/// CreateIconIndirect(fIcon=false, 핫스팟=(2*scale, 2*scale))로 HCURSOR를 만든다.
/// </summary>
internal static class CursorFactory
{
    private static Cursor? _eraser;

    /// <summary>지우개 커서 (지연 생성, 프로세스 수명 공유). 시스템 커서 크기(SM_CXCURSOR)와 동기화된다.</summary>
    public static Cursor Eraser => _eraser ??= CreateEraserCursor() ?? Cursors.Cross;

    private static Cursor? CreateEraserCursor()
    {
        try
        {
            int cx = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXCURSOR);
            int cy = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYCURSOR);
            if (cx <= 0) cx = 32;
            if (cy <= 0) cy = 32;

            float scale = cx / 32.0f;

            using var bmp = new System.Drawing.Bitmap(cx, cy, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                var state = g.Save();
                // 32x32 기준 좌표를 Windows 시스템 커서 크기로 확장
                g.ScaleTransform(scale, scale);
                g.TranslateTransform(2f, 2f);
                g.RotateTransform(-45f);

                float r = 3.5f;

                float eraserH = r * 2.0f;
                float ferruleH = r * 1.4f;
                float bodyH = r * 3.6f;

                float y0 = 0.5f;
                float y1 = y0 + eraserH;
                float y2 = y1 + ferruleH;
                float y3 = y2 + bodyH;

                float w = r * 2.0f;
                float left = -r;
                float right = r;

                float haloWidth = 3.5f;
                float darkWidth = 1.4f;

                // 1. 전체 외곽선 및 바디 경로
                using (var haloPen = new System.Drawing.Pen(System.Drawing.Color.White, haloWidth) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                using (var darkPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0x1F, 0x1F, 0x1F), darkWidth) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                {
                    using var path = new System.Drawing.Drawing2D.GraphicsPath();
                    // 팁 돔 (지우개 상단 끝)
                    path.AddArc(left, y0, w, w, 180, 180);
                    // 연필 몸체 바닥까지
                    path.AddLine(right, y0 + r, right, y3);
                    path.AddLine(right, y3, left, y3);
                    path.AddLine(left, y3, left, y0 + r);
                    path.CloseFigure();

                    // 흰색 배경 후광 스트로크 (어두운 배경 대비)
                    g.DrawPath(haloPen, path);

                    // 2. 연필 몸체 채우기 (노란색 3면)
                    float faceW = w / 3.0f;
                    using (var leftBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xE0, 0x8E, 0x00)))
                    using (var midBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xFF, 0xC4, 0x00)))
                    using (var rightBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xFF, 0xDA, 0x5C)))
                    {
                        g.FillRectangle(leftBrush, left, y2, faceW, bodyH);
                        g.FillRectangle(midBrush, left + faceW, y2, faceW, bodyH);
                        g.FillRectangle(rightBrush, left + faceW * 2, y2, faceW, bodyH);
                    }

                    // 3. 금속 페룰(Ferrule) 채우기 (은색 밴드)
                    float ferruleLeft = left - 0.3f;
                    float ferruleW = w + 0.6f;
                    float ferruleFaceW = ferruleW / 3.0f;
                    using (var ferruleDark = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x9E, 0xA4, 0xAA)))
                    using (var ferruleMid = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xCF, 0xD4, 0xD9)))
                    using (var ferruleLight = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xEE, 0xF1, 0xF4)))
                    {
                        g.FillRectangle(ferruleDark, ferruleLeft, y1, ferruleFaceW, ferruleH);
                        g.FillRectangle(ferruleMid, ferruleLeft + ferruleFaceW, y1, ferruleFaceW, ferruleH);
                        g.FillRectangle(ferruleLight, ferruleLeft + ferruleFaceW * 2, y1, ferruleFaceW, ferruleH);
                    }
                    // 페룰 리브 라인
                    using (var ribPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0x70, 0x75, 0x7A), 0.8f))
                    {
                        float midY = y1 + ferruleH * 0.5f;
                        g.DrawLine(ribPen, ferruleLeft, midY, ferruleLeft + ferruleW, midY);
                    }

                    // 4. 핑크 지우개 팁 채우기 (연필 뒤 지우개)
                    using (var eraserDark = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xDE, 0x60, 0x78)))
                    using (var eraserMid = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xFF, 0x82, 0x9B)))
                    using (var eraserLight = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xFF, 0xA8, 0xBC)))
                    {
                        using var eraserPath = new System.Drawing.Drawing2D.GraphicsPath();
                        eraserPath.AddArc(left, y0, w, w, 180, 180);
                        eraserPath.AddLine(right, y0 + r, right, y1);
                        eraserPath.AddLine(right, y1, left, y1);
                        eraserPath.AddLine(left, y1, left, y0 + r);
                        eraserPath.CloseFigure();

                        g.FillPath(eraserMid, eraserPath);
                        g.FillRectangle(eraserDark, left, y0 + r, faceW, eraserH - r);
                        g.FillRectangle(eraserLight, left + faceW * 2, y0 + r, faceW, eraserH - r);
                    }

                    // 5. 어두운 본선 테두리
                    g.DrawPath(darkPen, path);
                    g.DrawLine(darkPen, ferruleLeft, y1, ferruleLeft + ferruleW, y1);
                    g.DrawLine(darkPen, ferruleLeft, y2, ferruleLeft + ferruleW, y2);
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
                info.xHotspot = (uint)MathF.Round(2f * scale);
                info.yHotspot = (uint)MathF.Round(2f * scale);
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
