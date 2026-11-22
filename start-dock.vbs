' Start Fudock without flashing a console window.
' Double-click this file to launch, or put a shortcut to it in:
'   %APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup
Option Explicit
Dim fso, sh, base, exe
Set fso = CreateObject("Scripting.FileSystemObject")
Set sh = CreateObject("WScript.Shell")
base = fso.GetParentFolderName(WScript.ScriptFullName)
exe = base & "\build\Fudock.exe"
If Not fso.FileExists(exe) Then
    MsgBox "Fudock.exe not found." & vbCrLf & vbCrLf & _
           "Run build.cmd first, or check: " & exe, 48, "Fudock"
    WScript.Quit 1
End If
sh.Run """" & exe & """", 0, False
