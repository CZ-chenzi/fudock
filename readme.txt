浮坞 Fudock 1.0.0  —  贴在桌面层上的应用栏
================================================

【运行】
  双击 Fudock.exe ，Dock 出现在屏幕底部。
  如果没反应：可能已经在运行了 —— 看右下角托盘图标（单实例，不会开出第二个）。

【常用操作】
  左键点图标              打开应用
  拖动图标                调整顺序
  把 exe / lnk / bat 拖到条上   添加应用
  右键 Dock 空白处        菜单：置顶、位置、主题、系统区、设置…
  右键托盘图标            显示 / 隐藏 Dock、设置、退出
  Ctrl + Alt + H          隐藏 / 恢复 Windows 任务栏（可在设置里改）
  Win + D 或最小化全部窗口  Dock 会重新出现（默认它就是桌面的一部分）

【设置】
  右键 Dock →「Dock 设置…」（或 tray → Dock 设置…）。
  所有改动即时生效并自动保存；面板可以拖边角改大小，双击标题区恢复默认。
  界面语言：设置 → 其它 → 界面语言，中文 / English 即时切换（也可用 Fudock.exe --lang=en 启动）。

【数据位置】
  %APPDATA%\Fudock\settings.json    配置（图标、位置、外观）
  %APPDATA%\Fudock\error.log        日志（出问题时把这个文件发出来就行）
  不会写系统目录；除你主动打开「开机自启动」时会写一条
  HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Fudock 外，不动注册表。

【卸载】
  1. 托盘右键 → 退出
  2. 删除本程序文件夹
  3. 删除 %APPDATA%\Fudock
  （退出时程序会自己清掉开机自启项，不用手动改注册表）

【配置】
  Windows 10 / 11（x64 或 ARM64 均可），系统自带 .NET Framework 4.8。
  无需安装任何运行库，无需管理员权限。

作者：cz_chenzi     许可：MIT

Fudock 1.0.0  -  an app dock that lives on the desktop layer
============================================================

[RUN]
  Double-click Fudock.exe - the dock appears at the bottom of the screen.
  Nothing happens? It is probably already running - look for the tray icon
  (single instance: a second copy exits immediately).

[EVERYDAY USE]
  Left-click an icon              launch the app
  Drag an icon                    reorder
  Drop an exe / lnk / bat on it   add an app
  Right-click empty dock area     menu: always on top, position, theme, system area, settings...
  Right-click the tray icon       show / hide dock, settings, quit
  Ctrl + Alt + H                  hide / restore the Windows taskbar (changeable in settings)
  Win + D or minimize all        the dock comes back (by default it is part of the desktop)

[SETTINGS]
  Right-click the dock -> "Dock settings..." (or tray -> Dock settings...).
  Every change applies instantly and is saved automatically. Drag any edge or corner
  to resize the panel; double-click the title area to reset it to the default size.
  Interface language: Settings -> Other -> Interface language, Chinese / English, applies instantly
  (or start with Fudock.exe --lang=en).

[DATA]
  %APPDATA%\Fudock\settings.json    settings (icons, position, appearance)
  %APPDATA%\Fudock\error.log        log (just send this file if something goes wrong)
  Nothing is written outside your user profile. The only registry value the app ever
  writes is the opt-in autostart entry:
  HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Fudock

[UNINSTALL]
  1. Tray icon -> Quit
  2. Delete this program folder
  3. Delete %APPDATA%\Fudock
  (The autostart entry is removed on exit - no manual registry editing needed.)

[REQUIREMENTS]
  Windows 10 / 11 (x64 or ARM64) with the built-in .NET Framework 4.8.
  No runtime to install, no administrator rights needed.

Author: cz_chenzi     License: MIT

