using System;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Fudock
{
    public enum NetKind { Off, Wired, Wifi }
    public enum PowerKind { Unknown, Ac, Battery, Charging }

    /// <summary>
    /// 状态胶囊背后的数据源：网络 / 音量 / 电池 / 时间。
    ///
    /// 全部是**只读的轻量系统查询**，只用普通用户权限，不需要任何提权：
    ///  · 网络：System.Net.NetworkInformation 枚举网卡（自带，无需 API）
    ///  · 音量：Core Audio API（COM: IMMDeviceEnumerator + IAudioEndpointVolume）
    ///  · 电池：kernel32!GetSystemPowerStatus
    ///  · 全屏：shell32!SHQueryUserNotificationState（系统自己判断全屏游戏/视频）
    /// 任何一项失败都只是让胶囊少一格，不影响 Dock 本体。
    /// </summary>
    public sealed class SystemStatus
    {
        public NetKind Net { get; private set; }
        public string NetName { get; private set; }
        public bool HasVolume { get; private set; }
        public int Volume { get; private set; }          // 0..100
        public bool Muted { get; private set; }
        public PowerKind Power { get; private set; }
        public int BatteryPercent { get; private set; }  // 0..100，无电池时为 -1
        public DateTime Now { get; private set; }

        private int _tickCount;
        private bool _volumeBroken;      // COM 挂了就别每次重试

        public SystemStatus()
        {
            Net = NetKind.Off;
            NetName = "";
            Power = PowerKind.Unknown;
            BatteryPercent = -1;
            Now = DateTime.Now;
        }

        /// <summary>由 Dock 的轮询定时器调用（约 1 秒一次）。</summary>
        public void Tick()
        {
            Now = DateTime.Now;
            _tickCount++;

            // 网络 / 电池很便宜，每秒都刷；音量走 COM，2 秒一次够了
            RefreshNetwork();
            RefreshPower();
            if (!_volumeBroken && (_tickCount == 1 || _tickCount % 2 == 0)) RefreshVolume();
        }

        // ---------------- 网络 ----------------
        private void RefreshNetwork()
        {
            try
            {
                bool anyUp = false;
                bool wifi = false;
                string name = "";

                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    anyUp = true;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    {
                        wifi = true;
                        if (name.Length == 0) name = ni.Name;
                    }
                    else if (name.Length == 0)
                    {
                        name = ni.Name;
                    }
                }

                Net = anyUp ? (wifi ? NetKind.Wifi : NetKind.Wired) : NetKind.Off;
                NetName = name;
            }
            catch
            {
                Net = NetKind.Off;
                NetName = "";
            }
        }

        // ---------------- 电池 / 电源 ----------------
        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS st);

        private void RefreshPower()
        {
            try
            {
                SYSTEM_POWER_STATUS st;
                if (!GetSystemPowerStatus(out st))
                {
                    Power = PowerKind.Unknown;
                    BatteryPercent = -1;
                    return;
                }

                bool ac = st.ACLineStatus == 1;
                bool hasBattery = (st.BatteryFlag & 0x80) == 0 && st.BatteryFlag != 0xFF;
                int pct = st.BatteryLifePercent <= 100 ? st.BatteryLifePercent : -1;

                if (!hasBattery)
                {
                    Power = PowerKind.Ac;
                    BatteryPercent = -1;
                }
                else if (ac && st.BatteryFlag != 0x08)
                {
                    Power = PowerKind.Charging;
                    BatteryPercent = pct;
                }
                else if (ac)
                {
                    Power = PowerKind.Ac;
                    BatteryPercent = pct;
                }
                else
                {
                    Power = PowerKind.Battery;
                    BatteryPercent = pct;
                }
            }
            catch
            {
                Power = PowerKind.Unknown;
                BatteryPercent = -1;
            }
        }

        // ---------------- 音量（Core Audio COM） ----------------
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int flow, int state, out IntPtr devices);
            int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
            int GetDevice(string id, out IMMDevice device);
            int RegisterEndpointNotificationCallback(IntPtr cb);
            int UnregisterEndpointNotificationCallback(IntPtr cb);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            int OpenPropertyStore(int access, out IntPtr store);
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            int GetState(out int state);
        }

        // 方法顺序必须和 vtable 完全一致：只要 GetMasterVolumeLevelScalar / GetMute 的**序号**对，
        // 中间的参数类型用 IntPtr 占位也是 ABI 安全的。
        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr cb);
            int UnregisterControlChangeNotify(IntPtr cb);
            int GetChannelCount(out uint count);
            int SetMasterVolumeLevel(float level, ref Guid ctx);
            int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            int GetMasterVolumeLevel(out float level);
            int GetMasterVolumeLevelScalar(out float level);
            int SetChannelVolumeLevel(uint ch, float level, ref Guid ctx);
            int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
            int GetChannelVolumeLevel(uint ch, out float level);
            int GetChannelVolumeLevelScalar(uint ch, out float level);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        private void RefreshVolume()
        {
            object iface = null;
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                IMMDevice dev;
                if (enumerator.GetDefaultAudioEndpoint(0 /*eRender*/, 1 /*eMultimedia*/, out dev) != 0 || dev == null)
                {
                    HasVolume = false;
                    return;
                }

                Guid iid = typeof(IAudioEndpointVolume).GUID;
                if (dev.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out iface) != 0 || iface == null)
                {
                    HasVolume = false;
                    return;
                }

                var vol = (IAudioEndpointVolume)iface;
                Guid ctx = Guid.Empty;
                float scalar;
                if (vol.GetMasterVolumeLevelScalar(out scalar) != 0)
                {
                    HasVolume = false;
                    return;
                }
                bool mute;
                vol.GetMute(out mute);

                HasVolume = true;
                Volume = (int)Math.Round(scalar * 100);
                Muted = mute;
            }
            catch
            {
                // 没装音频设备 / COM 不可用：关掉音量格，永不重试，绝不抛到 UI
                _volumeBroken = true;
                HasVolume = false;
            }
            finally
            {
                if (iface != null && Marshal.IsComObject(iface))
                {
                    try { Marshal.ReleaseComObject(iface); } catch { }
                }
            }
        }

        // ---------------- 全屏（游戏 / 视频 / 演示） ----------------
        private enum QUNS
        {
            NOT_PRESENT = 1,                  // 用户不在（锁屏 / 切换用户）
            BUSY = 2,                         // 全屏应用：演示、全屏视频
            RUNNING_D3D_FULL_SCREEN = 3,      // 独占全屏游戏
            PRESENTATION_MODE = 4,            // 演示模式
            ACCEPTS_NOTIFICATIONS = 5,        // 正常
            QUIET_TIME = 6,                   // 专注助手
            APP = 7                           // 全屏 UWP/Win32（Win11）
        }

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out QUNS state);

        /// <summary>最近一次查询到的原始状态（日志 / 诊断用）。</summary>
        public static int LastFullscreenRaw = -1;
        public static string LastProbe = "";

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int max);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr h, System.Text.StringBuilder sb, int max);

        private static string WindowTitle(IntPtr h)
        {
            var sb = new System.Text.StringBuilder(128);
            GetWindowTextW(h, sb, sb.Capacity);
            return sb.ToString();
        }

        // 前台窗口 / 显示器几何（判断"真的全屏"用，比 shell 的启发式靠得住）
        [StructLayout(LayoutKind.Sequential)]
        private struct R2 { public int L, T, R, B; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public R2 rcMonitor;
            public R2 rcWork;
            public int dwFlags;
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out R2 r);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, int flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);

        /// <summary>
        /// 现在是不是"全屏游戏 / 全屏视频 / 演示"？
        ///
        /// 判定方式改了两次，记录一下原因：
        /// v1 直接信 `SHQueryUserNotificationState`（把 QUNS_APP 也算全屏）→ 实测普通桌面下
        ///    偶尔返回 2/3/7，系统区被误藏（"它不显示"）；
        /// v2 只认 2/3/4 → 仍然偶发误判。
        /// 现在改成**以几何为准**：前台窗口是否完整覆盖它所在显示器的 rect。
        /// 最大化窗口会留下任务栏高度 → 不算；F11/独占全屏/全屏播放器 → 算。
        /// shell 的状态只写进 LastFullscreenRaw 供日志参考，不再参与判定（宁可不藏，不误藏）。
        /// </summary>
        public static bool FullscreenBusy()
        {
            try
            {
                // 记录 shell 的看法，便于排查（不参与决策）
                QUNS st;
                LastFullscreenRaw = SHQueryUserNotificationState(out st) == 0 ? (int)st : -1;

                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                if (fg == GetDesktopWindow() || fg == GetShellWindow()) return false;
                if (fg == GetTaskbarWindow()) return false;      // 点任务栏不该算全屏

                R2 wr;
                if (!GetWindowRect(fg, out wr)) return false;

                IntPtr mon = MonitorFromWindow(fg, 2 /*MONITOR_DEFAULTTONEAREST*/);
                var mi = new MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (!GetMonitorInfo(mon, ref mi)) return false;

                // 诊断：把前台窗口身份和几何写进日志（排查"误判全屏"）
                var cls = new System.Text.StringBuilder(64);
                GetClassName(fg, cls, cls.Capacity);
                LastProbe = string.Format("{0} [{1}] {2},{3}-{4},{5} vs mon {6},{7}-{8},{9}",
                    cls, WindowTitle(fg), wr.L, wr.T, wr.R, wr.B,
                    mi.rcMonitor.L, mi.rcMonitor.T, mi.rcMonitor.R, mi.rcMonitor.B);

                // 允许 2px 误差（有些窗口边框会把 rect 撑大一点）
                bool covers = wr.L <= mi.rcMonitor.L + 2
                    && wr.T <= mi.rcMonitor.T + 2
                    && wr.R >= mi.rcMonitor.R - 2
                    && wr.B >= mi.rcMonitor.B - 2;

                // 还要 shell 也认为"不该打扰"（BUSY / D3D 全屏 / 演示）。
                // 只用几何会被"任务栏自动隐藏时的最大化窗口"骗到（实测 Edge 最大化后
                // rect 是 -11,-11-2571,1611，把 2560x1600 的屏幕整个盖住），
                // 只用 shell 状态又会被偶发的 BUSY 骗到 —— 两个都满足才算，宁可不藏。
                bool shellSaysBusy = st == QUNS.BUSY
                    || st == QUNS.RUNNING_D3D_FULL_SCREEN
                    || st == QUNS.PRESENTATION_MODE;
                return covers && shellSaysBusy;
            }
            catch
            {
                LastFullscreenRaw = -1;
                return false;
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string cls, string title);

        private static IntPtr GetTaskbarWindow()
        {
            try { return FindWindow("Shell_TrayWnd", null); }
            catch { return IntPtr.Zero; }
        }

        // ---------------- 给 UI 用的文案 ----------------
        public string TimeText()
        {
            return Now.ToString("HH:mm");
        }

        public string NetworkText()
        {
            switch (Net)
            {
                case NetKind.Wifi: return NetName.Length > 0 ? L.T("Wi-Fi：{0}", NetName) : "Wi-Fi";
                case NetKind.Wired: return NetName.Length > 0 ? L.T("有线网络：{0}", NetName) : L.T("有线网络");
                default: return L.T("无网络连接");
            }
        }

        public string VolumeText()
        {
            if (!HasVolume) return L.T("音量不可用");
            return Muted ? L.T("已静音") : L.T("音量 {0}%", Volume);
        }

        public string PowerText()
        {
            switch (Power)
            {
                case PowerKind.Ac: return L.T("已接通电源");
                case PowerKind.Charging: return BatteryPercent >= 0 ? L.T("充电中 {0}%", BatteryPercent) : L.T("充电中");
                case PowerKind.Battery: return BatteryPercent >= 0 ? L.T("电池 {0}%", BatteryPercent) : L.T("使用电池");
                default: return L.T("电源状态未知");
            }
        }
    }
}
