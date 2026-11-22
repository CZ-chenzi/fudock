# DPI-aware mouse helper.
# usage: .\pointer.ps1 -X 1200 -Y 1560 [-Click] [-Right] [-DelayMs 450]
param([int]$X = -1, [int]$Y = -1, [switch]$Click, [switch]$Right, [int]$DelayMs = 450)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class Ptr {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004, RIGHTDOWN = 0x0008, RIGHTUP = 0x0010;
}
'@
[void][Ptr]::SetProcessDPIAware()
if ($X -ge 0 -and $Y -ge 0) {
    [void][Ptr]::SetCursorPos($X, $Y - 40)
    Start-Sleep -Milliseconds 60
    [void][Ptr]::SetCursorPos($X, $Y - 20)
    Start-Sleep -Milliseconds 60
    [void][Ptr]::SetCursorPos($X, $Y)
}
Start-Sleep -Milliseconds $DelayMs
if ($Click) {
    if ($Right) {
        [Ptr]::mouse_event([Ptr]::RIGHTDOWN, 0, 0, 0, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 70
        [Ptr]::mouse_event([Ptr]::RIGHTUP, 0, 0, 0, [IntPtr]::Zero)
    }
    else {
        [Ptr]::mouse_event([Ptr]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 70
        [Ptr]::mouse_event([Ptr]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
    }
    Start-Sleep -Milliseconds 400
}
Write-Output "pointer -> $X,$Y right=$Right click=$Click"
