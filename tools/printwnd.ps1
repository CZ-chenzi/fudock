# Capture a window by title via PrintWindow (works even when the window is covered).
# usage: .\printwnd.ps1 -Title 'Fudock' -Out out.png
param([string]$Title = 'Fudock', [string]$Out = 'wnd.png')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public class PrintWnd {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

    public static string Shot(string title, string outPath) {
        SetProcessDPIAware();
        IntPtr h = FindWindow(null, title);
        if (h == IntPtr.Zero) return "window not found: " + title;
        RECT r; GetWindowRect(h, out r);
        int w = r.R - r.L, ht = r.B - r.T;
        if (w <= 0 || ht <= 0) return "bad rect";
        using (var bmp = new Bitmap(w, ht, PixelFormat.Format32bppArgb)) {
            using (var g = Graphics.FromImage(bmp)) {
                IntPtr hdc = g.GetHdc();
                bool ok = PrintWindow(h, hdc, 2 /*PW_RENDERFULLCONTENT*/);
                g.ReleaseHdc(hdc);
                if (!ok) { /* 老系统不支持时也先把 BitBlt 结果存下来 */ }
            }
            bmp.Save(outPath, ImageFormat.Png);
        }
        return string.Format("{0} {1}x{2}", outPath, w, ht);
    }
}
'@
[PrintWnd]::Shot($Title, $Out)
