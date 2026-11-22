using System;
using System.Collections.Generic;

namespace Fudock
{
    /// <summary>
    /// 极简双语表（中文 / English）。界面文字用 L.T("中文原文") 包一层：
    /// 中文模式原样返回，英文模式查表；查不到就退回中文，绝不会显示空白。
    /// 之所以用"中文原文当键"，是为了让代码里仍然能直接读出这句话是什么意思。
    /// </summary>
    internal static class L
    {
        /// <summary>true = English UI.</summary>
        public static bool En;

        public const int Chinese = 0;
        public const int English = 1;

        public static void Apply(int language) { En = language == English; }

        public static string T(string zh)
        {
            if (zh == null) return "";
            if (!En) return zh;
            string en;
            return Map.TryGetValue(zh, out en) ? en : zh;
        }

        /// <summary>带占位符的版本：T("已添加 {0} 个应用", n)。</summary>
        public static string T(string zh, params object[] args)
        {
            string fmt = T(zh);
            try { return string.Format(fmt, args); }
            catch { return fmt; }
        }

        /// <summary>两选一，给不想进词典的临时文案用。</summary>
        public static string Pick(string zh, string en) { return En ? en : zh; }

        private static readonly Dictionary<string, string> Map =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ---------- settings window ----------
            { "设置", "Settings" },
            { " 设置", " settings" },
            { "改动即时生效并自动保存　·　v", "Changes apply instantly and are saved automatically　·　v" },
            { "重新扫描应用", "Rescan apps" },
            { "恢复默认外观", "Reset appearance" },
            { "作者：", "Author: " },
            { "完成", "Done" },
            { "Dock 外观", "Dock appearance" },
            { "图标倒影反光", "Icon reflection" },
            { "设置界面", "Settings window" },
            { "交互与动画", "Interaction & motion" },
            { "位置与层级", "Position & layering" },
            { "系统区", "System area" },
            { "其它", "Other" },
            { "无", "None" },
            { "磨砂", "Frosted" },
            { "玻璃", "Glass" },
            { "界面背景效果", "Window backdrop" },
            { "预览：真实壁纸色块上的 Dock 条（跟随主题与不透明度）",
              "Preview: the dock bar over a wallpaper swatch (theme + opacity)" },
            { "跟随系统", "Follow system" },
            { "浅色", "Light" },
            { "深色", "Dark" },
            { "Dock 主题", "Dock theme" },
            { "背景不透明度", "Background opacity" },
            { "完全透明", "Fully transparent" },
            { "完全透明：鼠标悬停时才会显出一层细轮廓，保证还能找到并点击",
              "Fully transparent: a faint outline appears on hover so it can still be found and clicked" },
            { "拉到 0% 可完全透明，悬停时会临时显示轮廓",
              "Drag to 0% for full transparency; an outline shows on hover" },
            { "图标大小", "Icon size" },
            { "跟随 Dock", "Follow dock" },
            { "深浅", "Light / dark" },
            { "界面透明度", "Window opacity" },
            { "直角", "Square" },
            { "圆角半径", "Corner radius" },
            { "拉到 0 就是直角（方角）界面；只影响这个设置窗口，不影响 Dock。",
              "Drag to 0 for square corners. Affects this settings window only, not the dock." },
            { "屏幕底部", "Bottom" },
            { "屏幕顶部", "Top" },
            { "自定义", "Custom" },
            { "停靠位置", "Position" },
            { "始终置顶", "Always on top" },
            { "关闭则只在桌面上显示，会被其他窗口覆盖",
              "When off, the dock stays on the desktop and other windows cover it" },
            { "隐藏 Windows 任务栏", "Hide the Windows taskbar" },
            { "把系统任务栏藏起来，退出 Dock 时自动恢复",
              "Hides the system taskbar; restored automatically when the dock exits" },
            { "切换任务栏快捷键", "Taskbar hotkey" },
            { "点击后按下组合键；右键清除", "Click, then press a key combo; right-click to clear" },
            { "该组合键可能已被其它程序占用，请换一个",
              "That key combo may be taken by another app - pick another one" },
            { "悬停放大", "Hover magnification" },
            { "鼠标划过图标时放大并让位", "Icons grow and shift aside as the pointer passes" },
            { "放大倍数", "Magnification" },
            { "自动隐藏", "Auto hide" },
            { "收起后只在屏幕边缘留 4px 触发条", "When hidden, a 4 px trigger strip stays at the screen edge" },
            { "收起延迟", "Hide delay" },
            { "显示系统区", "Show system area" },
            { "Dock 末端的 铃铛 · 状态胶囊 · 溢出入口",
              "Bell · status capsule · overflow entry at the dock's end" },
            { "胶囊显示时间", "Show time in capsule" },
            { "关闭则只显示网络 / 音量 / 电池", "When off, only network / volume / battery are shown" },
            { "通知未读角标", "Unread badge" },
            { "有新通知时铃铛上显示数字，并轻脉冲一次",
              "Shows a count on the bell and pulses once when new notifications arrive" },
            { "溢出入口 ^", "Overflow entry ^" },
            { "第三方托盘图标始终折在系统托盘里，这里只做入口",
              "Third-party tray icons stay folded in the system tray; this is only a shortcut" },
            { "全屏时自动隐藏", "Hide in full screen" },
            { "全屏游戏 / 视频 / 演示时，系统区跟着 Dock 让路",
              "In full-screen games / video / presentations the system area steps aside with the dock" },
            { "面板生长动画", "Panel grow animation" },
            { "从 Dock 边缘展开，170ms ease-out", "Expands from the dock edge, 170 ms ease-out" },
            { "单击先开 Dock 面板", "Single click opens the dock panel first" },
            { "关闭 = 单击胶囊/铃铛直接调 Windows 快速设置/通知中心",
              "When off, clicking the capsule / bell opens Windows quick settings / notification center directly" },
            { "启用倒影", "Enable reflection" },
            { "只作用于 Dock 图标（不影响状态胶囊和面板）",
              "Applies to dock icons only (not the capsule or panels)" },
            { "强度（越低越含蓄）", "Strength (lower is subtler)" },
            { "高度（占图标高度的比例）", "Height (% of icon height)" },
            { "模糊", "Blur" },
            { "淡出（越大越快消失）", "Fade (higher fades out sooner)" },
            { "显示运行中但未固定的应用", "Show running but unpinned apps" },
            { "开机自动启动", "Start with Windows" },
            { "界面语言", "Interface language" },
            { "立即切换，无需重启", "Applies instantly, no restart needed" },
            { "中文", "中文" },
            { "English", "English" },

