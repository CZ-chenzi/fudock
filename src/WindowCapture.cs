using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Fudock
{
    /// <summary>抓取任意窗口的画面，用于 Dock 的窗口预览。</summary>
    internal static class WindowCapture
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        private const uint PW_RENDERFULLCONTENT = 0x00000002;

        /// <summary>抓一张窗口缩略图（失败返回 null，例如最小化窗口 / 受保护窗口）。</summary>
        public static ImageSource Capture(IntPtr hwnd, int maxWidth)
        {
            if (hwnd == IntPtr.Zero) return null;

            try
            {
                if (!IsWindow(hwnd) || IsIconic(hwnd)) return null;

                RECT r;
                if (!GetWindowRect(hwnd, out r)) return null;

                int w = r.Right - r.Left;
                int h = r.Bottom - r.Top;
                if (w < 24 || h < 24) return null;
                if (w > 8000 || h > 8000) return null;

                IntPtr screenDc = GetDC(IntPtr.Zero);
                if (screenDc == IntPtr.Zero) return null;

                IntPtr memDc = IntPtr.Zero;
                IntPtr hbmp = IntPtr.Zero;
                IntPtr oldObj = IntPtr.Zero;
                try
                {
                    memDc = CreateCompatibleDC(screenDc);
                    hbmp = CreateCompatibleBitmap(screenDc, w, h);
                    if (memDc == IntPtr.Zero || hbmp == IntPtr.Zero) return null;

                    oldObj = SelectObject(memDc, hbmp);
                    bool ok = PrintWindow(hwnd, memDc, PW_RENDERFULLCONTENT);
                    SelectObject(memDc, oldObj);
                    oldObj = IntPtr.Zero;

                    if (!ok) return null;

                    BitmapSource src = Imaging.CreateBitmapSourceFromHBitmap(
                        hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    if (src == null) return null;

                    // 缩略图不需要原尺寸，缩到指定宽度省内存
                    if (w > maxWidth)
                    {
                        double scale = (double)maxWidth / w;
                        var tb = new TransformedBitmap(src, new ScaleTransform(scale, scale));
                        tb.Freeze();
                        return tb;
                    }

                    src.Freeze();
                    return src;
                }
                finally
                {
                    if (oldObj != IntPtr.Zero) SelectObject(memDc, oldObj);
                    if (hbmp != IntPtr.Zero) DeleteObject(hbmp);
                    if (memDc != IntPtr.Zero) DeleteDC(memDc);
                    ReleaseDC(IntPtr.Zero, screenDc);
                }
            }
            catch (Exception ex)
            {
                SettingsStore.Log("WindowCapture: " + ex.Message);
                return null;
            }
        }
    }
}
