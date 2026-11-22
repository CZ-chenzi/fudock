using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Fudock
{
    /// <summary>
    /// 「把动作交给 Windows 自己」的那一层——本项目的原则是：
    /// Dock 只做入口，不重造系统面板（通知中心 / 快速设置都是原生 UI）。
    ///
    /// 需要的权限：全部是普通用户态调用，不需要管理员、不需要 UIAccess。
    /// 唯一敏感点是 SendInput 模拟 Win 组合键，属于常规输入注入，
    /// 只在用户真的点了 Dock 上对应的按钮时才会发生。
    /// </summary>
    internal static class ShellActions
    {
        // ---------------- 输入注入 ----------------
        // 注意：INPUT 是 union 结构，x64 下真实大小是 40 字节（type 占 4 字节 + 4 字节对齐 +
        // MOUSEINPUT 的 32 字节）。cbSize 不对 SendInput 会直接返回 0（E_INVALIDARG），
        // 所以这里显式按 x64 布局补齐，并在 32 位进程里直接放弃注入（走面板兜底）。
        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public uint pad;          // 对齐到 8 字节
            public KEYBDINPUT ki;     // 偏移 8
            public uint tail0;        // 补齐成 MOUSEINPUT 的 32 字节
            public uint tail1;
        }

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const ushort VK_LWIN = 0x5B;
        private const ushort VK_A = 0x41;
        private const ushort VK_N = 0x4E;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        /// <summary>模拟 Win + 字母。Win11 里 Win+A = 快速设置，Win+N = 通知中心。</summary>
        private static bool SendWinKey(ushort vk)
        {
            if (IntPtr.Size != 8) return false;      // 只保证 x64 的 INPUT 布局
            try
            {
                var inputs = new INPUT[4];
                inputs[0].type = INPUT_KEYBOARD; inputs[0].ki.wVk = VK_LWIN;
                inputs[1].type = INPUT_KEYBOARD; inputs[1].ki.wVk = vk;
                inputs[2].type = INPUT_KEYBOARD; inputs[2].ki.wVk = vk;
                inputs[2].ki.dwFlags = KEYEVENTF_KEYUP;
                inputs[3].type = INPUT_KEYBOARD; inputs[3].ki.wVk = VK_LWIN;
                inputs[3].ki.dwFlags = KEYEVENTF_KEYUP;

                int size = Marshal.SizeOf(typeof(INPUT));
                uint sent = SendInput(4, inputs, size);
                if (sent != 4)
                {
                    SettingsStore.Log("SendInput failed, err=" + Marshal.GetLastWin32Error() + " cbSize=" + size);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                SettingsStore.Log("SendWinKey: " + ex.Message);
                return false;
            }
        }

        /// <summary>Windows 11 快速设置（网络 / 音量 / 蓝牙 …）。失败返回 false，调用方转降级。</summary>
        public static bool QuickSettings()
        {
            return SendWinKey(VK_A);
        }

        /// <summary>Windows 11 通知中心（Win+N，22H2 起）。</summary>
        public static bool NotificationCenter()
        {
            return SendWinKey(VK_N);
        }

        // ---------------- 打开系统设置页 ----------------
        /// <summary>用 shell 打开 ms-settings: 协议或普通 URI，失败不抛。</summary>
        public static bool Open(string target)
        {
            try
            {
                var psi = new ProcessStartInfo(target) { UseShellExecute = true };
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                SettingsStore.Log("ShellActions.Open(" + target + "): " + ex.Message);
                return false;
            }
        }

        public static bool NotificationSettings() { return Open("ms-settings:notifications"); }
        public static bool NetworkSettings() { return Open("ms-settings:network"); }
        public static bool SoundSettings() { return Open("ms-settings:sound"); }
        public static bool PowerSettings() { return Open("ms-settings:batterysaver"); }
        public static bool TaskbarSettings() { return Open("ms-settings:taskbar"); }

        private const ushort VK_B = 0x42;
        private const ushort VK_RETURN = 0x0D;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string cls, string title);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr h);

        /// <summary>Win11 的托盘溢出窗口（XAML 岛）。用它判断"托盘到底有没有弹出来"。</summary>
        private static bool OverflowVisible()
        {
            try
            {
                IntPtr h = FindWindow("TopLevelWindowForOverflowXamlIsland", null);
                return h != IntPtr.Zero && IsWindowVisible(h);
            }
            catch { return false; }
        }

        /// <summary>只聚焦托盘（Win+B），不展开溢出——用来把自动隐藏的任务栏叫出来读图标。</summary>
        public static bool FocusTray()
        {
            if (IntPtr.Size != 8) return false;
            return SendWinKey(VK_B);
        }

        /// <summary>敲一下某个键（Esc 用来关系统弹层）。</summary>
        public static void TapKeyPublic(ushort vk) { TapKey(vk); }

        private static void TapKey(ushort vk)
        {
            var down = new INPUT[1];
            down[0].type = INPUT_KEYBOARD;
            down[0].ki.wVk = vk;
            var up = new INPUT[1];
            up[0].type = INPUT_KEYBOARD;
            up[0].ki.wVk = vk;
            up[0].ki.dwFlags = KEYEVENTF_KEYUP;
            int size = Marshal.SizeOf(typeof(INPUT));
            SendInput(1, down, size);
            SendInput(1, up, size);
        }

        /// <summary>
        /// 展开 Windows 自己的托盘溢出（任务栏上那个 ^）。
        ///
        /// 实测（Win11 23H2）：
        ///  · 溢出界面是 XAML 岛窗口 `TopLevelWindowForOverflowXamlIsland`，
        ///    `TrayNotifyWnd` 底下**没有**可点的 "Button"，所以老办法点不到；
        ///  · 可靠的路线是键盘：`Win + B`（把焦点移到托盘）→ `Enter`（展开隐藏图标），
        ///    实测能让该窗口从不可见变可见；
        ///  · 全失败才退到任务栏设置页，绝不崩、不假装成功。
        /// </summary>
        public static bool OpenSystemTrayOverflow()
        {
            try
            {
                SettingsStore.Log("Tray: start, visible=" + OverflowVisible());
                if (OverflowVisible()) return true;          // 本来就是展开的

                // 1) Win10 经典：TrayNotifyWnd 里的 "Button" 就是 chevron
                IntPtr tray = FindWindow("Shell_TrayWnd", null);
                if (tray != IntPtr.Zero)
                {
                    IntPtr notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
                    if (notify != IntPtr.Zero)
                    {
                        IntPtr btn = FindWindowEx(notify, IntPtr.Zero, "Button", null);
                        if (btn != IntPtr.Zero)
                        {
                            SendMessage(btn, 0x00F5 /* BM_CLICK */, IntPtr.Zero, IntPtr.Zero);
                            System.Threading.Thread.Sleep(180);
                            SettingsStore.Log("Tray: BM_CLICK 后 visible=" + OverflowVisible());
                            if (OverflowVisible()) return true;
                        }
                    }
                }

                // 2) Win11：Win + B → Enter
                bool sent = IntPtr.Size == 8 && SendWinKey(VK_B);
                SettingsStore.Log("Tray: Win+B sent=" + sent);
                if (sent)
                {
                    System.Threading.Thread.Sleep(360);      // 等焦点落到托盘
                    TapKey(VK_RETURN);
                    System.Threading.Thread.Sleep(320);
                    SettingsStore.Log("Tray: Win+B+Enter 后 visible=" + OverflowVisible());
                    if (OverflowVisible()) return true;
                }

                // 3) 兜底：任务栏设置页
                SettingsStore.Log("Tray: 全部失败 → 退到任务栏设置页");
                return TaskbarSettings();
            }
            catch (Exception ex)
            {
                SettingsStore.Log("OpenSystemTrayOverflow: " + ex.Message);
                return TaskbarSettings();
            }
        }

    }
}