            // ---------- dock menus / hints ----------
            { "松手添加到 Dock", "Release to add to the dock" },
            { "打开快速设置（Win + A）", "Open quick settings (Win + A)" },
            { "打开通知中心（Win + N）", "Open notification center (Win + N)" },
            { "通知设置…", "Notification settings…" },
            { "任务栏设置…", "Taskbar settings…" },
            { "修复：重启资源管理器（重建任务栏）", "Repair: restart Explorer (rebuild the taskbar)" },
            { "显示时间", "Show time" },
            { "隐藏系统区", "Hide system area" },
            { "显示 Windows 任务栏", "Show Windows taskbar" },
            { "切换到： {0}", "Switch to: {0}" },
            { "最小化 / 还原", "Minimize / restore" },
            { "关闭窗口", "Close window" },
            { "打开", "Open" },
            { "从 Dock 移除", "Remove from dock" },
            { "固定到 Dock", "Pin to dock" },
            { "打开文件所在位置", "Show in Explorer" },
            { "Dock 设置…", "Dock settings…" },
            { "添加应用…", "Add apps…" },
            { "显示未固定的运行应用", "Show unpinned running apps" },
            { "始终置顶显示（会盖住其他窗口）", "Always on top (covers other windows)" },
            { "自定义位置（拖动调整）…", "Custom position (drag to adjust)…" },
            { "主题", "Theme" },
            { "退出 Dock", "Quit dock" },
            { "选择要固定到 Dock 的应用", "Choose apps to pin to the dock" },
            { "应用程序 (*.exe;*.lnk;*.bat)|*.exe;*.lnk;*.bat|所有文件 (*.*)|*.*",
              "Applications (*.exe;*.lnk;*.bat)|*.exe;*.lnk;*.bat|All files (*.*)|*.*" },
            { "已添加到 Dock", "Added to the dock" },
            { "已添加 {0} 个应用", "Added {0} apps" },
            { "只能添加 exe / lnk / bat / cmd / url", "Only exe / lnk / bat / cmd / url can be added" },
            { "  ({0} 个窗口)", "  ({0} windows)" },

