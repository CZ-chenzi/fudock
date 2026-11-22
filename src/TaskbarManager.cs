using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;

namespace Fudock
{
    /// <summary>隐藏 / 显示 Windows 原生任务栏（含多显示器的副任务栏）。</summary>
    internal static class TaskbarManager
    {
        private const string PrimaryClass = "Shell_TrayWnd";
        private const string SecondaryClass = "Shell_SecondaryTrayWnd";

        // 只有**我们自己**藏过才算 IsHidden。
        // 不能用 !IsWindowVisible 判断：Windows 自己的"自动隐藏"也会让任务栏一会儿可见一会儿不可见，
        // 拿它当菜单勾选状态会误导用户——点一下"恢复任务栏"反而真把任务栏藏了（出过这个事故）。
        private static bool _hiddenByUs;

        /// <summary>是否是 Dock 主动把任务栏藏起来的。</summary>
        public static bool IsHidden { get { return _hiddenByUs; } }

        /// <summary>任务栏窗口此刻在系统层面是否可见（诊断用，会被 Windows 自动隐藏影响）。</summary>
        public static bool WindowVisible
        {
            get
            {
                IntPtr h = Native.FindWindow(PrimaryClass, null);
                if (h == IntPtr.Zero) return false;
                return Native.IsWindowVisible(h);
            }
        }

        /// <summary>
        /// 重建任务栏外壳：重启 explorer.exe（会关掉已打开的文件夹窗口）。
        /// 用于 shell 状态卡死的情况，比如"托盘溢出弹层空着 / 点 ^ 没反应"。
        /// </summary>
        public static bool RepairShell()
        {
            try
            {
                SettingsStore.Log("Taskbar RepairShell: restarting explorer");
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer"))
                {
                    try { p.Kill(); } catch { }
                }
                System.Threading.Thread.Sleep(1500);
                if (System.Diagnostics.Process.GetProcessesByName("explorer").Length == 0)
                {
                    System.Diagnostics.Process.Start("explorer.exe");
                }
                _hiddenByUs = false;
                System.Threading.Thread.Sleep(1200);
                return true;
            }
            catch (Exception ex)
            {
                SettingsStore.Log("RepairShell: " + ex.Message);
                return false;
            }
        }

        /// <summary>系统里确实存在任务栏窗口。</summary>
        public static bool Exists
        {
            get { return Native.FindWindow(PrimaryClass, null) != IntPtr.Zero; }
        }

        /// <summary>藏任务栏期间留下的标记文件，用来识别"上次没干净退出"。</summary>
        private static string FlagPath
        {
            get { return System.IO.Path.Combine(SettingsStore.AppDir, "taskbar-hidden.flag"); }
        }

        /// <summary>启动自检：上次如果是藏着任务栏被强杀的，这里把它显示回来。</summary>
        public static bool RecoverIfLeftHidden()
        {
            try
            {
                if (!System.IO.File.Exists(FlagPath)) return false;
                SettingsStore.Log("检测到上次隐藏任务栏后异常退出，正在恢复任务栏");
                ForceShow();
                return true;
            }
            catch (Exception ex)
            {
                SettingsStore.Log("RecoverIfLeftHidden: " + ex.Message);
                return false;
            }
        }

        public static void SetHidden(bool hidden)
        {
            try
            {
                if (hidden) _autoHideWas = ReadAutoHide();      // 藏之前先记住用户原本的设置
                try
                {
                    if (hidden)
                    {
                        System.IO.Directory.CreateDirectory(SettingsStore.AppDir);
                        System.IO.File.WriteAllText(FlagPath, DateTime.Now.ToString("s"));
                    }
                    else if (System.IO.File.Exists(FlagPath)) System.IO.File.Delete(FlagPath);
                }
                catch { }
                foreach (IntPtr h in AllTaskbars())
                {
                    Native.ShowWindow(h, hidden ? Native.SW_HIDE : Native.SW_SHOW);
                }
                _hiddenByUs = hidden;
                if (!hidden) ReassertAutoHide(_autoHideWas);    // 恢复时把自动隐藏重新上膛
                SettingsStore.Log("Taskbar SetHidden(" + hidden + ") windows=" + AllTaskbars().Count);
            }
            catch (Exception ex)
            {
                SettingsStore.Log("SetHidden taskbar: " + ex.Message);
            }
        }

