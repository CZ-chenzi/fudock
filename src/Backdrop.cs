using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Fudock
{
    /// <summary>
    /// Windows 背景效果：背景模糊 / 亚克力 / 毛玻璃(Mica)，以及窗口圆角区域。
    /// 只对普通窗口（非分层窗口）有效——分层窗口的 alpha 通道会盖住 DWM 的背景材质。
    /// </summary>
    internal static class Backdrop
    {
        // ---- WCA (SetWindowCompositionAttribute) ----
        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;   // AABBGGRR
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int Left, Right, Top, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom,
            int widthEllipse, int heightEllipse);

        private const int WCA_ACCENT_POLICY = 19;
        private const int ACCENT_DISABLED = 0;
        private const int ACCENT_ENABLE_BLURBEHIND = 3;
        private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        private const int DWMWCP_ROUND = 2;
        private const int DWMSBT_NONE = 1;
        private const int DWMSBT_TRANSIENTWINDOW = 3;  // Acrylic（亚克力）

        public static bool IsWin11
        {
            get { return Environment.OSVersion.Version.Build >= 22000; }
        }

        /// <summary>窗口内容整体透明，让 DWM 材质透出来（普通窗口专用）。</summary>
        public static void EnableTransparentComposition(Window w)
        {
            try
            {
                IntPtr h = new WindowInteropHelper(w).Handle;
                if (h == IntPtr.Zero) return;
                HwndSource src = HwndSource.FromHwnd(h);
                if (src != null && src.CompositionTarget != null)
                    src.CompositionTarget.BackgroundColor = Colors.Transparent;
            }
            catch { }
        }

        /// <summary>玻璃框扩展满整个客户区（WPF 做亚克力的标准做法）。</summary>
        public static void ExtendGlass(Window w)
        {
            try
            {
                IntPtr h = new WindowInteropHelper(w).Handle;
                if (h == IntPtr.Zero) return;
                var m = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(h, ref m);
            }
            catch { }
        }

        public static void SetRoundedCorners(Window w, bool round)
        {
            try
            {
                if (!IsWin11) return;
                IntPtr h = new WindowInteropHelper(w).Handle;
                if (h == IntPtr.Zero) return;
                int v = round ? DWMWCP_ROUND : 1;
                DwmSetWindowAttribute(h, DWMWA_WINDOW_CORNER_PREFERENCE, ref v, sizeof(int));
            }
            catch { }
        }

        /// <summary>
        /// 设置窗口外形的圆角。
        /// 注意：DWM 的系统材质（亚克力）**不受窗口区域约束**——用 SetWindowRgn 裁形时，
        /// 圆角外的直角区仍然会被材质涂上，看起来就是"遗留的透明直角边"。
        /// 所以有材质时必须交给 DWM 自己的圆角；没有材质时才用区域精确裁剪（可以做到直角）。
        /// </summary>
        public static void SetWindowRadius(Window w, double dipRadius, bool useRegion)
        {
            try
            {
                IntPtr h = new WindowInteropHelper(w).Handle;
                if (h == IntPtr.Zero) return;

                if (!useRegion)
                {
                    // 有材质：清掉区域，改用 DWM 圆角（材质会被一起裁圆，无直角边）
                    SetWindowRgn(h, IntPtr.Zero, true);
                    if (IsWin11)
                    {
                        int pref = DWMWCP_ROUND;
                        DwmSetWindowAttribute(h, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
                    }
                    return;
                }

                // 无材质：关掉 DWM 圆角，自己裁
                if (IsWin11)
                {
                    int pref = 1;   // DWMWCP_DONOTROUND
                    DwmSetWindowAttribute(h, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
                }

                RECT rc;
                if (!GetWindowRect(h, out rc)) return;
                int ww = rc.Right - rc.Left;
                int wh = rc.Bottom - rc.Top;
                if (ww <= 0 || wh <= 0) return;

                double scale = 1.0;
                PresentationSource src = PresentationSource.FromVisual(w);
                if (src != null && src.CompositionTarget != null)
                    scale = src.CompositionTarget.TransformToDevice.M11;
                if (scale <= 0) scale = 1.0;

                int pr = (int)Math.Round(dipRadius * scale);
                int limit = Math.Min(ww, wh) / 2;
                if (pr > limit) pr = limit;

                if (pr <= 0)
                {
                    SetWindowRgn(h, IntPtr.Zero, true);   // 直角
                    return;
                }

                IntPtr rgn = CreateRoundRectRgn(0, 0, ww + 1, wh + 1, pr * 2, pr * 2);
                if (rgn == IntPtr.Zero) return;
                SetWindowRgn(h, rgn, true);               // 成功后系统接管该 region
            }
            catch { }
        }

        /// <summary>把效果应用到窗口（无 / 磨砂 / 玻璃；深浅色自动适配）。</summary>
        public static void Apply(Window w, BackdropKind kind, bool dark)
        {
            try
            {
                IntPtr h = new WindowInteropHelper(w).Handle;
                if (h == IntPtr.Zero) return;

                SetDarkMode(h, dark);

                if (kind == BackdropKind.Frosted)
                {
                    // 磨砂 = 系统亚克力（带噪点）；旧系统退回 WCA 亚克力
                    if (IsWin11)
                    {
                        DisableAccent(h);
                        int backdrop = DWMSBT_TRANSIENTWINDOW;
                        DwmSetWindowAttribute(h, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
                        return;
                    }
                    ApplyAccent(h, ACCENT_ENABLE_ACRYLICBLURBEHIND, dark);
                    return;
                }

                if (kind == BackdropKind.Glass)
                {
                    if (dark)
                    {
                        // 深色：保持原来的做法 —— WCA 背景模糊（那层压暗在深色下正好是想要的）
                        if (IsWin11)
                        {
                            int none = DWMSBT_NONE;
                            DwmSetWindowAttribute(h, DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
                        }
                        ApplyAccent(h, ACCENT_ENABLE_BLURBEHIND, true);
                        return;
                    }

                    // 浅色：WCA 模糊会把整体压灰，改用系统亚克力作底，
                    // 白色叠层、柔光、白内亮边都由 WPF 画在上面
                    if (IsWin11)
                    {
                        DisableAccent(h);
                        int lightBackdrop = DWMSBT_TRANSIENTWINDOW;
                        DwmSetWindowAttribute(h, DWMWA_SYSTEMBACKDROP_TYPE, ref lightBackdrop, sizeof(int));
                        return;
                    }
                    ApplyAccent(h, ACCENT_ENABLE_ACRYLICBLURBEHIND, false);
                    return;
                }

                // None
                DisableAccent(h);
                if (IsWin11)
                {
                    int none = DWMSBT_NONE;
                    DwmSetWindowAttribute(h, DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
                }
            }
            catch (Exception ex)
            {
                SettingsStore.Log("Backdrop.Apply: " + ex.Message);
            }
        }

        private static void ApplyAccentPolicy(IntPtr h, int state, int flags, int gradientColor)
        {
            try
            {
                var policy = new AccentPolicy
                {
                    AccentState = state,
                    AccentFlags = flags,
                    GradientColor = gradientColor
                };

                int size = Marshal.SizeOf(policy);
                IntPtr p = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(policy, p, false);
                    var data = new WindowCompositionAttributeData
                    {
                        Attribute = WCA_ACCENT_POLICY,
                        Data = p,
                        SizeOfData = size
                    };
                    SetWindowCompositionAttribute(h, ref data);
                }
                finally { Marshal.FreeHGlobal(p); }
            }
            catch { }
        }

        private static void SetDarkMode(IntPtr h, bool dark)
        {
            try
            {
                int v = dark ? 1 : 0;
                DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int));
            }
            catch { }
        }

        private static void ApplyAccent(IntPtr h, int state, bool dark)
        {
            ApplyAccent(h, state, dark, dark ? unchecked((int)0xB4202028) : unchecked((int)0xB4F4F4F8));
        }

        private static void ApplyAccent(IntPtr h, int state, bool dark, int gradientColor)
        {
            ApplyAccentPolicy(h, state, 2, gradientColor);
        }

        private static void DisableAccent(IntPtr h)
        {
            ApplyAccent(h, ACCENT_DISABLED, false);
        }
    }
}
