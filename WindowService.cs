using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Fudock
{
    /// <summary>枚举顶层窗口、按可执行文件聚合、激活/最小化/关闭窗口。</summary>
    internal static class WindowService
    {
        private static readonly Dictionary<int, string> PathCache = new Dictionary<int, string>();
        private static int _selfPid = Process.GetCurrentProcess().Id;

        public static void ClearCache() { PathCache.Clear(); }

        /// <summary>抓取当前所有"用户会觉得是应用窗口"的顶层窗口。</summary>
        public static List<AppWindow> Snapshot()
        {
            var result = new List<AppWindow>();
            var livePids = new HashSet<int>();

            RefreshProcessSnapshot();

            Native.EnumWindowsProc cb = delegate (IntPtr hWnd, IntPtr lParam)
            {
                try
                {
                    if (!Native.IsWindowVisible(hWnd)) return true;
                    if (Native.IsCloaked(hWnd)) return true;
                    if (Native.GetWindowTextLength(hWnd) == 0) return true;

                    long ex = Native.GetWindowLongPtr(hWnd, Native.GWL_EXSTYLE).ToInt64();
                    if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return true;

                    IntPtr owner = Native.GetWindow(hWnd, Native.GW_OWNER);
                    if (owner != IntPtr.Zero && (ex & Native.WS_EX_APPWINDOW) == 0) return true;

                    var cls = new StringBuilder(256);
                    Native.GetClassName(hWnd, cls, cls.Capacity);
                    string cn = cls.ToString();
                    if (cn == "Progman" || cn == "WorkerW" || cn == "Shell_TrayWnd" ||
                        cn == "Shell_SecondaryTrayWnd" || cn == "Windows.UI.Core.CoreWindow" ||
                        cn == "ApplicationFrameWindow_DockIgnore") return true;

                    int pid;
                    Native.GetWindowThreadProcessId(hWnd, out pid);
                    if (pid <= 0 || pid == _selfPid) return true;

                    livePids.Add(pid);
                    string exe = GetExePath(pid);
                    if (string.IsNullOrEmpty(exe)) return true;
                    if (string.Equals(exe, SelfExe, StringComparison.OrdinalIgnoreCase)) return true;

                    var sb = new StringBuilder(512);
                    Native.GetWindowText(hWnd, sb, sb.Capacity);

                    result.Add(new AppWindow
                    {
                        Handle = hWnd,
                        ProcessId = pid,
                        Title = sb.ToString(),
                        ExePath = exe
                    });
                }
                catch { /* 单个窗口失败不影响整体 */ }
                return true;
            };

            Native.EnumWindows(cb, IntPtr.Zero);

            // 清理已退出进程的缓存
            if (PathCache.Count > livePids.Count + 32)
            {
                var toRemove = new List<int>();
                foreach (var kv in PathCache) if (!livePids.Contains(kv.Key)) toRemove.Add(kv.Key);
                foreach (var k in toRemove) PathCache.Remove(k);
            }

            return result;
        }

        private static string SelfExe
        {
            get
            {
                try { return Process.GetCurrentProcess().MainModule.FileName; }
                catch { return ""; }
            }
        }

        public static string GetExePath(int pid)
        {
            string cached;
            if (PathCache.TryGetValue(pid, out cached)) return cached;

            string path = Native.GetProcessPath(pid);
            if (string.IsNullOrEmpty(path))
            {
                try
                {
                    using (var p = Process.GetProcessById(pid))
                        path = p.MainModule.FileName;
                }
                catch { path = null; }
            }
            if (path == null) return null;

            PathCache[pid] = path;
            return path;
        }

        /// <summary>把窗口列表按 exe 路径分组。</summary>
        public static List<RunningApp> Group(List<AppWindow> windows)
        {
            var order = new List<string>();
            var map = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);

            foreach (var w in windows)
            {
                string key = w.ExePath ?? "";
                if (key.Length == 0) continue;
                RunningApp app;
                if (!map.TryGetValue(key, out app))
                {
                    app = new RunningApp
                    {
                        ExePath = key,
                        ProcessName = SafeProcessName(key)
                    };
                    map[key] = app;
                    order.Add(key);
                }
                app.Windows.Add(w);
            }

            var list = new List<RunningApp>();
            foreach (var k in order) list.Add(map[k]);

            // 多进程应用（如 Steam 主进程 + steamwebhelper）：记录祖先 exe，供 Dock 归属匹配
            foreach (var app in list)
            {
                var chain = new List<string>();
                foreach (var w in app.Windows)
                {
                    foreach (string a in GetAncestorExes(w.ProcessId))
                        if (!chain.Contains(a)) chain.Add(a);
                }
                app.Chain = chain;
            }
            return list;
        }

        // ---- 父进程链 ----
        private static Dictionary<int, int> _parentOf = new Dictionary<int, int>();

        /// <summary>这些进程只是"启动器/宿主"，往上走没有归属意义，遇到就停。</summary>
        private static readonly HashSet<string> ChainStops = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "bash.exe", "wt.exe",
            "services.exe", "svchost.exe", "wininit.exe", "winlogon.exe", "userinit.exe",
            "taskmgr.exe", "conhost.exe", "rundll32.exe", "dllhost.exe", "startmenuexperiencehost.exe"
        };

        private static void RefreshProcessSnapshot()
        {
            var map = new Dictionary<int, int>();
            IntPtr snap = Native.CreateToolhelp32Snapshot(Native.TH32CS_SNAPPROCESS, 0);
            if (snap == Native.INVALID_HANDLE_VALUE || snap == IntPtr.Zero) return;

            try
            {
                var pe = new Native.PROCESSENTRY32();
                pe.dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.PROCESSENTRY32));
                if (Native.Process32First(snap, ref pe))
                {
                    do
                    {
                        int pid = (int)pe.th32ProcessID;
                        int ppid = (int)pe.th32ParentProcessID;
                        if (pid > 0) map[pid] = ppid;
                    }
                    while (Native.Process32Next(snap, ref pe));
                }
            }
            catch { }
            finally { Native.CloseHandle(snap); }

            _parentOf = map;
        }

        /// <summary>沿父进程链向上收集可执行文件路径（遇到启动器/宿主即停止）。</summary>
        public static List<string> GetAncestorExes(int pid)
        {
            var list = new List<string>();
            int cur = pid;

            for (int depth = 0; depth < 4; depth++)
            {
                int parent;
                if (!_parentOf.TryGetValue(cur, out parent)) break;
                if (parent <= 0 || parent == cur) break;

                string exe = GetExePath(parent);
                if (string.IsNullOrEmpty(exe)) break;

                string low = exe.ToLowerInvariant();
                string name;
                try { name = System.IO.Path.GetFileName(exe); }
                catch { name = low; }

                // 父进程是资源管理器 / 终端这类宿主，说明已经走到"谁启动的它"了，停止
                if (ChainStops.Contains(name)) break;

                if (!list.Contains(low)) list.Add(low);
                cur = parent;
            }

            return list;
        }

        private static string SafeProcessName(string exePath)
        {
            try { return System.IO.Path.GetFileNameWithoutExtension(exePath); }
            catch { return exePath; }
        }

        /// <summary>激活窗口：还原 + 提到前台（绕过前台锁）。</summary>
        public static void Activate(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !Native.IsWindow(hWnd)) return;

            if (Native.IsIconic(hWnd))
                Native.ShowWindow(hWnd, Native.SW_RESTORE);

            IntPtr fg = Native.GetForegroundWindow();
            if (fg == hWnd) return;

            uint fgThread = 0;
            int fgPid;
            fgThread = (uint)Native.GetWindowThreadProcessId(fg, out fgPid);
            int dummy;
            uint targetThread = (uint)Native.GetWindowThreadProcessId(hWnd, out dummy);
            uint self = Native.GetCurrentThreadId();

            bool attachedFg = false, attachedSelf = false;
            try
            {
                if (fgThread != 0 && fgThread != targetThread)
                    attachedFg = Native.AttachThreadInput(fgThread, targetThread, true);
                if (self != targetThread)
                    attachedSelf = Native.AttachThreadInput(self, targetThread, true);

                Native.SetForegroundWindow(hWnd);
                Native.BringWindowToTop(hWnd);
                Native.SetFocus(hWnd);
            }
            finally
            {
                if (attachedFg) Native.AttachThreadInput(fgThread, targetThread, false);
                if (attachedSelf) Native.AttachThreadInput(self, targetThread, false);
            }

            if (Native.GetForegroundWindow() != hWnd)
            {
                // 兜底：老 API，依然有效
                Native.SwitchToThisWindow(hWnd, true);
            }
        }

        public static void Minimize(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !Native.IsWindow(hWnd)) return;
            Native.ShowWindow(hWnd, Native.SW_MINIMIZE);
        }

        public static bool IsForeground(IntPtr hWnd)
        {
            return Native.GetForegroundWindow() == hWnd;
        }

        /// <summary>温和关闭窗口（发 WM_CLOSE，超时防挂起）。</summary>
        public static void Close(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !Native.IsWindow(hWnd)) return;
            IntPtr r;
            Native.SendMessageTimeout(hWnd, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero,
                0x0002 /*SMTO_ABORTIFHUNG*/, 2000, out r);
        }
    }
}