        // ---------------- 自动隐藏状态（SW_HIDE 会把 shell 的自动隐藏状态机卡住） ----------------
        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);

        private const uint ABM_SETSTATE = 0x0000000A;
        private const int ABS_AUTOHIDE = 0x0000001;
        private const int ABS_ALWAYSONTOP = 0x0000002;

        /// <summary>是否开过"我们藏任务栏"的动作（用来知道要不要恢复自动隐藏）。</summary>
        private static bool _autoHideWas = true;

        /// <summary>读注册表里 Windows 自己的"自动隐藏任务栏"开关。</summary>
        private static bool ReadAutoHide()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3"))
                {
                    if (key == null) return true;
                    var bytes = key.GetValue("Settings") as byte[];
                    if (bytes == null || bytes.Length < 9) return true;
                    return (bytes[8] & 0x01) != 0;
                }
            }
            catch { return true; }
        }

        /// <summary>
        /// 让 shell 重新"上膛"一次自动隐藏状态。
        /// **这是恢复任务栏的关键**：用 SW_HIDE 藏过任务栏之后，Explorer 的自动隐藏状态机会卡死，
        /// 表现就是"鼠标移到屏幕底部，任务栏再也不弹出来"。
        /// 用 SHAppBarMessage(ABM_SETSTATE) 重新下发一次状态，就能把这条链路救活。
        /// </summary>
        public static void ReassertAutoHide(bool autoHide)
        {
            try
            {
                var data = new APPBARDATA();
                data.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
                data.lParam = (IntPtr)(autoHide ? (ABS_AUTOHIDE | ABS_ALWAYSONTOP) : ABS_ALWAYSONTOP);
                SHAppBarMessage(ABM_SETSTATE, ref data);
                SettingsStore.Log("Taskbar ReassertAutoHide(" + autoHide + ")");
            }
            catch (Exception ex)
            {
                SettingsStore.Log("ReassertAutoHide: " + ex.Message);
            }
        }

        /// <summary>
        /// 无条件把任务栏显示回来（兜底 / 事故恢复用）。
        /// 不会去动 Windows 自己的自动隐藏设置，只是把窗口 SW_SHOW 出来并清掉"我们藏过"的标记。
        /// </summary>
        public static void ForceShow()
        {
            try
            {
                foreach (IntPtr h in AllTaskbars())
                {
                    Native.ShowWindow(h, Native.SW_SHOW);
                    Native.ShowWindow(h, Native.SW_SHOWNORMAL);
                }
                _hiddenByUs = false;
                try { if (System.IO.File.Exists(FlagPath)) System.IO.File.Delete(FlagPath); } catch { }
                // SW_HIDE 过的任务栏，光 SW_SHOW 还不够：自动隐藏状态机可能已卡死，
                // 这里重新下发一次 Windows 自己的自动隐藏状态，底部才会重新弹出。
                ReassertAutoHide(_autoHideWas);
                SettingsStore.Log("Taskbar ForceShow, visible=" + WindowVisible);
            }
            catch (Exception ex)
            {
                SettingsStore.Log("ForceShow taskbar: " + ex.Message);
            }
        }

        /// <summary>切换，返回"切换后是否已被我们隐藏"。</summary>
        public static bool Toggle()
        {
            bool hide = !_hiddenByUs;
            SetHidden(hide);
            return hide;
        }

        /// <summary>主任务栏 + 各显示器的副任务栏。</summary>
        private static List<IntPtr> AllTaskbars()
        {
            var list = new List<IntPtr>();

            IntPtr primary = Native.FindWindow(PrimaryClass, null);
            if (primary != IntPtr.Zero) list.Add(primary);

            Native.EnumWindowsProc cb = delegate (IntPtr hWnd, IntPtr lParam)
            {
                try
                {
                    var sb = new StringBuilder(128);
                    Native.GetClassName(hWnd, sb, sb.Capacity);
                    if (string.Equals(sb.ToString(), SecondaryClass, StringComparison.Ordinal))
                        list.Add(hWnd);
                }
                catch { }
                return true;
            };
            Native.EnumWindows(cb, IntPtr.Zero);

            return list;
        }
    }
}