            // ---------- system panel ----------
            { "通知", "Notifications" },
            { "Windows 原生通知中心", "Windows notification center" },
            { "状态", "Status" },
            { "网络 · 音量 · 电源", "Network · volume · power" },
            { "系统托盘", "System tray" },
            { "第三方图标由 Windows 托管", "Third-party icons are handled by Windows" },
            { "{0} 条未读", "{0} unread" },
            { "通知都在系统通知中心里", "Notifications live in the Windows notification center" },
            { "Dock 不复制通知内容，也不弹自己的 toast——只负责把你送过去。",
              "The dock does not copy notification content or raise its own toasts - it just takes you there." },
            { "打开通知中心", "Open notification center" },
            { "网络", "Network" },
            { "已静音", "Muted" },
            { "不可用", "Unavailable" },
            { "音量", "Volume" },
            { "电源", "Power" },
            { "打开快速设置", "Open quick settings" },
            { "没读到后台应用", "No background apps found" },
            { "系统托盘当前是空的（应用可能还没注册图标），或者读取超时。可点下面刷新。",
              "The system tray is empty right now (apps may not have registered icons yet), or reading timed out. Use refresh below." },
            { "刷新列表", "Refresh list" },
            { "读取系统托盘", "Reading the system tray" },
            { "展开系统托盘", "Open the system tray" },
            { "系统原生", "System" },
            { "打开系统设置", "Open system settings" },

            // ---------- OS-derived status text ----------
            { "Wi-Fi：{0}", "Wi-Fi: {0}" },
            { "有线网络：{0}", "Ethernet: {0}" },
            { "有线网络", "Ethernet" },
            { "无网络连接", "No network" },
            { "音量不可用", "Volume unavailable" },
            { "音量 {0}%", "Volume {0}%" },
            { "充电中 {0}%", "Charging {0}%" },
            { "充电中", "Charging" },
            { "电池 {0}%", "Battery {0}%" },
            { "使用电池", "On battery" },
            { "电源状态未知", "Power state unknown" },
            // ---------- system area tooltips ----------
            { "通知中心（Win + N）", "Notification center (Win + N)" },
            { "通知中心（Win + N）· {0} 条未读", "Notification center (Win + N) · {0} unread" },
            { "系统托盘 · 第三方图标折在这里", "System tray · third-party icons live here" },
            { "已接通电源", "Plugged in" },
            { "网络：", "Network: " },
            { "音量：", "Volume: " },
            { "电源：", "Power: " },
            { "单击打开 Windows 快速设置（Win + A）", "Click to open Windows quick settings (Win + A)" },

            // ---------- tray menu ----------
            { "显示 / 隐藏 Dock", "Show / hide dock" },
            { "显示 / 隐藏 Windows 任务栏", "Show / hide the Windows taskbar" },
            { "自定义 Dock 位置…", "Custom dock position…" },
            { "紧急恢复：Windows 任务栏", "Emergency: restore the Windows taskbar" },
            { "退出", "Quit" },

            // ---------- startup / dialogs / flyout ----------
            { "浮坞 启动失败：\n", "Fudock failed to start:\n" },
            { "浮坞", "Fudock" },
            { "  {0} 个窗口", "  {0} windows" },
            { "还有 {0} 个窗口…", "{0} more windows…" },
            { "已最小化 / 无法预览", "Minimized / no preview" },
            { "已最小化", "Minimized" },
            { "(无标题)", "(untitled)" },
            { "拖动 Dock 到任意位置", "Drag the dock anywhere" },
            { "取消", "Cancel" },
            { "保存位置", "Save position" },
        };
    }
}
