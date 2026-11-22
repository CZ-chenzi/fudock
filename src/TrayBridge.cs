using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Fudock
{
    /// <summary>托盘里的一个后台应用。</summary>
    internal sealed class TrayIconEntry
    {
        public string Name = "";
        public int Index;                 // 在溢出弹层里的顺序（同名时用它区分）
        public ImageSource Icon;          // 从运行中的进程取到的真实图标
        public double ClickX, ClickY;     // 上次读到的可点坐标（物理像素）
        public bool InTaskbar;            // true = 直接显示在任务栏托盘里；false = 折在系统溢出弹层里
    }

    /// <summary>
    /// 第三方托盘图标桥接层（Level 3）。
    ///
    /// 为什么这么做：
    ///  · Win11 25H2 的托盘已经全是 XAML，`TrayNotifyWnd` 下面没有可读的 `ToolbarWindow32`，
    ///    老的 `TB_GETBUTTON` / `ITrayNotify` 路线在这个版本上不可用；
    ///  · 但溢出弹层 `TopLevelWindowForOverflowXamlIsland` 是标准 UIA 提供方，
    ///    `AutomationElement.FindAll` 能直接拿到每个图标的**名字 + 包围盒**（实测一行 12 个）；
    ///  · 图标本身没有公开的取图接口，所以按包围盒直接抓屏（24×24 像素），拿到的是真图标。
    ///
    /// 代价：读列表期间要短暂弹出一次 Windows 的溢出弹层（约 400ms），所以**结果要缓存**，
    /// 并且只在用户点开 ^ 且缓存过期时才重新读。
    /// </summary>
    internal static class TrayBridge
    {
        private const string OverflowClass = "TopLevelWindowForOverflowXamlIsland";

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string cls, string title);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr h);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);

        private const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;

        // ---------------- 缓存 ----------------
        private static List<TrayIconEntry> _cache;
        private static DateTime _cacheAt = DateTime.MinValue;
        private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

        public static bool HasCache { get { return _cache != null && _cache.Count > 0; } }

        /// <summary>缓存是否还能用（面板会先用缓存，过期了再后台刷新）。</summary>
        public static bool CacheFresh
        {
            get { return _cache != null && (DateTime.UtcNow - _cacheAt) < Ttl; }
        }

        public static List<TrayIconEntry> Cached
        {
            get { return _cache ?? new List<TrayIconEntry>(); }
        }

        // ---------------- 读列表 ----------------
        /// <summary>
        /// 读当前所有托盘图标（含被折叠进 ^ 的那些）。
        /// 期间会短暂打开一次系统溢出弹层，读完立刻关掉。
        /// </summary>
        public static List<TrayIconEntry> Enumerate(bool force)
        {
            if (!force && CacheFresh) return Cached;

            var list = new List<TrayIconEntry>();
            try
            {
                // 任务栏自动隐藏时，托盘区连 UIA 都读不到（窗口滑到屏幕外），
                // 所以先用 Win+B 把它叫出来，再分别读"直接显示在任务栏的"和"折在溢出里的"。
                ShellActions.FocusTray();

                // ① 任务栏上直接可见的托盘图标（XAML 宿主 Windows.UI.Composition.DesktopWindowContentBridge）
                ReadTaskbarTray(list, false);

                // ② 溢出弹层里的隐藏图标
                IntPtr ovf = EnsureOverflowOpen();
                if (ovf != IntPtr.Zero) ReadButtons(list, ovf, "hidden", true);

                SettingsStore.Log("TrayBridge: 共读到 " + list.Count + " 个托盘图标（任务栏可见 " +
                    CountInTaskbar(list) + "）");

                if (list.Count == 0) SettingsStore.Log("TrayBridge: 一条都没读到");
            }
            catch (Exception ex)
            {
                SettingsStore.Log("TrayBridge.Enumerate: " + ex.Message);
            }
            finally
            {
                CloseOverflow();
            }

            if (list.Count > 0)
            {
                _cache = list;
                _cacheAt = DateTime.UtcNow;
            }
            return list.Count > 0 ? list : Cached;
        }

        private static int CountInTaskbar(List<TrayIconEntry> list)
        {
            int n = 0;
            foreach (TrayIconEntry e in list) if (e.InTaskbar) n++;
            return n;
        }

        /// <summary>读任务栏上直接显示的那排托盘图标。</summary>
        private static void ReadTaskbarTray(List<TrayIconEntry> list, bool log)
        {
            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero) return;

            IntPtr notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
            double trayLeft = 0;
            if (notify != IntPtr.Zero)
            {
                RECT nr;
                if (GetWindowRect(notify, out nr)) trayLeft = nr.Left - 6;
            }

            IntPtr bridge = FindWindowEx(tray, IntPtr.Zero, BridgeClass, null);
            if (bridge == IntPtr.Zero) return;
            ReadButtons(list, bridge, "taskbar", trayLeft > 0, trayLeft);
        }

        private const string BridgeClass = "Windows.UI.Composition.DesktopWindowContentBridge";

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr h, out RECT r);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        /// <summary>把某个 UIA 根下面的按钮收进列表（去重 + 过滤系统项）。</summary>
        private static void ReadButtons(List<TrayIconEntry> list, IntPtr rootHwnd, string source, bool onlyRightOf, double minX = 0)
        {
            AutomationElement root = AutomationElement.FromHandle(rootHwnd);
            if (root == null) return;

            AutomationElementCollection all;
            try { all = root.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition); }
            catch (Exception ex) { SettingsStore.Log("TrayBridge(" + source + "): " + ex.Message); return; }

            int added = 0;
            foreach (AutomationElement el in all)
            {
                AutomationElement.AutomationElementInformation info = el.Current;
                if (info.ControlType != ControlType.Button) continue;

                System.Windows.Rect r = info.BoundingRectangle;
                if (r.Width < 8 || r.Height < 8) continue;
                if (onlyRightOf && r.X < minX) continue;          // 只要托盘区，不要左边任务栏应用按钮

                string name = CleanName(info.Name);
                if (name.Length == 0) continue;
                if (IsJunk(name)) continue;
                if (FindByName(list, name) != null) continue;      // 去重

                var entry = new TrayIconEntry
                {
                    Name = name,
                    Index = list.Count,
                    InTaskbar = source == "taskbar",
                    ClickX = r.X + r.Width / 2,
                    ClickY = r.Y + r.Height / 2
                };
                entry.Icon = ResolveIcon(entry.Name);
                list.Add(entry);
                added++;

                // 正常命中不再逐条打日志（发布版日志要干净）；
                // 只有"退化成字母块"这种真问题才记一行，方便用户反馈时定位。
                if (entry.Icon == null)
                {
                    SettingsStore.Log("TrayBridge 图标未命中: '" + entry.Name + "'（退化为首字母块）");
                }
            }
            SettingsStore.Log("TrayBridge(" + source + "): +" + added);
        }

        private static string CleanName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";

            // 托盘项的 Name 常常是"名字 + 换行 + 状态文本"，甚至把两种状态拼在一起
            // （"Steam - 正在同步""NVIDIA …（1 个显示器，10 个程序）  NVIDIA …（22 个程序）"）。
            // 只留第一段，避免列表里出现又长又重复的标题。
            int cut = raw.IndexOf('\n');
            if (cut < 0) cut = raw.IndexOf('\r');
            if (cut > 0) raw = raw.Substring(0, cut);

            int dbl = raw.IndexOf("  ", StringComparison.Ordinal);
            if (dbl > 0) raw = raw.Substring(0, dbl);

            return raw.Trim();
        }

        /// <summary>过滤掉不是"应用"的托盘项：时钟、输入法、显示桌面、chevron、系统四件套。</summary>
        private static bool IsJunk(string name)
        {
            if (name.StartsWith("浮坞", StringComparison.OrdinalIgnoreCase)) return true;

            string lower = name.ToLowerInvariant();
            string[] junk =
            {
                "显示隐藏的图标", "显示桌面", "任务栏输入指示", "输入指示",
                "显示桌面按钮", "show hidden icons", "show desktop"
            };
            foreach (string j in junk)
                if (lower.Contains(j)) return true;

            // 时钟：名字里带 HH:mm
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"\b\d{1,2}:\d{2}\b")) return true;

            // 系统四件套已经在状态胶囊里，不重复塞进托盘面板
            string[] core = { "网络", "音量", "电池", "电源", "麦克风" };
            foreach (string k in core)
                if (name.StartsWith(k, StringComparison.Ordinal)) return true;

            return false;
        }

        private static TrayIconEntry FindByName(List<TrayIconEntry> list, string name)
        {
            foreach (TrayIconEntry e in list)
                if (e.Name == name) return e;
            return null;
        }
        /// <summary>打开系统溢出弹层（已开就直接用）。</summary>
        private static IntPtr EnsureOverflowOpen()
        {
            IntPtr h = FindWindow(OverflowClass, null);
            if (h != IntPtr.Zero && IsWindowVisible(h)) return Settle(h);

            // 我们已经先用 Win+B 把焦点放到托盘上了，这里直接 Enter 展开就行；
            // 再发一次 Win+B 反而会把焦点移走，导致 Enter 落空（曾经就这样读到 0 个）。
            ShellActions.TapKeyPublic(0x0D);
            for (int i = 0; i < 14; i++)
            {
                System.Threading.Thread.Sleep(50);
                h = FindWindow(OverflowClass, null);
                if (h != IntPtr.Zero && IsWindowVisible(h)) return Settle(h);
            }

            // 还不行就走完整路线（Win+B → Enter）再来一次
            if (ShellActions.OpenSystemTrayOverflow())
            {
                for (int i = 0; i < 14; i++)
                {
                    System.Threading.Thread.Sleep(50);
                    h = FindWindow(OverflowClass, null);
                    if (h != IntPtr.Zero && IsWindowVisible(h)) return Settle(h);
                }
            }
            return IntPtr.Zero;
        }

        /// <summary>弹层是淡入动画：UIA 立刻有元素，但像素/子项还没齐，等它稳定一下。</summary>
        private static IntPtr Settle(IntPtr h)
        {
            System.Threading.Thread.Sleep(320);
            return h;
        }
        private static void CloseOverflow()
        {
            try
            {
                IntPtr h = FindWindow(OverflowClass, null);
                if (h == IntPtr.Zero || !IsWindowVisible(h)) return;
                ShellActions.TapKeyPublic(0x1B);               // Esc
                System.Threading.Thread.Sleep(120);
            }
            catch { }
        }

        // ---------------- 图标来源 ----------------
        // 为什么不用抓屏：实测这台机器的系统溢出弹层**只渲染背景、不画图标**
        //（按 UIA 给的精确矩形去 CopyFromScreen，抓到的是弹层的模糊底图）。
        // 所以走"托盘名 → 正在运行的进程 → 进程真实图标"，并且是多路匹配：
        //   ① 进程名 / exe 名 / 文件说明(FileDescription) / 产品名(ProductName) 都当关键字
        //   ② 先精确、再包含、最后按"单词交集"打分，命中分数最高的
        //   ③ 中文名用别名表兜底（虚拟机→vmware、加速器→uu、显卡→amd/nvidia …）
        //   ④ 系统自带条目（蓝牙 / 安全中心 / 声音 / 网络 / 电池）用内置矢量图标
        //   ⑤ 全都不行才退化成首字母块
        private sealed class ProcKey
        {
            public string Path;
            public string ProcName = "";      // 进程名（归一化），用来区分"产品主程序"和"组件"
            public List<string> Keys = new List<string>();
        }

        private static List<ProcKey> BuildProcessIndex()
        {
            var list = new List<ProcKey>();
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcesses())
                {
                    try
                    {
                        string path = ExePath(p);
                        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) continue;

                        var item = new ProcKey { Path = path, ProcName = Norm(p.ProcessName) };
                        AddKey(item, p.ProcessName);
                        AddKey(item, System.IO.Path.GetFileNameWithoutExtension(path));
                        // 目录名也当一个键：D:\UU\GameViewer\bin\GameViewer.exe → "gameviewer","uu"
                        try
                        {
                            string dir = System.IO.Path.GetDirectoryName(path);
                            if (!string.IsNullOrEmpty(dir))
                            {
                                AddKey(item, System.IO.Path.GetFileName(dir));
                                string up = System.IO.Path.GetDirectoryName(dir);
                                if (!string.IsNullOrEmpty(up)) AddKey(item, System.IO.Path.GetFileName(up));
                            }
                        }
                        catch { }
                        try
                        {
                            var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
                            AddKey(item, vi.FileDescription);
                            AddKey(item, vi.ProductName);
                        }
                        catch { }
                        if (item.Keys.Count > 0)
                        {
                            list.Add(item);
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }
            SettingsStore.Log("TrayBridge: 进程索引 " + list.Count + " 条");
            return list;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr h, uint flags, System.Text.StringBuilder name, ref int size);

        /// <summary>
        /// 取进程的 exe 路径。
        /// 不能用 Process.MainModule：进程一旦提权（UU加速器、部分游戏工具都是），
        /// MainModule 会抛 Win32Exception，那些应用的图标就再也匹配不到——
        /// 之前 UU 的两个图标张冠李戴就是这么来的。
        /// PROCESS_QUERY_LIMITED_INFORMATION(0x1000) 对同用户会话的提权进程是可用的。
        /// </summary>
        private static string ExePath(System.Diagnostics.Process p)
        {
            try
            {
                IntPtr h = OpenProcess(0x1000, false, p.Id);
                if (h != IntPtr.Zero)
                {
                    try
                    {
                        var sb = new System.Text.StringBuilder(1024);
                        int size = sb.Capacity;
                        if (QueryFullProcessImageName(h, 0, sb, ref size)) return sb.ToString();
                    }
                    finally { CloseHandle(h); }
                }
            }
            catch { }

            try { return p.MainModule.FileName; }
            catch { return null; }
        }

        private static void AddKey(ProcKey item, string raw)
        {
            string k = Norm(raw);
            // 最小长度 2：UU加速器的主程序就叫 uu.exe，名字归一化后只有 "uu"，
            // 门槛设成 3 会把它整个漏掉，结果图标只能匹到 uu_ball.exe（组件）——曾经就是这样错的。
            if (k.Length < 2) return;
            if (!item.Keys.Contains(k)) item.Keys.Add(k);
        }

        private static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        // 中文托盘名 → 进程关键字（匹配词 | 排除词）。同一家公司的两个产品必须分开，
        // 否则 UU加速器 / UU远程 会都匹到同一个 exe，图标自然张冠李戴。
        private static readonly string[,] Aliases =
        {
            // 匹配词 | 排除词（排除词为空串表示不排除）
            { "虚拟机",  "vmware|virtualbox|vbox|hyper", "" },
            { "远程",    "remote|viewer|gameviewer",     "accelerat|booster" },
            { "加速器",  "uu|accel|booster",             "remote|viewer|gameviewer" },
            { "uu",      "uu|netease|gameviewer",        "" },
            { "网易",    "uu|cloudmusic|netease",        "" },
            { "微信",    "wechat|weixin",                "" },
            { "qq",      "qq|tencent",                   "" },
            { "钉钉",    "dingtalk",                     "" },
            { "飞书",    "feishu|lark",                  "" },
            { "云音乐",  "cloudmusic|netease",           "" },
            { "输入法",  "ime|sogou|qqpinyin|baidu",     "" },
            { "显卡",    "amd|nvidia|radeon|igfx",       "" },
            { "安全",    "securityhealth|msmpeng|defender", "" },
            { "驱动",    "driver|amd|nvidia|realtek",    "" },
            { "wallpaper", "wallpaper",                  "" },
            { "steam",   "steam",                        "" },
            { "更新",    "update|updater",               "" },
            { "下载",    "download|thunder|xunlei",      "" },
        };

        private static ImageSource ResolveIcon(string trayName)
        {
            _lastIconSource = "";
            try
            {
                string key = Norm(trayName);
                string lower = (trayName ?? "").ToLowerInvariant();

                // ① 系统自带条目：直接给内置矢量图标
                ImageSource sys = SystemGlyph(lower);
                if (sys != null) { _lastIconSource = "内置矢量图"; return sys; }

                if (key.Length == 0) return null;

                List<ProcKey> procs = _procIndex ?? (_procIndex = BuildProcessIndex());

                // ② 精确命中（进程名/exe/描述 完全相同）
                foreach (ProcKey pk in procs)
                    foreach (string k in pk.Keys)
                        if (k == key) return Use(pk.Path);

                // ④ 别名表（中文名优先，带排除词）
                for (int i = 0; i < Aliases.GetLength(0); i++)
                {
                    string from = Aliases[i, 0];
                    if (lower.IndexOf(from, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    string[] wants = Aliases[i, 1].Split('|');
                    string[] denies = Aliases[i, 2].Length == 0
                        ? new string[0] : Aliases[i, 2].Split('|');

                    // 同一个别名组里优先"短键"（uu.exe 就是 UU加速器主程序，
                    // uu_ball/uu_launcher 只是组件，图标可能不是产品图标）
                    var hits = new List<ProcKey>();
                    foreach (string w in wants)
                    {
                        foreach (ProcKey pk in procs)
                        {
                            bool ok = false;
                            bool bad = false;
                            foreach (string k in pk.Keys)
                            {
                                if (k.Contains(w)) ok = true;
                                foreach (string d in denies)
                                    if (d.Length > 0 && k.Contains(d)) bad = true;
                            }
                            if (ok && !bad && !hits.Contains(pk)) hits.Add(pk);
                        }
                    }
                    if (hits.Count == 0) continue;

                    // 组内挑"最像产品主程序"的那个：
                    //  · 键和别名 token 完全一致（uu）优先；
                    //  · 带 ball / helper / cloudsyn / service / update 的是组件，图标不是产品图标，排后面；
                    //  · 再按键长度短的优先。
                    string token = wants[0];
                    hits.Sort(delegate (ProcKey a, ProcKey b)
                    {
                        int wa = Rank(a, token), wb = Rank(b, token);
                        if (wa != wb) return wa.CompareTo(wb);
                        return ShortestKey(a).Length.CompareTo(ShortestKey(b).Length);
                    });
                    _lastIconSource = "";
                    if (hits.Count > 0 && SettingsStore.LogPath != null)
                    {
                        string dbg = "TrayBridge 候选(" + from + "):";
                        for (int hi = 0; hi < hits.Count && hi < 4; hi++)
                            dbg += " " + System.IO.Path.GetFileName(hits[hi].Path) + "#" + Rank(hits[hi], token);
                        SettingsStore.Log(dbg);
                    }
                    foreach (ProcKey pk in hits)
                    {
                        ImageSource img = Use(pk.Path);
                        if (img != null) return img;
                    }
                }

                // ③ 包含关系：把所有命中的进程都收起来，再按"像产品主程序"排序，
                // 不能拿到第一个就用——UU加速器的组件 uu_ball.exe 的 FileDescription 也叫
                // "UU加速器"，先到的组件会把产品主程序顶掉（图标就是这么错的）。
                {
                    var cands = new List<ProcKey>();
                    foreach (ProcKey pk in procs)
                        foreach (string k in pk.Keys)
                            if (k.Length >= 4 && (key.Contains(k) || k.Contains(key)))
                            {
                                if (!cands.Contains(pk)) cands.Add(pk);
                                break;
                            }

                    if (cands.Count > 0)
                    {
                        cands.Sort(delegate (ProcKey a, ProcKey b)
                        {
                            int wa = RankForContains(a), wb = RankForContains(b);
                            if (wa != wb) return wa.CompareTo(wb);
                            return ShortestKey(a).Length.CompareTo(ShortestKey(b).Length);
                        });

                        foreach (ProcKey pk in cands)
                        {

                            ImageSource img = Use(pk.Path);
                            if (img != null) return img;
                            _lastIconSource = "";
                        }
                    }
                }
                // ⑤ 单词交集打分（"AMD Software: Adrenalin Edition" ↔ "AMD Software"）
                ProcKey best = null;
                int bestScore = 0;
                foreach (ProcKey pk in procs)
                {
                    foreach (string k in pk.Keys)
                    {
                        int s = Overlap(trayName, key, k);
                        if (s > bestScore) { bestScore = s; best = pk; }
                    }
                }
                if (best != null && bestScore >= 6)
                {
                    SettingsStore.Log("TrayBridge 命中[打分] " + trayName + " score=" + bestScore +
                        " => " + System.IO.Path.GetFileName(best.Path));
                    return Use(best.Path);
                }
            }
            catch (Exception ex)
            {
                SettingsStore.Log("TrayBridge.ResolveIcon: " + ex.Message);
            }
            return null;
        }

        private static List<ProcKey> _procIndex;

        private static int RankForContains(ProcKey pk)
        {
            string n = pk.ProcName ?? "";
            int penalty = 0;
            if (n.Contains("ball") || n.Contains("helper") || n.Contains("cloudsyn") ||
                n.Contains("service") || n.Contains("update") || n.Contains("crash"))
                penalty = 30;
            return penalty + Math.Min(n.Length, 20);
        }

        private static int Rank(ProcKey pk, string token)
        {
            // 只看**进程名**，不能看目录名——uu_ball.exe 和 uu.exe 都在 D:\Netease\UU\ 下，
            // 目录名都会变成 "uu"，只用"键等于 token"会把组件排到主程序前面（UU 图标就是这样错的）。
            if (pk.ProcName == token) return 0;                // 进程名就是产品名：最优先
            if (pk.ProcName.StartsWith(token, StringComparison.Ordinal)) return 5;

            int penalty = 0;
            string n = pk.ProcName;
            if (n.Contains("ball") || n.Contains("helper") || n.Contains("cloudsyn") ||
                n.Contains("service") || n.Contains("update") || n.Contains("crash"))
                penalty = 30;                                  // 组件
            return 10 + penalty;
        }

        private static string ShortestKey(ProcKey pk)
        {
            string best = null;
            foreach (string k in pk.Keys)
                if (best == null || k.Length < best.Length) best = k;
            return best ?? "";
        }

        private static string _lastIconSource = "";

        private static ImageSource Use(string path)
        {
            try
            {
                ImageSource img = IconService.Get(path);
                if (img != null) { _lastIconSource = System.IO.Path.GetFileName(path); return img; }

                // 兜底：Shell 大图标列表偶尔取不到（UU加速器这类提权进程的 exe 就遇到过），
                // 换 ExtractAssociatedIcon 再试一次。取不到就返回 null，外层继续试下一个候选。
                img = FromAssociatedIcon(path);
                if (img != null) { _lastIconSource = System.IO.Path.GetFileName(path) + "(assoc)"; return img; }
            }
            catch { }
            return null;
        }

        private static ImageSource FromAssociatedIcon(string path)
        {
            try
            {
                using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    if (ico == null) return null;
                    BitmapSource bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        ico.Handle, Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
                    bs.Freeze();
                    return bs;
                }
            }
            catch { return null; }
        }

        /// <summary>把托盘名和候选键都拆成"词"，返回共有的词数×2 + 最长公共子串长度。</summary>
        private static int Overlap(string rawName, string normName, string key)
        {
            int score = 0;
            // 拉丁词
            foreach (var tok in System.Text.RegularExpressions.Regex.Split(rawName ?? "", "[^A-Za-z0-9]+"))
            {
                if (tok.Length < 3) continue;
                if (key.Contains(tok.ToLowerInvariant())) score += 4;
            }
            // 中文双字
            for (int i = 0; i + 1 < (rawName ?? "").Length; i++)
            {
                char c0 = rawName[i], c1 = rawName[i + 1];
                if (c0 < 0x4E00 || c1 < 0x4E00) continue;
                string bi = new string(new[] { c0, c1 });
                if (key.Contains(bi)) score += 3;
            }
            // 共同的连续片段（≥4 字符）
            score += CommonRun(normName, key);
            return score;
        }

        private static int CommonRun(string a, string b)
        {
            if (a.Length < 4 || b.Length < 4) return 0;
            int best = 0;
            for (int i = 0; i < a.Length; i++)
            {
                for (int len = 4; i + len <= a.Length; len++)
                {
                    string sub = a.Substring(i, len);
                    if (b.Contains(sub)) best = Math.Max(best, len);
                    else break;
                }
            }
            return best >= 4 ? best : 0;
        }

        /// <summary>系统自带托盘条目的内置矢量图标（蓝牙 / 安全 / 音量 / 网络 / 电池 …）。</summary>
        private static ImageSource SystemGlyph(string lowerName)
        {
            string path = null;
            if (lowerName.Contains("蓝牙") || lowerName.Contains("bluetooth"))
                path = "M12 2 L18 7 L13 12 L18 17 L12 22 L12 13 L7 17 L6 15.6 L11.4 12 L6 8.4 L7 7 L12 11 Z";
            else if (lowerName.Contains("安全") || lowerName.Contains("security") || lowerName.Contains("defender"))
                path = "M12 2 L20 5 L20 11 C20 16 16.5 20.4 12 22 C7.5 20.4 4 16 4 11 L4 5 Z";
            else if (lowerName.Contains("音量") || lowerName.Contains("volume"))
                path = "M4 9 L8 9 L13 5 L13 19 L8 15 L4 15 Z M16 8 C17.6 9.4 17.6 14.6 16 16";
            else if (lowerName.Contains("网络") || lowerName.Contains("network") || lowerName.Contains("wifi"))
                path = "M2 9 C7.5 3.5 16.5 3.5 22 9 M5 12.5 C9 8.5 15 8.5 19 12.5 M8.5 16 C10.5 14 13.5 14 15.5 16";
            else if (lowerName.Contains("电池") || lowerName.Contains("battery"))
                path = "M3 8 L18 8 L18 16 L3 16 Z M19 10 L21 10 L21 14 L19 14 Z";
            if (path == null) return null;

            try
            {
                Geometry g = Geometry.Parse(path);
                System.Windows.Media.Brush brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8A, 0x8A, 0x94));
                var dg = new GeometryDrawing(null, new System.Windows.Media.Pen(brush, 1.4), g);
                var image = new DrawingImage(dg) { };
                image.Freeze();
                // 描边类图形：白色填充 + 描边；整体缩放到 24×24 由 Image.Stretch 完成
                return image;
            }
            catch { return null; }
        }
        private static BitmapSource CaptureIconUnused(System.Windows.Rect r)
        {
            try
            {
                // 按钮里图标是居中的 24×24，直接取中心 26×26 一点余量
                double size = Math.Min(r.Width, r.Height);
                int px = (int)Math.Round(size * 0.46);          // 60 → 28，正好覆盖居中的 24×24 图标
                if (px < 8) px = 8;
                int x = (int)Math.Round(r.X + (r.Width - px) / 2.0);
                int y = (int)Math.Round(r.Y + (r.Height - px) / 2.0);

                using (var bmp = new Bitmap(px, px, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(px, px), CopyPixelOperation.SourceCopy);
                    }
                    IntPtr hb = bmp.GetHbitmap();
                    try
                    {
                        BitmapSource bs = Imaging.CreateBitmapSourceFromHBitmap(hb, IntPtr.Zero,
                            Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        bs.Freeze();
                        return bs;
                    }
                    finally { DeleteObject(hb); }
                }
            }
            catch { return null; }
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr h);

        // ---------------- 点击某个托盘图标 ----------------
        /// <summary>
        /// 点开某个后台应用的托盘图标：重新打开溢出弹层 → 按名字+顺序找到它 → 真实鼠标点击。
        /// 返回 false 表示这一次没找到（比如图标刚被移除）。
        /// </summary>
        public static bool Invoke(TrayIconEntry target)
        {
            if (target == null) return false;
            try
            {
                // 任务栏上直接可见的图标：叫出任务栏后直接在它的 XAML 宿主里 Invoke（不打开溢出弹层）
                if (target.InTaskbar)
                {
                    ShellActions.FocusTray();
                    System.Threading.Thread.Sleep(320);
                    if (InvokeIn(FindWindow("Shell_TrayWnd", null), BridgeClass, target)) return true;
                    // 退一步：还是在溢出弹层里找一遍（图标可能刚好被折叠）
                }

                IntPtr ovf = EnsureOverflowOpen();
                if (ovf == IntPtr.Zero) return false;
                if (InvokeIn(ovf, null, target)) return true;

                CloseOverflow();
                return false;
            }
            catch (Exception ex)
            {
                SettingsStore.Log("TrayBridge.Invoke: " + ex.Message);
                CloseOverflow();
                return false;
            }
        }

        /// <summary>在指定容器里按名字找到按钮并 Invoke（失败再退回真实鼠标点击）。</summary>
        private static bool InvokeIn(IntPtr topHwnd, string childClass, TrayIconEntry target)
        {
            if (topHwnd == IntPtr.Zero) return false;

            IntPtr rootHwnd = topHwnd;
            if (childClass != null)
            {
                rootHwnd = FindWindowEx(topHwnd, IntPtr.Zero, childClass, null);
                if (rootHwnd == IntPtr.Zero) return false;
            }

            AutomationElement root = AutomationElement.FromHandle(rootHwnd);
            if (root == null) return false;

            AutomationElementCollection all;
            try { all = root.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition); }
            catch { return false; }

            AutomationElement hit = null;
            foreach (AutomationElement el in all)
            {
                AutomationElement.AutomationElementInformation info = el.Current;
                if (info.ControlType != ControlType.Button) continue;
                System.Windows.Rect r = info.BoundingRectangle;
                if (r.Width < 8 || r.Height < 8) continue;

                string name = CleanName(info.Name);
                if (name == null || name.Length == 0) continue;
                if (name == target.Name) { hit = el; break; }
            }
            if (hit == null) return false;

            try
            {
                object pat;
                if (hit.TryGetCurrentPattern(InvokePattern.Pattern, out pat) && pat != null)
                {
                    ((InvokePattern)pat).Invoke();
                    SettingsStore.Log("TrayBridge.Invoke(UIA) -> " + target.Name);
                    System.Threading.Thread.Sleep(180);
                    CloseOverflow();
                    return true;
                }
            }
            catch (Exception ex)
            {
                SettingsStore.Log("TrayBridge.Invoke(UIA 失败，改鼠标): " + ex.Message);
            }

            System.Windows.Rect br = hit.Current.BoundingRectangle;
            ClickAt((int)(br.X + br.Width / 2), (int)(br.Y + br.Height / 2));
            System.Threading.Thread.Sleep(180);
            CloseOverflow();
            return true;
        }
        private static void ClickAt(int x, int y)
        {
            SetCursorPos(x, y);
            System.Threading.Thread.Sleep(60);
            mouse_event(LEFTDOWN, 0, 0, 0, IntPtr.Zero);
            System.Threading.Thread.Sleep(40);
            mouse_event(LEFTUP, 0, 0, 0, IntPtr.Zero);
        }
    }
}
