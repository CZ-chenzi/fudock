using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Fudock
{
    /// <summary>从 exe / lnk / 协议 提取高分辨率图标，并缓存。</summary>
    internal static class IconService
    {
        private static readonly Dictionary<string, ImageSource> Cache =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        public static void Clear() { Cache.Clear(); }

        public static ImageSource Get(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            ImageSource img;
            if (Cache.TryGetValue(path, out img)) return img;

            img = Load(path);
            Cache[path] = img; // 可能为 null，但缓存可以避免反复尝试
            return img;
        }

        private static ImageSource Load(string path)
        {
            try
            {
                // 1) 系统大图标列表（256px），效果最好
                int index = GetSystemIconIndex(path);
                if (index >= 0)
                {
                    var fromList = FromImageList(index, Native.SHIL_JUMBO);
                    if (fromList != null) return fromList;
                    fromList = FromImageList(index, Native.SHIL_EXTRALARGE);
                    if (fromList != null) return fromList;
                }

                // 2) 退回到 SHGFI_ICON 大图标
                var fi = new Native.SHFILEINFO();
                uint flags = Native.SHGFI_ICON | Native.SHGFI_LARGEICON;
                IntPtr res = Native.SHGetFileInfo(path, 0, ref fi, (uint)System.Runtime.InteropServices.Marshal.SizeOf(fi), flags);
                if (res != IntPtr.Zero && fi.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        return FromHIcon(fi.hIcon);
                    }
                    finally { Native.DestroyIcon(fi.hIcon); }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static int GetSystemIconIndex(string path)
        {
            try
            {
                var fi = new Native.SHFILEINFO();
                IntPtr res = Native.SHGetFileInfo(path, 0, ref fi,
                    (uint)System.Runtime.InteropServices.Marshal.SizeOf(fi),
                    Native.SHGFI_SYSICONINDEX);
                if (res == IntPtr.Zero) return -1;
                return fi.iIcon;
            }
            catch { return -1; }
        }

        private static ImageSource FromImageList(int index, int listType)
        {
            IImageList list = null;
            try
            {
                Guid iid = Native.IID_IImageList;
                int hr = Native.SHGetImageList(listType, ref iid, out list);
                if (hr != 0 || list == null) return null;

                IntPtr hIcon = IntPtr.Zero;
                hr = list.GetIcon(index, Native.ILD_TRANSPARENT, ref hIcon);
                if (hr != 0 || hIcon == IntPtr.Zero) return null;

                try { return FromHIcon(hIcon); }
                finally { Native.DestroyIcon(hIcon); }
            }
            catch { return null; }
            finally
            {
                if (list != null)
                {
                    try { System.Runtime.InteropServices.Marshal.ReleaseComObject(list); }
                    catch { }
                }
            }
        }

        private static ImageSource FromHIcon(IntPtr hIcon)
        {
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                if (src == null) return null;

                // 转成 Pbgra32 便于高质量缩放 + 冻结
                var converted = new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
                converted.Freeze();
                return converted;
            }
            catch { return null; }
        }

        /// <summary>给没有图标的条目生成一个"首字母"占位图。</summary>
        public static ImageSource MakeLetterIcon(string text, Color bg, Color fg)
        {
            const int size = 128;
            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                var brush = new SolidColorBrush(bg);
                dc.DrawRoundedRectangle(brush, null, new Rect(0, 0, size, size), 28, 28);

                string ch = string.IsNullOrEmpty(text) ? "?" : text.Substring(0, 1).ToUpperInvariant();
                var ft = new FormattedText(ch,
                    System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                    size * 0.55, new SolidColorBrush(fg), 96);
                dc.DrawText(ft, new Point((size - ft.Width) / 2, (size - ft.Height) / 2));
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        /// <summary>解析 .lnk 的目标路径（失败返回原路径）。</summary>
        public static string ResolveShortcut(string lnkPath)
        {
            try
            {
                if (!lnkPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return lnkPath;
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return lnkPath;
                object shell = Activator.CreateInstance(t);
                try
                {
                    object sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod,
                        null, shell, new object[] { lnkPath });
                    Type sct = sc.GetType();
                    return (string)sct.InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, sc, null);
                }
                finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(shell); }
            }
            catch { return lnkPath; }
        }
    }
}
