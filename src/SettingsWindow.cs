using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Fudock
{
    /// <summary>设置面板：Fluent 卡片风格，自绘控件，深浅色/透明度/圆角可独立调整。</summary>
    public class SettingsWindow : Window
    {
        private readonly DockWindow _dock;
        private Palette _pal;
        private Border _preview;
        private Grid _shell;                                     // 外层容器
        private Border _shellFill;                               // 铺满矩形的底色
        private Border _shellStroke;                             // 沿区域边缘的圆角描边
        private readonly List<Border> _cards = new List<Border>();   // 需要跟着重绘的卡片
        private bool _sizeLocked;                     // 高度是否已从"贴合内容"切成固定值（切成后才能拖动改大小）
        private DispatcherTimer _sizeSaveTimer;       // 拖动结束时再落盘，别每一帧都写文件

        private DockSettings S { get { return _dock.Settings; } }

        public SettingsWindow(DockWindow dock)
        {
            _dock = dock;
            _pal = Palette.CreateUi(dock.Settings);

            Title = AppInfo.Name + L.T(" 设置");
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;            // 真·系统材质要求非分层窗口
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanResize;     // 允许拖边框/角调整大小
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
            FontFamily = Sx.UiFont;

            // 吃 DWM 材质：去掉标题栏/边框，客户区整体当作“玻璃框”；
            // ResizeBorderThickness 给出一圈 6px 的可拖拽边框（WindowChrome 负责命中与光标）。
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                // 设成 0：让整个客户区都归 WPF，边缘拖拽由下面的手写逻辑接管。
                // 如果交给 WindowChrome（>0），那圈会变成非客户区，WPF 收不到 PreviewMouseDown，
                // 我们自己实现的拖拽就永远不触发。
                ResizeBorderThickness = new Thickness(0),
                // 不用 -1：GlassFrameThickness=-1 会把整个窗口当"玻璃框"，
                // WindowChrome 于是自己接管 WM_NCHITTEST 并一律返回 HTCLIENT，
                // 边框缩放就永远命中不到。玻璃区域仍由 Backdrop.ExtendGlass() 的
                // DwmExtendFrameIntoClientArea(-1) 提供，材质不受影响。
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });

            double availH = SystemParameters.WorkArea.Height;
            double availW = SystemParameters.WorkArea.Width;

            // 尺寸：记住上次拖出来的大小；没拖过就 800 宽 + 高度贴合内容
            MinWidth = 780;      // 两列卡片布局的实测下限，再窄右列会被裁
            MinHeight = 460;
            MaxWidth = Math.Max(MinWidth + 40, availW - 40);
            MaxHeight = Math.Max(MinHeight, availH - 40);
            Width = S.UiWidth >= MinWidth ? Math.Min(S.UiWidth, MaxWidth) : Math.Max(MinWidth, 800);

            if (S.UiHeight >= MinHeight)
            {
                Height = Math.Min(S.UiHeight, MaxHeight);
                SizeToContent = SizeToContent.Manual;
                _sizeLocked = true;
            }
            else
            {
                SizeToContent = SizeToContent.Height;   // 高度贴合内容，小屏时再滚动
            }

            BuildUi();

            SourceInitialized += OnSourceInitialized;

            // 首次排版完成后把高度锁死：这样用户拖边框时不会被"贴合内容"顶回去
            ContentRendered += OnSizedOnce;
            SizeChanged += OnWindowSizeChanged;

            // 自愈式层级：激活时置顶（保证能看见），失焦后放下（不赖在最前面）
            Activated += delegate { Topmost = true; };
            Deactivated += delegate { Topmost = false; };

            PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) Close();
            };
        }

        // =====================================================================
        // 可拖动调整大小（真实覆盖层 + 自己算 Rect）
        // =====================================================================
        // 为什么这么做：
        //  · 这套窗口是"非分层 + DWM 材质 + 无边框"，WindowChrome 的 ResizeBorderThickness
        //    在这种组合下命中不到（实测拖角落完全没反应）；
        //  · 也不能只在 Window 上等 PreviewMouseDown —— 边缘那圈会被 ScrollViewer/描边吃掉；
        //  · 所以用 8 个真实元素（4 边 + 4 角）盖在最上层：有背景、能命中、带对应光标，
        //    按下就 capture 自己，之后的移动全部用来改 Left/Top/Width/Height。
        private const double GrabBand = 6;     // 边可拖拽厚度（DIP）
        private const double CornerSize = 12;  // 角可拖拽尺寸（DIP）
        private int _resizeEdges;              // 1=左 2=右 4=上 8=下
        private Point _resizeStart;            // 屏幕坐标（DIP）
        private Rect _resizeRect;              // 起始 Rect（DIP）

        private FrameworkElement BuildResizeOverlay()
        {
            var g = new Grid { Background = null, IsHitTestVisible = false };

            g.Children.Add(EdgeBand(HorizontalAlignment.Left, VerticalAlignment.Stretch,
                GrabBand, double.NaN, 1, Cursors.SizeWE));
            g.Children.Add(EdgeBand(HorizontalAlignment.Right, VerticalAlignment.Stretch,
                GrabBand, double.NaN, 2, Cursors.SizeWE));
            g.Children.Add(EdgeBand(HorizontalAlignment.Stretch, VerticalAlignment.Top,
                double.NaN, GrabBand, 4, Cursors.SizeNS));
            g.Children.Add(EdgeBand(HorizontalAlignment.Stretch, VerticalAlignment.Bottom,
                double.NaN, GrabBand, 8, Cursors.SizeNS));

            g.Children.Add(CornerBand(HorizontalAlignment.Left, VerticalAlignment.Top, 1 | 4, Cursors.SizeNWSE));
            g.Children.Add(CornerBand(HorizontalAlignment.Right, VerticalAlignment.Top, 2 | 4, Cursors.SizeNESW));
            g.Children.Add(CornerBand(HorizontalAlignment.Left, VerticalAlignment.Bottom, 1 | 8, Cursors.SizeNESW));
            g.Children.Add(CornerBand(HorizontalAlignment.Right, VerticalAlignment.Bottom, 2 | 8, Cursors.SizeNWSE));

            return g;
        }

        private FrameworkElement EdgeBand(HorizontalAlignment ha, VerticalAlignment va,
            double w, double h, int edges, Cursor cursor)
        {
            var b = new Border { Background = Brushes.Transparent, HorizontalAlignment = ha, VerticalAlignment = va, Cursor = cursor };
            if (!double.IsNaN(w)) b.Width = w;
            if (!double.IsNaN(h)) b.Height = h;
            AttachResize(b, edges);
            return b;
        }

        private FrameworkElement CornerBand(HorizontalAlignment ha, VerticalAlignment va, int edges, Cursor cursor)
        {
            var b = new Border
            {
                Background = Brushes.Transparent,
                HorizontalAlignment = ha,
                VerticalAlignment = va,
                Width = CornerSize,
                Height = CornerSize,
                Cursor = cursor
            };
            AttachResize(b, edges);
            return b;
        }

        private void AttachResize(FrameworkElement e, int edges)
        {
            e.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs args)
            {
                if (WindowState != WindowState.Normal) return;
                _resizeEdges = edges;
                _resizeStart = ScreenPoint(args);
                _resizeRect = new Rect(Left, Top, ActualWidth, ActualHeight);
                try { ((UIElement)s).CaptureMouse(); } catch { }
                args.Handled = true;
            };
            e.MouseMove += delegate (object s, MouseEventArgs args)
            {
                if (_resizeEdges == 0) return;
                if (args.LeftButton != MouseButtonState.Pressed) { EndResize(); return; }
                ApplyResize(ScreenPoint(args));
                args.Handled = true;
            };
            e.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs args)
            {
                if (_resizeEdges == 0) return;
                EndResize();
                args.Handled = true;
            };
            e.LostMouseCapture += delegate { if (_resizeEdges != 0) EndResize(); };
        }

        private void ApplyResize(Point now)
        {
            double dx = now.X - _resizeStart.X;
            double dy = now.Y - _resizeStart.Y;

            double x = _resizeRect.X, y = _resizeRect.Y;
            double w = _resizeRect.Width, h = _resizeRect.Height;

            if ((_resizeEdges & 1) != 0) { x += dx; w -= dx; }
            if ((_resizeEdges & 2) != 0) { w += dx; }
            if ((_resizeEdges & 4) != 0) { y += dy; h -= dy; }
            if ((_resizeEdges & 8) != 0) { h += dy; }

            if (w < MinWidth) { if ((_resizeEdges & 1) != 0) x = _resizeRect.Right - MinWidth; w = MinWidth; }
            if (h < MinHeight) { if ((_resizeEdges & 4) != 0) y = _resizeRect.Bottom - MinHeight; h = MinHeight; }
            if (w > MaxWidth) w = MaxWidth;
            if (h > MaxHeight) h = MaxHeight;

            Left = x; Top = y; Width = w; Height = h;
        }

        private void EndResize()
        {
            if (_resizeEdges == 0) return;
            _resizeEdges = 0;
            try { ReleaseMouseCapture(); } catch { }
            PersistSizeNow();
        }

        /// <summary>鼠标位置换算成屏幕 DIP（把窗口缩放算进去）。</summary>
        private Point ScreenPoint(MouseEventArgs e)
        {
            try
            {
                Point dev = PointToScreen(e.GetPosition(this));
                PresentationSource src = PresentationSource.FromVisual(this);
                double s = (src != null && src.CompositionTarget != null)
                    ? src.CompositionTarget.TransformFromDevice.M11 : 1.0;
                return new Point(dev.X * s, dev.Y * s);
            }
            catch { return _resizeStart; }
        }

        private void PersistSizeNow()
        {
            try
            {
                if (WindowState != WindowState.Normal) return;
                S.UiWidth = Math.Round(ActualWidth);
                S.UiHeight = Math.Round(ActualHeight);
                _dock.SaveSettings();
            }
            catch { }
        }
        // =====================================================================
        // 可拖动调整大小
        // =====================================================================
        /// <summary>
        /// 首次渲染完把高度固定下来。
        /// 用 SizeToContent=Height 时窗口高度由内容决定，用户拖边框会被内容测量顶回去，
        /// 所以第一次排版后就切成手动尺寸——之后拖多大就是多大。
        /// </summary>
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCLBUTTONDOWN = 0x00A1;

        private DispatcherTimer _resizeTick;
        private int _resizeCode;

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        private double DpiScale()
        {
            try
            {
                uint dpi = GetDpiForWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);
                if (dpi > 0) return dpi / 96.0;
            }
            catch { }
            return 1.0;
        }

        /// <summary>
        /// 边缘命中 + 边缘按下。
        /// 命中返回缩放码（系统给出正确光标）；**按下**则自己接管拖拽（不进系统的模态 sizing 循环），
        /// 这样真实鼠标/注入输入都能调整，不会出现"光标变了但拖不动"。
        /// </summary>
        private IntPtr HitTestHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (ResizeMode == ResizeMode.NoResize || WindowState != WindowState.Normal) return IntPtr.Zero;

            if (msg == WM_NCHITTEST)
            {
                try
                {
                    int sx = (short)(lParam.ToInt64() & 0xFFFF);
                    int sy = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                    int code = HitCodeAt(sx, sy);
                    if (code == 0) return IntPtr.Zero;
                    handled = true;
                    return new IntPtr(code);
                }
                catch { return IntPtr.Zero; }
            }

            if (msg == WM_NCLBUTTONDOWN)
            {
                int code = wParam.ToInt32();
                if (code < 10 || code > 17) return IntPtr.Zero;
                try
                {
                    POINT p;
                    GetCursorPos(out p);
                    BeginManualResize(code, p);
                    handled = true;
                    return IntPtr.Zero;
                }
                catch { return IntPtr.Zero; }
            }

            return IntPtr.Zero;
        }

        /// <summary>屏幕物理坐标落在哪条边（0 = 不在边缘）。</summary>
        private int HitCodeAt(int screenX, int screenY)
        {
            double s = DpiScale();
            int ox = (int)Math.Round(Left * s), oy = (int)Math.Round(Top * s);
            double x = (screenX - ox) / s, y = (screenY - oy) / s;
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return 0;

            bool l = x <= GrabBand, r = x >= w - GrabBand;
            bool t = y <= GrabBand, b = y >= h - GrabBand;
            if (!l && !r && !t && !b) return 0;

            if (l && t) return 13;
            if (r && t) return 14;
            if (l && b) return 16;
            if (r && b) return 17;
            if (l) return 10;
            if (r) return 11;
            if (t) return 12;
            return 15;
        }

        private void BeginManualResize(int code, POINT screen)
        {
            double s = DpiScale();
            _resizeCode = code;
            _resizeEdges = 0;
            if (code == 10 || code == 13 || code == 16) _resizeEdges |= 1;
            if (code == 11 || code == 14 || code == 17) _resizeEdges |= 2;
            if (code == 12 || code == 13 || code == 14) _resizeEdges |= 4;
            if (code == 15 || code == 16 || code == 17) _resizeEdges |= 8;

            _resizeStart = new Point(screen.X / s, screen.Y / s);
            _resizeRect = new Rect(Left, Top, ActualWidth, ActualHeight);

            if (_resizeTick == null)
            {
                _resizeTick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                _resizeTick.Tick += delegate { OnResizeTick(); };
            }
            _resizeTick.Start();
        }

        private void OnResizeTick()
        {
            try
            {
                if ((GetAsyncKeyState(0x01) & 0x8000) == 0) { EndManualResize(); return; }
                POINT p;
                if (!GetCursorPos(out p)) return;
                double s = DpiScale();
                ApplyResize(new Point(p.X / s, p.Y / s));
            }
            catch { EndManualResize(); }
        }

        private void EndManualResize()
        {
            try { if (_resizeTick != null) _resizeTick.Stop(); } catch { }
            if (_resizeEdges == 0) return;
            _resizeEdges = 0;
            PersistSizeNow();
        }

        private void OnSizedOnce(object sender, EventArgs e)

        {
            ContentRendered -= OnSizedOnce;
            if (_sizeLocked) return;
            try
            {
                Height = ActualHeight;
                SizeToContent = SizeToContent.Manual;
                _sizeLocked = true;
            }
            catch { }
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_sizeLocked || !IsLoaded) return;
            if (WindowState != WindowState.Normal) return;

            // 拖动过程中频繁触发，这里只重置计时器，停手 400ms 再保存
            if (_sizeSaveTimer == null)
            {
                _sizeSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _sizeSaveTimer.Tick += delegate
                {
                    _sizeSaveTimer.Stop();
                    try
                    {
                        S.UiWidth = Math.Round(ActualWidth);
                        S.UiHeight = Math.Round(ActualHeight);
                        _dock.SaveSettings();
                    }
                    catch { }
                };
            }
            _sizeSaveTimer.Stop();
            _sizeSaveTimer.Start();
        }

        /// <summary>恢复成默认大小（双击标题区，或设置里的「恢复默认外观」）。</summary>
        public void ResetWindowSize()
        {
            try
            {
                S.UiWidth = 0;
                S.UiHeight = 0;
                _dock.SaveSettings();

                SizeToContent = SizeToContent.Height;
                Width = 800;
                _sizeLocked = false;

                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
                {
                    Height = ActualHeight;
                    SizeToContent = SizeToContent.Manual;
                    _sizeLocked = true;
                }));
            }
            catch { }
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            Backdrop.EnableTransparentComposition(this);
            Backdrop.ExtendGlass(this);
            ApplyBackdrop();

            // 边框缩放走系统通道：WM_NCHITTEST 对边缘返回 HTLEFT/HTBOTTOMRIGHT…（光标由系统给），
            // 边缘"按下"由 Hook 里的 WM_NCLBUTTONDOWN 自己接管（轮询 GetCursorPos + 按键状态），
            // 不进系统的模态 sizing 循环，真实鼠标和注入输入都能拖。
            try
            {
                IntPtr h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                System.Windows.Interop.HwndSource src = System.Windows.Interop.HwndSource.FromHwnd(h);
                if (src != null) src.AddHook(HitTestHook);
            }
            catch (Exception ex) { SettingsStore.Log("HitTest hook: " + ex.Message); }
        }

        /// <summary>应用当前的背景材质预设（磨砂 / 玻璃）。</summary>
        private void ApplyBackdrop()
        {
            Backdrop.Apply(this, S.UiBackdrop, Palette.CreateUi(S).IsDark);
        }

        /// <summary>外壳底色：选了材质就半透明让材质透出来，否则保持原来的通透度。</summary>
        private Brush ShellBackground()
        {
            Color c = _pal.WindowColor;

            if (S.UiBackdrop == BackdropKind.Glass)
            {
                // 玻璃拟态：浅色是“10% 白半透”，深色沿用原来的深蓝黑底（约 70%）
                double a = (_pal.IsDark ? 0xB4 : 0x2E) * Math.Min(1.0, Math.Max(0.0, S.UiOpacity));
                byte ga = (byte)Math.Round(Math.Min(255, Math.Max(0, a)));
                return _pal.IsDark
                    ? new SolidColorBrush(Color.FromArgb(ga, 0x20, 0x20, 0x28))
                    : new SolidColorBrush(Color.FromArgb(ga, 0xFF, 0xFF, 0xFF));
            }

            // 磨砂/玻璃都会自带底，深色主题再压一下保证文字清晰
            double baseAlpha = S.UiOpacity;
            if (S.UiBackdrop == BackdropKind.Frosted) baseAlpha *= 0.55;
            byte alpha = (byte)Math.Round(255 * Math.Min(1.0, Math.Max(0.0, baseAlpha)));
            return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        }

        private void BuildUi()
        {
            _pal = Palette.CreateUi(S);
            _cards.Clear();

            // 底色铺满整个矩形，描边单独一层、带圆角（圆角跟着「圆角半径」走）
            _shellFill = new Border
            {
                Background = ShellBackground(),
                IsHitTestVisible = false
            };
            _shellStroke = new Border
            {
                BorderBrush = _pal.Glass ? _pal.GlassBorder : _pal.CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(S.UiRadius),
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            };

            _shell = new Grid();
            _shell.Children.Add(_shellFill);

            var dp = new DockPanel { LastChildFill = true };

            FrameworkElement header = BuildHeader();
            DockPanel.SetDock(header, Dock.Top);
            dp.Children.Add(header);

            var line1 = new Border { Height = 1, Background = _pal.Divider };
            DockPanel.SetDock(line1, Dock.Top);
            dp.Children.Add(line1);

            FrameworkElement footer = BuildFooter();
            DockPanel.SetDock(footer, Dock.Bottom);
            dp.Children.Add(footer);

            dp.Children.Add(BuildBody());

            if (S.UiBackdrop == BackdropKind.Glass)
            {
                _shell.Children.Add(BuildAurora());   // 大块柔和纹理
                _shell.Children.Add(dp);
                _shell.Children.Add(BuildSheen());    // 顶部柔和折射高光
            }
            else
            {
                _shell.Children.Add(dp);
            }

            _shell.Children.Add(_shellStroke);        // 描边压在最上层
            _shell.Children.Add(BuildResizeHint());   // 右下角"可拖动"示意

            var root = new Grid();
            root.Children.Add(_shell);
            root.Children.Add(BuildResizeOverlay());   // 视觉/光标提示层（命中交给 WM_NCHITTEST）

            Content = root;
        }

        // =====================================================================
        // 玻璃拟态的装饰层
        // =====================================================================
        /// <summary>两团大块柔光（主色只取 1-2 种，保证文字始终清晰）。</summary>
        private FrameworkElement BuildAurora()
        {
            Color primary, second;
            byte a1, a2;

            if (_pal.IsDark)
            {
                primary = Color.FromRgb(0x4C, 0xC2, 0xFF);   // 青蓝
                second = Color.FromRgb(0x8B, 0x5C, 0xF6);    // 紫
                a1 = 0x34;
                a2 = 0x34;
            }
            else
            {
                // 浅色：纯白高光，不再带蓝紫色调
                primary = Colors.White;
                second = Colors.White;
                a1 = 0x40;
                a2 = 0x2C;
            }

            var g = new Grid { IsHitTestVisible = false, ClipToBounds = true };
            g.Children.Add(Blob(primary, a1, 640, HorizontalAlignment.Left, VerticalAlignment.Top,
                new Thickness(-200, -220, 0, 0)));
            g.Children.Add(Blob(second, a2, 560, HorizontalAlignment.Right, VerticalAlignment.Bottom,
                new Thickness(0, 0, -170, -150)));
            return g;
        }

        private static Border Blob(Color c, byte a, double size,
            HorizontalAlignment ha, VerticalAlignment va, Thickness margin)
        {
            return new Border
            {
                Width = size,
                Height = size,
                HorizontalAlignment = ha,
                VerticalAlignment = va,
                Margin = margin,
                CornerRadius = new CornerRadius(size / 2),
                IsHitTestVisible = false,
                Background = new RadialGradientBrush(
                    Color.FromArgb(a, c.R, c.G, c.B),
                    Color.FromArgb(0, c.R, c.G, c.B))
            };
        }

        /// <summary>顶部一层很淡的白色渐变，模拟玻璃的折射面。</summary>
        private FrameworkElement BuildSheen()
        {
            return new Border
            {
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Top,
                Height = 150,
                Background = new LinearGradientBrush(
                    Color.FromArgb(_pal.IsDark ? (byte)0x20 : (byte)0x33, 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255), 90)
            };
        }

        /// <summary>整窗重建（配色变化时用）。</summary>
        private void Restyle()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
            {
                Content = null;
                BuildUi();
                // 深浅色或背景效果变了，必须重新套一遍系统材质：
                // 玻璃在深色走 WCA 模糊、浅色走亚克力，不重套就会留着上一个主题的底
                ApplyBackdrop();
            }));
        }

        /// <summary>只重绘外壳与卡片：拖动透明度/圆角滑块时用，避免打断拖动。</summary>
        private void ApplySurface()
        {
            if (_shell == null) return;
            Palette p = Palette.CreateUi(S);
            double r = S.UiRadius;

            _shellFill.Background = ShellBackground();
            _shellStroke.BorderBrush = p.Glass ? p.GlassBorder : p.CardBorder;
            _shellStroke.CornerRadius = new CornerRadius(r);
            foreach (Border c in _cards)
            {
                c.Background = p.Glass ? p.GlassCardBg : p.CardBg;
                c.BorderBrush = p.Glass ? p.GlassBorder : p.CardBorder;
                c.CornerRadius = new CornerRadius(p.Glass ? Math.Round(r * 0.83 + 5) : Math.Round(r * 0.83));
            }
        }

        private FrameworkElement BuildHeader()
        {
            var g = new Grid { Height = 64, Background = Brushes.Transparent };

            var sp = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(44, 0, 0, 0)
            };
            sp.Children.Add(Sx.TxtBold(AppInfo.Name + L.T(" 设置"), 17, _pal.Text));
            TextBlock sub = Sx.Txt(L.T("改动即时生效并自动保存　·　v") + AppInfo.Version + "　·　" + AppInfo.Tagline, 11.5, _pal.TextMuted);
            sub.Margin = new Thickness(0, 4, 0, 0);
            sp.Children.Add(sub);
            g.Children.Add(sp);

            var dot = new Ellipse
            {
                Width = 14,
                Height = 14,
                Fill = _pal.Accent,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(22, 0, 0, 0)
            };
            g.Children.Add(dot);

            TextBlock x = Sx.Txt("✕", 13, _pal.TextMuted);
            x.HorizontalAlignment = HorizontalAlignment.Center;
            x.VerticalAlignment = VerticalAlignment.Center;

            var close = new Border
            {
                Child = x,
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(Sx.RButton(_pal) + 6),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0)
            };
            close.MouseEnter += delegate { close.Background = _pal.SubtleHover; x.Foreground = _pal.Text; };
            close.MouseLeave += delegate { close.Background = Brushes.Transparent; x.Foreground = _pal.TextMuted; };
            close.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            close.MouseLeftButtonUp += delegate { Close(); };
            g.Children.Add(close);

            g.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    try { DragMove(); } catch { }
                }
            };
            return g;
        }

        private FrameworkElement BuildFooter()
        {
            var g = new Grid { Height = 62 };

            var left = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(18, 0, 0, 0)
            };
            left.Children.Add(Sx.Button(_pal, L.T("重新扫描应用"), false, delegate { _dock.RefreshRunning(true); }));
            left.Children.Add(Sx.Button(_pal, L.T("恢复默认外观"), false, ResetAppearance));
            g.Children.Add(left);

            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 18, 0)
            };

            TextBlock author = Sx.Txt(L.T("作者：") + AppInfo.Author + "　·　" + AppInfo.Name + " v" + AppInfo.Version, 11.5, _pal.TextMuted);
            author.VerticalAlignment = VerticalAlignment.Center;
            author.Margin = new Thickness(0, 0, 16, 0);
            right.Children.Add(author);

            Border ok = Sx.Button(_pal, L.T("完成"), true, delegate { Close(); });
            ok.Margin = new Thickness(0, 0, 0, 0);
            right.Children.Add(ok);
            g.Children.Add(right);

            return g;
        }

        private FrameworkElement BuildBody()
        {
            var left = new StackPanel { Margin = new Thickness(18, 14, 7, 10) };
            var right = new StackPanel { Margin = new Thickness(7, 14, 18, 10) };

            // 左列：Dock 外观 / 设置界面自身外观
            var appearRows = new List<UIElement>();
            appearRows.Add(BuildPreview());
            appearRows.Add(ThemeRow());
            appearRows.Add(OpacityBlock());
            appearRows.Add(OpacityHint());
            appearRows.Add(IconSizeBlock());
            left.Children.Add(AddCard(Sx.Card(_pal, L.T("Dock 外观"), appearRows.ToArray())));

            // 图标倒影（只作用于 Dock 图标）
            left.Children.Add(AddCard(Sx.Card(_pal, L.T("图标倒影反光"),
                ReflectRow(), ReflectStrengthBlock(), ReflectHeightBlock(),
                ReflectBlurBlock(), ReflectFadeBlock())));

            left.Children.Add(AddCard(Sx.Card(_pal, L.T("设置界面"),
                UiThemeRow(), UiBackdropRow(), UiOpacityBlock(), UiRadiusBlock(), UiRadiusHint())));

            // 右列：交互 / 位置 / 其它
            right.Children.Add(AddCard(Sx.Card(_pal, L.T("交互与动画"),
                MagnifyRow(), MagnifyBlock(), AutoHideRow(), AutoHideDelayBlock())));

            right.Children.Add(AddCard(Sx.Card(_pal, L.T("位置与层级"),
                DockEdgeRow(), TopmostRow(), HideTaskbarRow(), TaskbarHotkeyRow())));

            // 系统区：铃铛 / 状态胶囊 / 溢出入口
            right.Children.Add(AddCard(Sx.Card(_pal, L.T("系统区"),
                SystemAreaRow(), SystemTimeRow(), SystemBadgeRow(),
                TrayOverflowRow(), FullscreenHideRow(), PanelAnimRow(), PanelFirstRow())));

            right.Children.Add(AddCard(Sx.Card(_pal, L.T("其它"), ShowRunningRow(), LanguageRow(), StartupRow())));

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, 1);
            grid.Children.Add(left);
            grid.Children.Add(right);

            return new ScrollViewer
            {
                Content = grid,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
        }

        /// <summary>右下角三颗斜点：提示这里可以拖动改变大小（WindowChrome 负责真正的拖拽）。</summary>
        private FrameworkElement BuildResizeHint()
        {
            var sp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 12, 10),
                IsHitTestVisible = false,
                Opacity = 0.45
            };
            double d = 3;
            for (int i = 0; i < 3; i++)
            {
                sp.Children.Add(new Ellipse
                {
                    Width = d,
                    Height = d,
                    Fill = _pal.TextMuted,
                    Margin = new Thickness(1.5, 2.6 * (3 - i), 0, 0),
                    VerticalAlignment = VerticalAlignment.Bottom
                });
            }
            return sp;
        }

        private Border AddCard(Border card)
        {
            _cards.Add(card);
            return card;
        }

        // =====================================================================
        // 背景材质预设
        // =====================================================================
        // 三档：无 / 磨砂（系统亚克力）/ 玻璃（玻璃拟态）
        private static readonly string[] BackdropNames = { L.T("无"), L.T("磨砂"), L.T("玻璃") };
        private static readonly BackdropKind[] BackdropValues =
            { BackdropKind.None, BackdropKind.Frosted, BackdropKind.Glass };

        private FrameworkElement UiBackdropRow()
        {
            int cur = 0;
            for (int i = 0; i < BackdropValues.Length; i++)
                if (BackdropValues[i] == S.UiBackdrop) cur = i;

            var seg = new Segmented(_pal, BackdropNames, cur, delegate (int i)
            {
                S.UiBackdrop = BackdropValues[i];
                Apply();
                Restyle();             // Restyle 会按需重新截取背后画面
            });
            return StackedRow(L.T("界面背景效果"), seg);
        }

        /// <summary>标题在上、控件占满整行（4 段的分段控件需要整行宽度）。</summary>
        private FrameworkElement StackedRow(string title, FrameworkElement control)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 9, 0, 9) };
            sp.Children.Add(Sx.Txt(title, 13, _pal.Text));
            control.Margin = new Thickness(0, 8, 0, 0);
            sp.Children.Add(control);
            return sp;
        }

        // =====================================================================
        // Dock 预览条
        // =====================================================================
        private FrameworkElement BuildPreview()
        {
            var bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Color[] cols =
            {
                Color.FromRgb(0x4C, 0x8F, 0xF5),
                Color.FromRgb(0x2F, 0xB4, 0x7C),
                Color.FromRgb(0xF2, 0xA0, 0x3D),
                Color.FromRgb(0xE0, 0x5B, 0x6C),
                Color.FromRgb(0x8B, 0x5C, 0xF6)
            };

            foreach (Color c in cols)
            {
                var col = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(5, 0, 5, 0) };
                col.Children.Add(new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(c),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                col.Children.Add(new Ellipse
                {
                    Width = 4,
                    Height = 4,
                    Fill = _pal.Dot,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 0)
                });
                bar.Children.Add(col);
            }

            _preview = new Border
            {
                Height = 46,
                CornerRadius = new CornerRadius(Math.Max(2, _pal.CornerRadius)),
                Background = _pal.BarBackground,
                BorderBrush = _pal.BarBorder,
                BorderThickness = new Thickness(1),
                Child = bar
            };

            var stage = new Border
            {
                CornerRadius = new CornerRadius(Sx.RCard(_pal)),
                Background = new LinearGradientBrush(
                    Color.FromRgb(0x5B, 0x8D, 0xEF),
                    Color.FromRgb(0xB0, 0x7C, 0xE8), 40),
                Padding = new Thickness(18, 16, 18, 16),
                Margin = new Thickness(0, 2, 0, 12),
                Child = _preview
            };

            var wrap = new StackPanel();
            wrap.Children.Add(stage);

            TextBlock hint = Sx.Txt(L.T("预览：真实壁纸色块上的 Dock 条（跟随主题与不透明度）"), 11, _pal.TextMuted);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(2, 0, 0, 2);
            wrap.Children.Add(hint);
            return wrap;
        }

        private void UpdatePreview()
        {
            if (_preview == null) return;
            Palette p = Palette.Create(S);
            _preview.Background = p.BarBackground;
            _preview.BorderBrush = p.BarBorder;
            _preview.CornerRadius = new CornerRadius(Math.Max(2, p.CornerRadius));
        }

        // =====================================================================
        // Dock 外观
        // =====================================================================
        private static readonly int[] ThemeMap = { 0, 2, 1 };   // 视图序号 -> DockTheme

        private FrameworkElement ThemeRow()
        {
            int cur = 0;
            for (int i = 0; i < ThemeMap.Length; i++) if (ThemeMap[i] == (int)S.Theme) cur = i;

            var seg = new Segmented(_pal, new string[] { L.T("跟随系统"), L.T("浅色"), L.T("深色") }, cur,
                delegate (int i)
                {
                    S.Theme = (DockTheme)ThemeMap[i];
                    Apply();
                    Restyle();
                });
            seg.MinWidth = 186;
            return Sx.Row(_pal, L.T("Dock 主题"), null, seg);
        }

        private FrameworkElement OpacityBlock()
        {
            TextBlock val = Sx.Txt(OpacityText(S.BarOpacity), 12, _pal.TextMuted);

            var sl = new FluentSlider(_pal, 0, 100, S.BarOpacity * 100, delegate (double v)
            {
                S.BarOpacity = Math.Round(v / 100.0, 2);
                val.Text = OpacityText(S.BarOpacity);
                Apply();
                UpdatePreview();
            });
            return Sx.SliderBlock(_pal, L.T("背景不透明度"), val, sl);
        }

        /// <summary>0% 单独叫"完全透明"。</summary>
        private static string OpacityText(double opacity)
        {
            int pct = (int)Math.Round(opacity * 100);
            return pct <= 0 ? L.T("完全透明") : pct + "%";
        }

        /// <summary>完全透明时的提示：靠悬停轮廓保证可发现、可点击。</summary>
        private FrameworkElement OpacityHint()
        {
            TextBlock hint = Sx.Txt(S.BarOpacity <= 0.001
                ? L.T("完全透明：鼠标悬停时才会显出一层细轮廓，保证还能找到并点击")
                : L.T("拉到 0% 可完全透明，悬停时会临时显示轮廓"),
                11, _pal.TextMuted);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(2, -2, 2, 6);
            return hint;
        }

        private FrameworkElement IconSizeBlock()
        {
            TextBlock val = Sx.Txt(S.IconSize + " px", 12, _pal.TextMuted);

            var sl = new FluentSlider(_pal, 24, 96, S.IconSize, delegate (double v)
            {
                S.IconSize = (int)Math.Round(v);
                val.Text = S.IconSize + " px";
                Apply();
            });
            return Sx.SliderBlock(_pal, L.T("图标大小"), val, sl);
        }

        // =====================================================================
        // 设置界面自身
        // =====================================================================
        private FrameworkElement UiThemeRow()
        {
            int cur = 0;
            if (S.UiTheme == DockTheme.Light) cur = 1;
            else if (S.UiTheme == DockTheme.Dark) cur = 2;

            var seg = new Segmented(_pal, new string[] { L.T("跟随 Dock"), L.T("浅色"), L.T("深色") }, cur,
                delegate (int i)
                {
                    S.UiTheme = i == 0 ? DockTheme.Auto : (i == 1 ? DockTheme.Light : DockTheme.Dark);
                    Apply();
                    Restyle();
                });
            seg.MinWidth = 186;
            return Sx.Row(_pal, L.T("深浅"), null, seg);
        }

        private FrameworkElement UiOpacityBlock()
        {
            TextBlock val = Sx.Txt(((int)Math.Round(S.UiOpacity * 100)) + "%", 12, _pal.TextMuted);

            var sl = new FluentSlider(_pal, 55, 100, S.UiOpacity * 100, delegate (double v)
            {
                S.UiOpacity = Math.Round(v / 100.0, 2);
                val.Text = ((int)Math.Round(v)) + "%";
                ApplySurface();
                _dock.SaveSettings();
            });
            sl.OnCommit = delegate { Restyle(); };
            return Sx.SliderBlock(_pal, L.T("界面透明度"), val, sl);
        }

        private FrameworkElement UiRadiusBlock()
        {
            TextBlock val = Sx.Txt(S.UiRadius < 0.5 ? L.T("直角") : ((int)S.UiRadius) + " px", 12, _pal.TextMuted);

            var sl = new FluentSlider(_pal, 0, 26, S.UiRadius, delegate (double v)
            {
                S.UiRadius = Math.Round(v);
                val.Text = S.UiRadius < 0.5 ? L.T("直角") : ((int)S.UiRadius) + " px";
                ApplySurface();
                _dock.SaveSettings();
            });
            sl.OnCommit = delegate { Restyle(); };
            return Sx.SliderBlock(_pal, L.T("圆角半径"), val, sl);
        }

        private FrameworkElement UiRadiusHint()
        {
            TextBlock t = Sx.Txt(L.T("拉到 0 就是直角（方角）界面；只影响这个设置窗口，不影响 Dock。"),
                11, _pal.TextMuted);
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(2, 0, 2, 2);
            return t;
        }

        // =====================================================================
        // 交互 / 位置 / 其它
        // =====================================================================
        private FrameworkElement DockEdgeRow()
        {
            int cur = S.Position == DockPosition.Custom ? 2 : (S.Edge == DockEdge.Bottom ? 0 : 1);

            var seg = new Segmented(_pal, new string[] { L.T("屏幕底部"), L.T("屏幕顶部"), L.T("自定义") }, cur,
                delegate (int i)
                {
                    if (i == 2)
                    {
                        // 拖动时本窗口会挡路，先关掉再进拖动模式
                        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                        {
                            Close();
                            _dock.BeginPositionEdit();
                        }));
                        return;
                    }
                    S.Position = DockPosition.Edge;
                    S.Edge = i == 0 ? DockEdge.Bottom : DockEdge.Top;
                    Apply();
                });
            seg.MinWidth = 208;
            return Sx.Row(_pal, L.T("停靠位置"), null, seg);
        }

        private FrameworkElement TopmostRow()
        {
            var tg = new ToggleSwitch(_pal, S.AlwaysOnTop, delegate (bool on) { _dock.SetAlwaysOnTop(on); });
            return Sx.Row(_pal, L.T("始终置顶"), L.T("关闭则只在桌面上显示，会被其他窗口覆盖"), tg);
        }

        private FrameworkElement HideTaskbarRow()
        {
            var tg = new ToggleSwitch(_pal, S.HideTaskbar, delegate (bool on) { _dock.SetHideTaskbar(on); });
            return Sx.Row(_pal, L.T("隐藏 Windows 任务栏"), L.T("把系统任务栏藏起来，退出 Dock 时自动恢复"), tg);
        }

        private FrameworkElement TaskbarHotkeyRow()
        {
            var sp = new StackPanel();

            var box = new HotkeyBox(_pal, S.HotkeyMods, S.HotkeyVk, delegate (int mods, int vk)
            {
                _dock.SetTaskbarHotkey(mods, vk);
                Restyle();          // 重新渲染，顺便反映注册成败
            });

            sp.Children.Add(Sx.Row(_pal, L.T("切换任务栏快捷键"), L.T("点击后按下组合键；右键清除"), box));

            if (S.HotkeyVk > 0 && !_dock.IsHotkeyActive)
            {
                TextBlock warn = Sx.Txt(L.T("该组合键可能已被其它程序占用，请换一个"), 11,
                    new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x38)));
                warn.Margin = new Thickness(2, 0, 2, 2);
                warn.TextWrapping = TextWrapping.Wrap;
                sp.Children.Add(warn);
            }

            return sp;
        }

        private FrameworkElement MagnifyRow()
        {
            var tg = new ToggleSwitch(_pal, S.Magnify, delegate (bool on) { S.Magnify = on; Apply(); });
            return Sx.Row(_pal, L.T("悬停放大"), L.T("鼠标划过图标时放大并让位"), tg);
        }

        private FrameworkElement MagnifyBlock()
        {
            TextBlock val = Sx.Txt(S.MagnifyMax.ToString("0.00") + "x", 12, _pal.TextMuted);

            var sl = new FluentSlider(_pal, 100, 200, S.MagnifyMax * 100, delegate (double v)
            {
                S.MagnifyMax = Math.Round(v / 100.0, 2);
                val.Text = S.MagnifyMax.ToString("0.00") + "x";
                Apply();
            });
            return Sx.SliderBlock(_pal, L.T("放大倍数"), val, sl);
        }

        private FrameworkElement AutoHideRow()
        {
            var tg = new ToggleSwitch(_pal, S.AutoHide, delegate (bool on) { S.AutoHide = on; Apply(); });
            return Sx.Row(_pal, L.T("自动隐藏"), L.T("收起后只在屏幕边缘留 4px 触发条"), tg);
        }

        private FrameworkElement AutoHideDelayBlock()
        {
            TextBlock val = Sx.Txt(((int)S.AutoHideDelayMs) + " ms", 12, _pal.TextMuted);

            var sl = new FluentSlider(_pal, 0, 2000, S.AutoHideDelayMs, delegate (double v)
            {
                S.AutoHideDelayMs = Math.Round(v / 50.0) * 50;
                val.Text = ((int)S.AutoHideDelayMs) + " ms";
                Apply();
            });
            return Sx.SliderBlock(_pal, L.T("收起延迟"), val, sl);
        }

        // ---------------- 系统区（铃铛 / 状态胶囊 / 溢出入口）----------------
        private FrameworkElement SystemAreaRow()
        {
            var tg = new ToggleSwitch(_pal, S.SystemArea, delegate (bool on)
            {
                S.SystemArea = on;
                Apply();            // 触发 Dock 重建，系统区跟着出现/消失
            });
            return Sx.Row(_pal, L.T("显示系统区"), L.T("Dock 末端的 铃铛 · 状态胶囊 · 溢出入口"), tg);
        }

        private FrameworkElement SystemTimeRow()
        {
            var tg = new ToggleSwitch(_pal, S.SystemCapsuleTime, delegate (bool on)
            {
                S.SystemCapsuleTime = on;
                Apply();
            });
            return Sx.Row(_pal, L.T("胶囊显示时间"), L.T("关闭则只显示网络 / 音量 / 电池"), tg);
        }

        private FrameworkElement SystemBadgeRow()
        {
            var tg = new ToggleSwitch(_pal, S.SystemBadge, delegate (bool on)
            {
                S.SystemBadge = on;
                Apply();
            });
            return Sx.Row(_pal, L.T("通知未读角标"), L.T("有新通知时铃铛上显示数字，并轻脉冲一次"), tg);
        }

        private FrameworkElement TrayOverflowRow()
        {
            var tg = new ToggleSwitch(_pal, S.TrayOverflow, delegate (bool on)
            {
                S.TrayOverflow = on;
                Apply();
            });
            return Sx.Row(_pal, L.T("溢出入口 ^"), L.T("第三方托盘图标始终折在系统托盘里，这里只做入口"), tg);
        }

        private FrameworkElement FullscreenHideRow()
        {
            var tg = new ToggleSwitch(_pal, S.FullscreenHide, delegate (bool on)
            {
                S.FullscreenHide = on;
                Apply();
            });
            return Sx.Row(_pal, L.T("全屏时自动隐藏"), L.T("全屏游戏 / 视频 / 演示时，系统区跟着 Dock 让路"), tg);
        }

        private FrameworkElement PanelAnimRow()
        {
            var tg = new ToggleSwitch(_pal, S.PanelAnim, delegate (bool on)
            {
                S.PanelAnim = on;
                Apply();
            });
            return Sx.Row(_pal, L.T("面板生长动画"), L.T("从 Dock 边缘展开，170ms ease-out"), tg);
        }

        private FrameworkElement PanelFirstRow()
        {
            var tg = new ToggleSwitch(_pal, S.PanelFirst, delegate (bool on)
            {
                S.PanelFirst = on;
                Apply();
            });
            return Sx.Row(_pal, L.T("单击先开 Dock 面板"), L.T("关闭 = 单击胶囊/铃铛直接调 Windows 快速设置/通知中心"), tg);
        }

        // ---------------- 图标倒影 ----------------
        private FrameworkElement ReflectRow()
        {
            var tg = new ToggleSwitch(_pal, S.ReflectOn, delegate (bool on)
            {
                S.ReflectOn = on;
                Apply();
            });
            return Sx.Row(_pal, L.T("启用倒影"), L.T("只作用于 Dock 图标（不影响状态胶囊和面板）"), tg);
        }

        private FrameworkElement ReflectStrengthBlock()
        {
            TextBlock val = Sx.Txt((int)Math.Round(S.ReflectStrength) + "%", 12, _pal.TextMuted);
            var sl = new FluentSlider(_pal, 0, 60, S.ReflectStrength, delegate (double v)
            {
                S.ReflectStrength = Math.Round(v);
                val.Text = ((int)Math.Round(v)) + "%";
                PreviewReflection();
            });
            sl.OnCommit = delegate { _dock.SaveSettings(); };
            sl.IsEnabled = S.ReflectOn;
            return Sx.SliderBlock(_pal, L.T("强度（越低越含蓄）"), val, sl);
        }

        private FrameworkElement ReflectHeightBlock()
        {
            TextBlock val = Sx.Txt((int)Math.Round(S.ReflectHeight) + "%", 12, _pal.TextMuted);
            var sl = new FluentSlider(_pal, 0, 100, S.ReflectHeight, delegate (double v)
            {
                S.ReflectHeight = Math.Round(v);
                val.Text = ((int)Math.Round(v)) + "%";
                PreviewReflection();
            });
            sl.OnCommit = delegate { _dock.SaveSettings(); };
            sl.IsEnabled = S.ReflectOn;
            return Sx.SliderBlock(_pal, L.T("高度（占图标高度的比例）"), val, sl);
        }

        private FrameworkElement ReflectBlurBlock()
        {
            TextBlock val = Sx.Txt((int)Math.Round(S.ReflectBlur) + " px", 12, _pal.TextMuted);
            var sl = new FluentSlider(_pal, 0, 12, S.ReflectBlur, delegate (double v)
            {
                S.ReflectBlur = Math.Round(v);
                val.Text = ((int)Math.Round(v)) + " px";
                PreviewReflection();
            });
            sl.OnCommit = delegate { _dock.SaveSettings(); };
            sl.IsEnabled = S.ReflectOn;
            return Sx.SliderBlock(_pal, L.T("模糊"), val, sl);
        }

        private FrameworkElement ReflectFadeBlock()
        {
            TextBlock val = Sx.Txt((int)Math.Round(S.ReflectFade) + "%", 12, _pal.TextMuted);
            var sl = new FluentSlider(_pal, 5, 100, S.ReflectFade, delegate (double v)
            {
                S.ReflectFade = Math.Round(v);
                val.Text = ((int)Math.Round(v)) + "%";
                PreviewReflection();
            });
            sl.OnCommit = delegate { _dock.SaveSettings(); };
            sl.IsEnabled = S.ReflectOn;
            return Sx.SliderBlock(_pal, L.T("淡出（越大越快消失）"), val, sl);
        }

        /// <summary>拖倒影滑块时的高频回调：只刷倒影，不重建 Dock、不写盘。</summary>
        private void PreviewReflection()
        {
            try { _dock.ApplyReflectionPreview(); } catch { }
        }

        private FrameworkElement ShowRunningRow()        {
            var tg = new ToggleSwitch(_pal, S.ShowUnpinnedRunning, delegate (bool on)
            {
                S.ShowUnpinnedRunning = on;
                Apply();
            });
            return Sx.Row(_pal, L.T("显示运行中但未固定的应用"), null, tg);
        }

        /// <summary>界面语言：中文 / English。切换后整个面板按新语言重建。</summary>
        private FrameworkElement LanguageRow()
        {
            var seg = new Segmented(_pal, new string[] { L.T("中文"), L.T("English") }, L.En ? 1 : 0,
                delegate (int i)
                {
                    if (S.Language == i) return;
                    App.SetLanguage(i);
                });
            seg.MinWidth = 186;
            return Sx.Row(_pal, L.T("界面语言"), L.T("立即切换，无需重启"), seg);
        }
        private FrameworkElement StartupRow()
        {
            var tg = new ToggleSwitch(_pal, S.LaunchAtStartup, delegate (bool on)
            {
                S.LaunchAtStartup = on;
                StartupManager.SetEnabled(on);
                Apply();
            });
            return Sx.Row(_pal, L.T("开机自动启动"), null, tg);
        }

        // =====================================================================
        // 动作
        // =====================================================================
        private void Apply()
        {
            _dock.SaveSettings();
            _dock.ApplyAllChanges();
        }

        private void ResetAppearance()
        {
            DockSettings d = DockSettings.CreateDefault();
            S.IconSize = d.IconSize;
            S.Magnify = d.Magnify;
            S.MagnifyMax = d.MagnifyMax;
            S.AutoHide = d.AutoHide;
            S.AutoHideDelayMs = d.AutoHideDelayMs;
            S.BarOpacity = d.BarOpacity;
            S.EdgeMargin = d.EdgeMargin;
            S.Theme = d.Theme;
            S.Edge = d.Edge;
            S.UiTheme = d.UiTheme;
            S.UiOpacity = d.UiOpacity;
            S.UiRadius = d.UiRadius;
            Apply();
            Restyle();
        }
    }
}
