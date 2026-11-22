using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace Fudock
{
    public enum DockEdge { Bottom = 0, Top = 1 }

    public enum DockPosition { Edge = 0, Custom = 1 }

    public enum DockTheme { Auto = 0, Dark = 1, Light = 2 }

    /// <summary>界面背景效果：无 / 磨砂（系统亚克力）/ 玻璃（玻璃拟态）。</summary>
    public enum BackdropKind { None = 0, Frosted = 2, Glass = 3 }

    /// <summary>一个被固定到 Dock 上的应用条目（持久化）。</summary>
    public class PinnedEntry
    {
        public string Path { get; set; }     // exe / lnk / 协议(ms-settings:)
        public string Name { get; set; }     // 显示名
        public string Args { get; set; }     // 启动参数
    }

    /// <summary>全部可持久化设置。</summary>
    public class DockSettings
    {
        public int Version { get; set; }
        public int IconSize { get; set; }
        public bool AutoHide { get; set; }
        public double AutoHideDelayMs { get; set; }
        public bool Magnify { get; set; }
        public double MagnifyMax { get; set; }
        public DockEdge Edge { get; set; }
        public DockPosition Position { get; set; }

        /// <summary>自定义位置：Dock 条中心在工作区里的归一化坐标（0~1）。</summary>
        public double PosX { get; set; }
        public double PosY { get; set; }

        public double EdgeMargin { get; set; }
        public DockTheme Theme { get; set; }
        public bool AlwaysOnTop { get; set; }
        public bool HideTaskbar { get; set; }   // 隐藏 Windows 原生任务栏

        // ---- 隐藏任务栏的全局快捷键 ----
        public int HotkeyMods { get; set; }     // MOD_ALT/CTRL/SHIFT/WIN 位组合
        public int HotkeyVk { get; set; }       // 主键虚拟键码，0 = 禁用

        // ---- 设置界面自身的样式（与 Dock 独立）----
        public DockTheme UiTheme { get; set; }
        public double UiOpacity { get; set; }
        public double UiRadius { get; set; }
        public double UiWidth { get; set; }      // 设置窗口宽度（0 = 自动 800）
        public int Language { get; set; }        // 界面语言：0 = 中文，1 = English
        public double UiHeight { get; set; }     // 设置窗口高度（0 = 跟随内容）

        // ---- 背景材质预设（设置界面用）----
        public BackdropKind UiBackdrop { get; set; }

        // ---- 图标倒影反光（只作用于 Dock 图标，不影响系统区/弹层）----
        public bool ReflectOn { get; set; }
        public double ReflectStrength { get; set; }   // 0..60 (%)
        public double ReflectHeight { get; set; }     // 0..100 (% 图标高度)
        public double ReflectBlur { get; set; }       // 0..12 (px)
        public double ReflectFade { get; set; }       // 0..100 (% 淡出位置)


        // ---- Dock 末端的系统区（铃铛 / 状态胶囊 / 溢出入口）----
        public bool SystemArea { get; set; }         // 整个系统区开关（默认开）
        public bool SystemCapsuleTime { get; set; }  // 胶囊里是否显示时间（默认开）
        public bool SystemBadge { get; set; }        // 通知未读角标（默认开）
        public bool TrayOverflow { get; set; }        // 溢出入口 ^（默认开，图标本身永远折叠）
        public bool FullscreenHide { get; set; }      // 全屏游戏/视频时隐藏系统区（默认开）
        public bool PanelAnim { get; set; }           // 面板生长动画（默认开）
        public bool PanelFirst { get; set; }          // 单击胶囊/铃铛先开 Dock 面板（默认关：直接调系统面板）

        public bool ShowRunningDots { get; set; }
        public bool ShowUnpinnedRunning { get; set; }
        public bool ShowLabels { get; set; }
        public bool LaunchAtStartup { get; set; }
        public double BarOpacity { get; set; }
        public List<PinnedEntry> Pinned { get; set; }

        public DockSettings() { }

        public static DockSettings CreateDefault()
        {
            return new DockSettings
            {
                Version = 9,
                IconSize = 46,
                AutoHide = false,
                AutoHideDelayMs = 550,
                Magnify = true,
                MagnifyMax = 1.55,
                Edge = DockEdge.Bottom,
                Position = DockPosition.Edge,
                PosX = 0.5,
                PosY = 0.95,
                EdgeMargin = 10,
                Theme = DockTheme.Auto,
                AlwaysOnTop = false,
                HideTaskbar = false,
                HotkeyMods = 0x0003,     // Ctrl + Alt
                HotkeyVk = 0x48,         // H
                UiTheme = DockTheme.Auto,
                UiOpacity = 0.97,
                UiRadius = 12,
                UiWidth = 0,
                UiHeight = 0,
                Language = 0,
                UiBackdrop = BackdropKind.Frosted,
                ShowRunningDots = true,
                ShowUnpinnedRunning = true,
                ShowLabels = false,
                LaunchAtStartup = false,
                BarOpacity = 0.93,
                ReflectOn = false,
                ReflectStrength = 24,
                ReflectHeight = 45,
                ReflectBlur = 3,
                ReflectFade = 70,

                SystemArea = true,
                SystemCapsuleTime = true,
                SystemBadge = true,
                TrayOverflow = true,
                FullscreenHide = true,
                PanelAnim = true,
                PanelFirst = false,
                Pinned = new List<PinnedEntry>()
            };
        }

        public void Clamp()
        {
            IconSize = Math.Min(96, Math.Max(24, IconSize));
            MagnifyMax = Math.Min(2.0, Math.Max(1.0, MagnifyMax));
            AutoHideDelayMs = Math.Min(5000, Math.Max(0, AutoHideDelayMs));
            EdgeMargin = Math.Min(120, Math.Max(0, EdgeMargin));
            // 允许 0（完全透明）：此时 Dock 会用"悬停才显形"的方式保证还能被发现/点击
            BarOpacity = Math.Min(1.0, Math.Max(0.0, BarOpacity));
            UiOpacity = Math.Min(1.0, Math.Max(0.55, UiOpacity));
            UiWidth = Math.Min(4000, Math.Max(0, UiWidth));
            Language = Language == L.English ? L.English : L.Chinese;
            UiHeight = Math.Min(4000, Math.Max(0, UiHeight));
            ReflectStrength = Math.Min(60, Math.Max(0, ReflectStrength));
            ReflectHeight = Math.Min(100, Math.Max(0, ReflectHeight));
            ReflectBlur = Math.Min(12, Math.Max(0, ReflectBlur));
            ReflectFade = Math.Min(100, Math.Max(0, ReflectFade));

            UiRadius = Math.Min(26, Math.Max(0, UiRadius));
            PosX = Math.Min(1.0, Math.Max(0.0, PosX));
            PosY = Math.Min(1.0, Math.Max(0.0, PosY));
            if (HotkeyVk < 0) HotkeyVk = 0;
            if (HotkeyVk > 0xFF) HotkeyVk = 0;
            if (HotkeyMods < 0) HotkeyMods = 0;
            if ((HotkeyMods & 0xF) == 0) HotkeyMods = 0;   // 没有修饰键就当作禁用
            if (Pinned == null) Pinned = new List<PinnedEntry>();
            if (!Enum.IsDefined(typeof(DockEdge), Edge)) Edge = DockEdge.Bottom;
            if (!Enum.IsDefined(typeof(DockPosition), Position)) Position = DockPosition.Edge;
            if (!Enum.IsDefined(typeof(DockTheme), Theme)) Theme = DockTheme.Auto;
            if (!Enum.IsDefined(typeof(DockTheme), UiTheme)) UiTheme = DockTheme.Auto;
            // 背景效果只保留 无 / 磨砂 / 玻璃（旧配置里的其它值一律归一成磨砂）
            if (UiBackdrop != BackdropKind.None
                && UiBackdrop != BackdropKind.Frosted
                && UiBackdrop != BackdropKind.Glass)
                UiBackdrop = BackdropKind.Frosted;
        }

        public DockSettings Clone()
        {
            var c = (DockSettings)MemberwiseClone();
            c.Pinned = new List<PinnedEntry>();
            foreach (var p in Pinned)
                c.Pinned.Add(new PinnedEntry { Path = p.Path, Name = p.Name, Args = p.Args });
            return c;
        }
    }

    /// <summary>一个顶层窗口。</summary>
    public class AppWindow
    {
        public IntPtr Handle { get; set; }
        public int ProcessId { get; set; }
        public string Title { get; set; }
        public string ExePath { get; set; }
    }

    /// <summary>按可执行文件路径聚合的运行中应用。</summary>
    public class RunningApp
    {
        public string ExePath { get; set; }
        public string ProcessName { get; set; }
        public List<AppWindow> Windows { get; set; }

        /// <summary>该进程的祖先可执行文件路径（小写），用于多进程应用的归属匹配。</summary>
        public List<string> Chain { get; set; }

        public RunningApp() { Windows = new List<AppWindow>(); Chain = new List<string>(); }
    }

    /// <summary>Dock 上的一个图标（运行时对象）。</summary>
    public class DockTile
    {
        public string DisplayName;
        public string TargetPath;      // 启动目标（exe/lnk/协议）
        public string LaunchArgs;
        public string MatchExe;        // 用于匹配运行中窗口的规范化 exe 路径（小写）
        public ImageSource Icon;
        public bool Pinned;
        public bool IsSeparator;
        public List<AppWindow> Windows = new List<AppWindow>();

        public bool IsRunning { get { return Windows != null && Windows.Count > 0; } }

        public DockTile CloneShallow()
        {
            return (DockTile)MemberwiseClone();
        }
    }
}
