# DPI-aware full-screen screenshot.  usage: .\shot.ps1 -Out out.png [-X x -Y y -W w -H h]
param(
    [string]$Out = 'shot.png',
    [int]$X = 0, [int]$Y = 0, [int]$W = 0, [int]$H = 0
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
public class DpiHelper {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(System.IntPtr ctx);
}
'@ -ErrorAction SilentlyContinue
try { [DpiHelper]::SetProcessDPIAware() } catch { }

$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
if ($W -le 0) { $X = $b.X; $Y = $b.Y; $W = $b.Width; $H = $b.Height }
$bmp = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($X, $Y, 0, 0, (New-Object System.Drawing.Size($W, $H)))
$g.Dispose()
$dir = Split-Path -Parent $Out
if ($dir -and !(Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output ("saved $Out  ${W}x${H}  (screen $($b.Width)x$($b.Height))")
