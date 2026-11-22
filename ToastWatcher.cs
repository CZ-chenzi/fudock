using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Fudock
{
    /// <summary>
    /// 通知"有没有新的"探测层（Level 2）。
    ///
    /// Windows **没有公开 API** 读通知中心里的未读数：WinRT 的
    /// ToastNotificationManager.History 只能看到本应用自己发的 toast，
    /// 系统的通知库是 %LOCALAPPDATA%\Microsoft\Windows\Notifications\wpndatabase.db（SQLite，且无权直读）。
    ///
    /// 所以这里用两个**只读的旁路信号**：
    ///  1. SetWinEventHook 盯 ShellExperienceHost 的两个窗口：
    ///     · 标题为 "New notification" 的 Windows.UI.Core.CoreWindow = 来了一条 toast → 未读 +1
    ///     · 通知中心本体出现 → 视为用户已经看过 → 未读清零
    ///  2. wpndatabase.db 的写入时间变了 → 兜底当作"有新通知"（第一路信号失灵时仍能亮角标）
    ///
    /// 拿不到就保持 0（等于 Level 1），不抛异常、不阻塞 UI。
    /// </summary>
    internal sealed class ToastWatcher : IDisposable
    {
        public int Unread { get; private set; }

        /// <summary>来了新通知（UI 线程上触发，可用于播放一次轻脉冲）。</summary>
        public event Action NewNotification;

        /// <summary>通知中心被打开（未读清零）。</summary>
        public event Action Cleared;

        private const uint EVENT_OBJECT_SHOW = 0x8002;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc,
            uint pid, uint tid, uint flags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hook);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int max);

        private readonly WinEventProc _proc;      // 必须存字段：委托被 GC 掉回调就断了
        private IntPtr _hook = IntPtr.Zero;
        private DateTime _dbStamp = DateTime.MinValue;
        private readonly string _dbPath;

        public ToastWatcher()
        {
            _dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Windows\Notifications\wpndatabase.db");

            try
            {
                _proc = OnWinEvent;
                _hook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _proc,
                    0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
                if (_hook == IntPtr.Zero) SettingsStore.Log("ToastWatcher: hook 安装失败（角标走数据库兜底）");
            }
            catch (Exception ex)
            {
                SettingsStore.Log("ToastWatcher: " + ex.Message);
                _hook = IntPtr.Zero;
            }
        }

        /// <summary>由 Dock 的轮询调用：用通知库的写入时间做兜底信号。</summary>
        public void PollFallback()
        {
            try
            {
                if (!File.Exists(_dbPath)) return;
                DateTime stamp = File.GetLastWriteTimeUtc(_dbPath);
                if (_dbStamp == DateTime.MinValue) { _dbStamp = stamp; return; }
                if (stamp > _dbStamp)
                {
                    _dbStamp = stamp;
                    Bump();
                }
            }
            catch { }
        }

        /// <summary>
        /// 调试用：直接摆一个未读数（设 DOCK_TOAST_DEBUG=1 时由 DockWindow 调）。
        /// 正式运行不会走到这里。
        /// </summary>
        public void DebugUnread(int n)
        {
            Unread = Math.Max(0, Math.Min(99, n));
            var h = NewNotification;
            if (h != null) h();
        }

        /// <summary>用户在 Dock 上点开了通知中心 → 立即清零，不等窗口事件。</summary>
        public void MarkRead()
        {
            if (Unread == 0) return;
            Unread = 0;
            var h = Cleared;
            if (h != null) h();
        }

        private void Bump()
        {
            if (Unread < 99) Unread++;
            var h = NewNotification;
            if (h != null) h();
        }

        private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return;

                var cls = new StringBuilder(64);
                GetClassName(hwnd, cls, cls.Capacity);
                if (cls.ToString() != "Windows.UI.Core.CoreWindow") return;   // 只管 ShellExperienceHost 的 XAML 岛

                var title = new StringBuilder(160);
                GetWindowTextW(hwnd, title, title.Capacity);
                string t = title.ToString();

                bool center = t.IndexOf("通知中心", StringComparison.OrdinalIgnoreCase) >= 0
                           || t.IndexOf("Notifications", StringComparison.OrdinalIgnoreCase) >= 0
                           || t.IndexOf("Action center", StringComparison.OrdinalIgnoreCase) >= 0;
                if (center)
                {
                    MarkRead();
                    return;
                }

                bool toast = t.IndexOf("New notification", StringComparison.OrdinalIgnoreCase) >= 0
                          || t.IndexOf("新通知", StringComparison.OrdinalIgnoreCase) >= 0;
                if (toast) Bump();
            }
            catch { }
        }

        public void Dispose()
        {
            try
            {
                if (_hook != IntPtr.Zero) { UnhookWinEvent(_hook); _hook = IntPtr.Zero; }
            }
            catch { }
        }
    }
}
