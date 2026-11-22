## 浮坞 Fudock 1.0.0

贴在桌面层的 Windows 11 Dock 栏 · 单文件 284 KB · 零运行时依赖 · 中英双语

**下载**：`Fudock-1.0.0.zip`（165 KB，解压即用：`Fudock.exe` + `readme.txt`（中英对照）+ `LICENSE`）

**SHA-256（zip）**：`4016E63BE1A1E446667712844603C0C05217DC3E49AD17ABF1BEEDD0F4448398`
**SHA-256（Fudock.exe）**：`5C73962542F5D2C4C6E390F5FBBEA5AB9ED450A26B412CE62D3F161DD0942CF6`

### ⚠️ 首次运行提示

本程序**没有代码签名**（个人项目，未购买证书），Windows SmartScreen 可能提示「已保护你的电脑」。
点「更多信息」→「仍要运行」即可。建议先用上面的 SHA-256 校验文件完整性。

### 亮点

- **桌面层 Dock**：默认不置顶，被窗口正常覆盖；`Win + D` 或最小化后自动回来；一键可切回始终置顶
- **单文件 284 KB**：无 .NET SDK、无 Node/Electron、无管理员权限，只用系统自带 .NET Framework 4.8
- **Fluent 设置面板**：全自绘卡片界面，改动即时生效并自动保存，可拖边角缩放
- **完全透明**：0% 时全透明，悬停显出细轮廓，仍然点得中
- **图标倒影**：强度 / 高度 / 模糊 / 淡出可调
- **系统区**：铃铛未读角标、状态胶囊（网络 / 音量 / 电池 / 时间）、`^` 直接列出后台托盘应用
- **多窗口预览**：悬停看实时缩略图，可切换同应用窗口
- **中英双语**：设置里一键切换，或 `Fudock.exe --lang=en`
- `Ctrl + Alt + H` 隐藏 / 恢复 Windows 任务栏；托盘菜单、开机自启、单实例

### 要求

Windows 10 / 11（x64 或 ARM64），系统自带 .NET Framework 4.8。无需安装任何运行库，无需管理员权限。

### 数据与卸载

- 配置：`%APPDATA%\Fudock\settings.json`；日志：`%APPDATA%\Fudock\error.log`
- 除主动开启「开机自动启动」时写一条 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Fudock` 外，不改注册表
- 卸载：托盘右键「退出」→ 删除程序目录 → 删除 `%APPDATA%\Fudock`（自启项在退出时自动清理）

### 已知限制

- 只在主显示器停靠
- UWP / Store 应用（设置、计算器、照片等）无法固定到 Dock
- 需要管理员权限的程序不会弹 UAC（Windows UIPI 限制）

---

## Fudock 1.0.0 (English)

A desktop-layer dock for Windows 11. One 284 KB exe, no runtime, bilingual UI.

- **Desktop-layer dock** — not topmost by default, so windows cover it; `Win + D` brings it back; one toggle for classic always-on-top.
- **One 284 KB exe** — no .NET SDK, no Node/Electron, no admin rights, just the built-in .NET Framework 4.8.
- **Fluent settings** — hand-drawn cards, applies and saves instantly, resizable by dragging any edge.
- **Fully transparent mode**, **icon reflection**, **system area** (unread badge / status capsule / `^` tray apps), **window previews**, drag-to-add & reorder.
- **Chinese / English UI**, switch in Settings or start with `Fudock.exe --lang=en`.
- Requires Windows 10 / 11 (x64 or ARM64). Data in `%APPDATA%\Fudock`.
- Not code-signed: if SmartScreen complains, choose **More info → Run anyway** and check the SHA-256 above.
