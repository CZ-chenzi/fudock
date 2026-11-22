using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Fudock
{
    /// <summary>Dock 主窗口。</summary>
    public class DockWindow : Window
    {
        private const double ShadowRoom = 12;   // 窗口四周留白（放阴影/放大溢出）
        private const double StripVisible = 4;  // 自动隐藏时露出的触发条高度

        private readonly DockSettings _s;
        private Palette _pal;

        private Grid _root;
        private Border _bar;
        private StackPanel _stack;
        private Border _halo;                   // 顶部高光
        private readonly List<DockTileView> _views = new List<DockTileView>();
        private readonly List<DockTile> _items = new List<DockTile>();
        private int _pinnedCount;

        private DispatcherTimer _poll;
        private DispatcherTimer _hideTimer;
        private bool _inside, _hidden, _busy;
        private string _sig = "";
        private FlyoutWindow _flyout;

        // 拖拽（图标排序）
        private DockTileView _dragView;
        private Point _dragStart;
        private bool _dragging;
        private double _dragSlotOrigin;    // 拖动期间第一个格子的左边界（bar 坐标）
        private double _dragTileWidth;     // 拖动期间的格子宽度

        // 自定义位置（拖动整条 Dock）
        private bool _editing;
        private bool _editDragging;
        private DockPosition _origPosition;
        private double _origPosX, _origPosY;
        private PositionEditorWindow _editorWnd;
        private Native.POINT _editStartPx;
        private double _editStartLeft, _editStartTop;
        private double _dpiScale = 1.0;

        // 拖入添加的视觉反馈
        private Border _dropHint;
        private TextBlock _dropHintText;
        private DispatcherTimer _dragHintTimer;
        private bool _rebuildPending;      // 拖动期间跳过的刷新，结束后补一次

        // 悬停窗口预览
        private DispatcherTimer _previewTimer;
        private DispatcherTimer _previewWatch;
        private DockTileView _previewTile;
        private int _previewOutside;
        private readonly List<IntPtr> _warmList = new List<IntPtr>();
        public event EventHandler SettingsApplied;

        // ---------- 系统区（铃铛 / 状态胶囊 / 溢出入口）----------
        private SystemAreaView _sysArea;      // Dock 末端的三段式系统区
        private SystemPanel _sysPanel;        // 从 Dock 边缘长出来的面板
        private SystemStatus _status;         // 网络 / 音量 / 电源 / 时间
        private ToastWatcher _toast;          // 未读通知探测
        private bool _sysDimmed;              // 全屏时已隐藏
        private bool _hoverReveal;            // 完全透明时才用：悬停临时显形
        private double _lastBarH = -1;        // 上次真正套用到窗口上的条高
        private double _appliedAreaW = -1;    // 上次 ApplyGeometry 用到的系统区宽度（自愈用）
        private int _fullStreak;              // 连续几次判定为全屏（去抖，避免抖一下就消失）
        private bool _sysAreaLogged;

        // ---------- 尺寸 ----------
        private double IconSize { get { return _s.IconSize; } }
        private double Pad { get { return Math.Round(_s.IconSize * 0.22); } }
        private double DotH { get { return Math.Round(_s.IconSize * 0.19); } }
        private double Gap { get { return Math.Round(_s.IconSize * 0.15); } }
        private double TileW { get { return IconSize + Gap; } }
        /// <summary>倒影占用的额外高度（关掉时为 0，不改变原布局）。</summary>
        private double ReflectRoom
        {
            get
            {
                if (!_s.ReflectOn || _s.ReflectHeight <= 1 || _s.ReflectStrength <= 0.5) return 0;
                return Math.Round(IconSize * (_s.ReflectHeight / 100.0));
            }
        }
        private double BarH { get { return IconSize + DotH + Pad * 2 + ReflectRoom; } }
        private double MaxScale { get { return _s.Magnify ? _s.MagnifyMax : 1.0; } }
        private double Head { get { return Math.Max(6, Math.Ceiling(IconSize * (MaxScale - 1)) + 4); } }
        private double WinH { get { return BarH + Head + ShadowRoom * 2; } }

        public DockSettings Settings { get { return _s; } }

        /// <summary>Dock 条顶边在屏幕坐标中的 Y（供弹出层定位）。</summary>
        public double BarTopScreen
        {
            get
            {
                return _s.Edge == DockEdge.Bottom
                    ? Top + Height - ShadowRoom - BarH
                    : Top + ShadowRoom;
            }
        }

        public double BarBottomScreen { get { return BarTopScreen + BarH; } }

        public DockWindow(DockSettings settings)
        {
            _s = settings;
            _s.Clamp();
            _pal = Palette.Create(_s);

            Title = AppInfo.Name;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = _s.AlwaysOnTop;   // 默认 false：像桌面小部件一样，被其他窗口覆盖
            ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            BuildUi();

            _hideTimer = new DispatcherTimer();
            _hideTimer.Tick += delegate
            {
                _hideTimer.Stop();
                if (_s.AutoHide && !_inside && !HasOpenPopup()) HideDock();
            };

            // 拖拽反馈的滞留计时器：收不到 DragOver 就自动收起
            _dragHintTimer = new DispatcherTimer();
            _dragHintTimer.Interval = TimeSpan.FromMilliseconds(260);
            _dragHintTimer.Tick += delegate
            {
                _dragHintTimer.Stop();
                HideDropHint();
            };

            // 悬停预览：延时出现 + 盯着鼠标自动收起
            _previewTimer = new DispatcherTimer();
            _previewTimer.Tick += delegate { OnPreviewTick(); };

            _previewWatch = new DispatcherTimer();
            _previewWatch.Tick += delegate { OnPreviewWatch(); };

            _poll = new DispatcherTimer();
            _poll.Interval = TimeSpan.FromMilliseconds(1200);
            _poll.Tick += delegate
            {
                // 资源管理器重启后任务栏会重新出现，这里按设置再压一次
                if (_s.HideTaskbar && TaskbarManager.Exists && !TaskbarManager.IsHidden)
                    TaskbarManager.SetHidden(true);
                // 自愈：我们藏过、但设置上不该藏了（比如被菜单/外部改回来）→ 立刻显示
                else if (!_s.HideTaskbar && TaskbarManager.IsHidden)
                    TaskbarManager.ForceShow();

                RefreshRunning(false);
                WarmupThumbnails();
                TickSystemArea();
            };

            // 系统区数据源：轻量只读查询，任何一项失败都只是少一格
            _status = new SystemStatus();
            _toast = new ToastWatcher();
            _toast.NewNotification += delegate
            {
                // 未读角标 + 一次轻脉冲；不弹自己的 toast
                if (_sysArea != null)
                {
                    _sysArea.ApplyStatus(_status, _toast.Unread, _s);
                    _sysArea.PulseBadge();
                }
            };
            _toast.Cleared += delegate
            {
                if (_sysArea != null) _sysArea.ApplyStatus(_status, _toast.Unread, _s);
            };
            if (Environment.GetEnvironmentVariable("DOCK_TOAST_DEBUG") == "1") _toast.DebugUnread(3);

            Loaded += OnLoaded;
            Closed += delegate
            {
                if (_poll != null) _poll.Stop();
                UnregisterHotkey();
                if (_toast != null) _toast.Dispose();
            };

            // 失去前台焦点后主动沉到窗口栈底，保证任何应用窗口都能盖住 Dock
            Deactivated += delegate { if (!_s.AlwaysOnTop && !_editing) SendToBottom(); };
        }

        // =====================================================================
        // 窗口层级：默认"桌面层"，被其它窗口覆盖；可选始终置顶
        // =====================================================================
        private const int HWND_BOTTOM = 1;
        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_NOOWNERZORDER = 0x0200;

        private void ApplyZOrder()
        {
            try
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                if (h == IntPtr.Zero) return;

                if (_s.AlwaysOnTop)
                {
                    Topmost = true;
                    return;
                }

                Topmost = false;
                SendToBottom();
            }
            catch { }
        }

        /// <summary>沉到所有非置顶窗口之下（桌面之上）。</summary>
        private void SendToBottom()
        {
            try
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                if (h == IntPtr.Zero) return;
                Native.SetWindowPos(h, new IntPtr(HWND_BOTTOM), 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            }
            catch { }
        }

        public void SetAlwaysOnTop(bool on)
        {
            _s.AlwaysOnTop = on;
            SaveSettings();
            ApplyZOrder();
        }

        // =====================================================================
        // 构建
        // =====================================================================
        private void BuildUi()
        {
            _root = new Grid();
            _root.Background = null;   // 透明：鼠标可以穿透到桌面
            Content = _root;

            _stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            _halo = new Border
            {
                VerticalAlignment = VerticalAlignment.Top,
                Height = 1,
                Margin = new Thickness(10, 1, 10, 0),
                Background = new LinearGradientBrush(
                    Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0x50, 255, 255, 255), 0),
                IsHitTestVisible = false
            };

            var content = new Grid();
            content.Children.Add(_stack);
            content.Children.Add(_halo);
            _halo.HorizontalAlignment = HorizontalAlignment.Center;
            _halo.Width = 1;   // 由 SizeChanged 跟随 StackPanel 宽度，避免撑大 Bar

            // 拖入时覆盖在图标上的提示层（配色在 ApplyPalette 里按主题刷新）
            _dropHintText = new TextBlock
            {
                Text = L.T("松手添加到 Dock"),
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };

            var hintRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var plus = new TextBlock
            {
                Text = "+",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, -1, 7, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            hintRow.Children.Add(plus);
            hintRow.Children.Add(_dropHintText);

            _dropHint = new Border
            {
                CornerRadius = new CornerRadius(10),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(3),
                Child = hintRow
            };            content.Children.Add(_dropHint);
            Panel.SetZIndex(_dropHint, 10);

            _bar = new Border
            {
                Child = content,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = _s.Edge == DockEdge.Bottom
                    ? VerticalAlignment.Bottom : VerticalAlignment.Top,
                CornerRadius = new CornerRadius(_pal.CornerRadius),
                BorderThickness = new Thickness(1),
                Background = _pal.BarBackground,
                BorderBrush = _pal.BarBorder,
                Padding = new Thickness(Pad, 0, Pad, 0),
                Height = BarH,
                AllowDrop = true,
                Margin = _s.Edge == DockEdge.Bottom
                    ? new Thickness(0, 0, 0, ShadowRoom)
                    : new Thickness(0, ShadowRoom, 0, 0)
            };

            _bar.MouseEnter += delegate { OnEnter(); };
            _bar.MouseLeave += delegate { OnLeave(); };
            _bar.MouseMove += OnBarMouseMove;
            _bar.MouseRightButtonUp += OnBarRightClick;
            _bar.PreviewMouseLeftButtonDown += OnEditDragStart;
            _bar.PreviewMouseMove += OnEditDragMove;
            _bar.PreviewMouseLeftButtonUp += OnEditDragEnd;
            _bar.DragOver += OnDragOver;
            _bar.Drop += OnDrop;
            _bar.SizeChanged += delegate { SyncHalo(); };
            _stack.SizeChanged += delegate { SyncHalo(); };

            _root.Children.Add(_bar);

            ApplyPalette();
        }

        private void ApplyPalette()
        {
            _pal = Palette.Create(_s);
            if (_bar == null) return;
            UpdateBarSurface();
            _bar.CornerRadius = new CornerRadius(_pal.CornerRadius);
            _bar.VerticalAlignment = _s.Edge == DockEdge.Bottom
                ? VerticalAlignment.Bottom : VerticalAlignment.Top;
            _bar.Margin = _s.Edge == DockEdge.Bottom
                ? new Thickness(0, 0, 0, ShadowRoom) : new Thickness(0, ShadowRoom, 0, 0);
            _halo.Background = new LinearGradientBrush(
                Color.FromArgb(0, 255, 255, 255),
                Color.FromArgb(_pal.IsDark ? (byte)0x55 : (byte)0x66, 255, 255, 255), 0);
            if (_sysArea != null) _sysArea.ApplyPalette(_pal);
            if (_dropHint != null)            {
                // 不描边、不用强调色：卡片底色 + 正文色，和菜单/设置面板同一套中性配色
                _dropHint.Background = _pal.IsDark
                    ? new SolidColorBrush(Color.FromArgb(0xFF, 0x2B, 0x2B, 0x33))
                    : new SolidColorBrush(Color.FromArgb(0xFF, 0xF7, 0xF7, 0xFA));
                _dropHint.BorderBrush = _pal.Divider;
                _dropHint.BorderThickness = new Thickness(1);
                _dropHint.CornerRadius = new CornerRadius(Math.Max(6, Math.Round(_pal.CornerRadius * 0.62)));
                _dropHint.Opacity = 0.97;

                if (_dropHintText != null) _dropHintText.Foreground = _pal.Text;
                var row = _dropHint.Child as Panel;
                if (row != null && row.Children.Count > 0)
                {
                    var plus = row.Children[0] as TextBlock;
                    if (plus != null) plus.Foreground = _pal.TextMuted;
                }
            }

            foreach (var v in _views) v.ApplyPalette(_pal);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            HideFromAltTab();
            if (_status != null) _status.Tick();     // 先取一次状态，胶囊不至于空白一帧
            Rebuild();
            _poll.Start();
            if (_s.AutoHide)
            {
                _hidden = true;
                SetTopImmediate(HiddenTop());
            }
            ApplyZOrder();
            ApplyHotkey();

            // 上次退出时是把任务栏藏起来的，则恢复这个状态
            if (_s.HideTaskbar) TaskbarManager.SetHidden(true);
        }

        // =====================================================================
        // 全局快捷键：切换 Windows 任务栏（可在设置里改）
        // =====================================================================
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_TASKBAR = 0xD0C1;

        private IntPtr _hwnd = IntPtr.Zero;
        private bool _hotkeyOn;
        private bool _hookInstalled;

        /// <summary>按当前设置重新注册快捷键（0 = 未设置则只注销）。</summary>
        public void ApplyHotkey()
        {
            try
            {
                if (_hwnd == IntPtr.Zero) _hwnd = new WindowInteropHelper(this).Handle;
                if (_hwnd == IntPtr.Zero) return;

                if (!_hookInstalled)
                {
                    HwndSource src = HwndSource.FromHwnd(_hwnd);
                    if (src != null) { src.AddHook(HotkeyHook); _hookInstalled = true; }
                }

                UnregisterHotkey();


                // ① 隐藏任务栏
                if (_s.HotkeyVk > 0 && _s.HotkeyMods != 0)
                {
                    _hotkeyOn = Native.RegisterHotKey(_hwnd, HOTKEY_TASKBAR,
                        (uint)_s.HotkeyMods, (uint)_s.HotkeyVk);
                    if (!_hotkeyOn)
                        SettingsStore.Log("热键注册失败（可能被其它程序占用）："
                            + Hotkey.Text(_s.HotkeyMods, _s.HotkeyVk));
                }


            }
            catch (Exception ex)
            {
                SettingsStore.Log("ApplyHotkey: " + ex.Message);
            }
        }

        private void UnregisterHotkey()
        {
            if (_hwnd != IntPtr.Zero)
            {
                if (_hotkeyOn) { try { Native.UnregisterHotKey(_hwnd, HOTKEY_TASKBAR); } catch { } }
            }
            _hotkeyOn = false;
        }

        private IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HOTKEY_TASKBAR) { ToggleTaskbar(); handled = true; }
            }
            return IntPtr.Zero;
        }

        /// <summary>快捷键当前是否注册成功（设置面板用来提示）。</summary>
        public bool IsHotkeyActive { get { return _hotkeyOn; } }


        /// <summary>切换 Windows 任务栏显示/隐藏，并记住状态。</summary>
        public void ToggleTaskbar()
        {
            bool hidden = TaskbarManager.Toggle();
            _s.HideTaskbar = hidden;
            SaveSettings();
        }

        public void SetHideTaskbar(bool hidden)
        {
            TaskbarManager.SetHidden(hidden);
            _s.HideTaskbar = hidden;
            SaveSettings();
        }

        /// <summary>设置隐藏任务栏的全局快捷键（vk = 0 表示取消）。</summary>
        public void SetTaskbarHotkey(int mods, int vk)
        {
            _s.HotkeyMods = mods;
            _s.HotkeyVk = vk;
            SaveSettings();
            ApplyHotkey();
        }

        /// <summary>程序退出时把任务栏还回去，别让用户没任务栏用。</summary>
        public void RestoreTaskbar()
        {
            try
            {
                if (TaskbarManager.Exists && TaskbarManager.IsHidden) TaskbarManager.SetHidden(false);
            }
            catch { }
        }

        private void HideFromAltTab()
        {
            try
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
                ex |= Native.WS_EX_TOOLWINDOW;
                Native.SetWindowLongPtrSafe(h, Native.GWL_EXSTYLE, (IntPtr)ex);
            }
            catch { }
        }

        // =====================================================================
        // 数据
        // =====================================================================
        public void Rebuild()
        {
            var apps = WindowService.Group(WindowService.Snapshot());
            _sig = Signature(apps);
            BuildItems(apps);
            RebuildViews();
            ApplyGeometry();
        }

        private static string Signature(List<RunningApp> apps)
        {
            var parts = new List<string>(apps.Count);
            foreach (var a in apps) parts.Add((a.ExePath ?? "").ToLowerInvariant());
            parts.Sort(StringComparer.Ordinal);
            return string.Join("|", parts.ToArray());
        }

        private void BuildItems(List<RunningApp> apps)
        {
            _items.Clear();
            foreach (var p in _s.Pinned)
            {
                if (string.IsNullOrEmpty(p.Path)) continue;
                var t = new DockTile
                {
                    DisplayName = string.IsNullOrEmpty(p.Name) ? System.IO.Path.GetFileNameWithoutExtension(p.Path) : p.Name,
                    TargetPath = p.Path,
                    LaunchArgs = p.Args,
                    Pinned = true,
                    MatchExe = LaunchService.MatchKey(p)
                };
                t.Icon = IconService.Get(p.Path);
                t.Windows = FindWindows(apps, t.MatchExe);
                _items.Add(t);
            }
            _pinnedCount = _items.Count;

            if (_s.ShowUnpinnedRunning && apps.Count > 0)
            {
                var extras = new List<DockTile>();
                foreach (var a in apps)
                {
                    if (IsPinnedApp(a)) continue;
                    var t = new DockTile
                    {
                        DisplayName = string.IsNullOrEmpty(a.ProcessName) ? a.ExePath : a.ProcessName,
                        TargetPath = a.ExePath,
                        Pinned = false,
                        MatchExe = (a.ExePath ?? "").ToLowerInvariant(),
                        Windows = a.Windows
                    };
                    t.Icon = IconService.Get(a.ExePath);
                    extras.Add(t);
                }
                if (extras.Count > 0)
                {
                    if (_items.Count > 0) _items.Add(new DockTile { IsSeparator = true });
                    _items.AddRange(extras);
                }
            }
        }

        /// <summary>这个运行中的应用是否已固定到 Dock（支持多进程应用：父进程链也算）。</summary>
        private bool IsPinnedApp(RunningApp a)
        {
            if (a == null) return false;
            string exeKey = (a.ExePath ?? "").ToLowerInvariant();

            foreach (var p in _s.Pinned)
            {
                string key = LaunchService.MatchKey(p);
                if (string.IsNullOrEmpty(key)) continue;
                if (string.Equals(key, exeKey, StringComparison.OrdinalIgnoreCase)) return true;
                if (a.Chain != null && a.Chain.Contains(key)) return true;
            }
            return false;
        }

        /// <summary>找出属于某个固定项的所有窗口（自身 exe 或任一祖先 exe 匹配）。</summary>
        private static List<AppWindow> FindWindows(List<RunningApp> apps, string key)
        {
            var list = new List<AppWindow>();
            if (string.IsNullOrEmpty(key)) return list;

            foreach (var a in apps)
            {
                bool selfMatch = string.Equals((a.ExePath ?? "").ToLowerInvariant(), key,
                    StringComparison.OrdinalIgnoreCase);
                bool chainMatch = a.Chain != null && a.Chain.Contains(key);

                if (selfMatch || chainMatch) list.AddRange(a.Windows);
            }
            return list;
        }

        private void RebuildViews()
        {
            CloseFlyout();
            // 注意：**不要**在这里关系统面板。轮询每 1.2s 可能因为应用列表变化触发重建，
            // 一关就会把正在看的"系统托盘"面板闪没（曾经就是这样）。
            _stack.Children.Clear();
            _views.Clear();

            foreach (var t in _items)
            {
                if (t.IsSeparator)
                {
                    _stack.Children.Add(new DockSeparatorView(IconSize, _pal));
                    continue;
                }

                var v = new DockTileView(t, IconSize, _pal, DotH, ReflectRoom) { Width = TileW };
                v.ApplyReflection(_s);
                v.MouseEnter += delegate { v.SetHover(true); OnTileHoverEnter(v); };
                v.MouseLeave += delegate { v.SetHover(false); OnTileHoverLeave(v); };
                v.MouseLeftButtonDown += OnTileDown;
                v.MouseMove += OnTileMove;
                v.MouseLeftButtonUp += OnTileUp;
                v.MouseRightButtonUp += OnTileRightClick;

                // 兜底：鼠标抬起事件可能丢失（比如在别的窗口上松手），别让拖动状态卡住
                var tileRef = v;
                v.LostMouseCapture += delegate
                {
                    if (!ReferenceEquals(_dragView, tileRef)) return;
                    _dragView = null;
                    tileRef.Opacity = 1.0;
                    FinishReorder(tileRef);
                };

                ToolTipService.SetInitialShowDelay(v, 380);
                ToolTipService.SetPlacement(v, System.Windows.Controls.Primitives.PlacementMode.Top);
                ToolTipService.SetShowDuration(v, 20000);
                v.ToolTip = TooltipFor(t);

                _stack.Children.Add(v);
                _views.Add(v);
            }

            BuildSystemArea();      // 末端：铃铛 / 状态胶囊 / 溢出入口
            UpdateHalo();
        }

        private static string TooltipFor(DockTile t)
        {
            if (t.IsRunning)
                return t.Windows.Count > 1
                    ? t.DisplayName + L.T("  ({0} 个窗口)", t.Windows.Count)
                    : t.DisplayName;
            return t.DisplayName;
        }

        private void UpdateHalo()
        {
            // 顶边那根 1px 高光线：**完全透明时必须一起藏掉**，
            // 否则条底都看不见了，却留着一条细细的亮边（就是之前反馈的"透明后的细边框"）。
            bool invisible = _s.BarOpacity <= 0.001;
            _halo.Visibility = (_views.Count == 0 || invisible)
                ? Visibility.Collapsed : Visibility.Visible;
            SyncHalo();
        }

        /// <summary>高光条宽度跟随图标区宽度（不能让它反过来撑大 Bar）。</summary>
        private void SyncHalo()
        {
            if (_halo == null || _stack == null) return;
            double w = Math.Max(0, _stack.ActualWidth - 16);
            if (Math.Abs(_halo.Width - w) > 0.5) _halo.Width = w;
        }

        // =====================================================================
        // 几何
        // =====================================================================
        private void ApplyGeometry()
        {
            double n = _views.Count;
            double areaW = AreaW;
            double maxW = (n * TileW + areaW) * Math.Max(1.0, MaxScale) + Pad * 2 + 2;
            _appliedAreaW = areaW;
            double minW = 140;

            BeginAnimation(Window.TopProperty, null);
            BeginAnimation(Window.LeftProperty, null);

            Width = Math.Max(minW, maxW + ShadowRoom * 2);
            Height = WinH;
            _bar.Height = BarH;
            _lastBarH = BarH;

            Rect wa = SystemParameters.WorkArea;

            if (_editing)
            {
                // 拖动中：位置由鼠标决定，这里只更新尺寸
            }
            else if (_s.Position == DockPosition.Custom)
            {
                ApplyCustomPlacement(wa);
            }
            else
            {
                Left = wa.Left + (wa.Width - Width) / 2.0;
                Top = _hidden ? HiddenTop() : ShownTop();
            }

            ResetMagnify();
        }

        // =====================================================================
        // 自定义位置
        // =====================================================================
        /// <summary>Dock 条中心（屏幕 DIP 坐标）。</summary>
        public double BarCenterX { get { return Left + Width / 2.0; } }
        public double BarCenterY { get { return BarTopScreen + BarH / 2.0; } }

        public bool IsPositionEditing { get { return _editing; } }

        private void ApplyCustomPlacement(Rect wa)
        {
            double cx = wa.Left + _s.PosX * wa.Width;
            double cy = wa.Top + _s.PosY * wa.Height;

            // 条相对窗口顶边的偏移（底部停靠 / 顶部停靠时不同）
            double barTopOffset = BarTopScreen - Top;
            Left = cx - Width / 2.0;
            Top = cy - barTopOffset - BarH / 2.0;
            ClampIntoWorkArea(wa);
        }

        private void ClampIntoWorkArea(Rect wa)
        {
            double minL = wa.Left - ShadowRoom;
            double maxL = wa.Right - Width + ShadowRoom;
            double minT = wa.Top - ShadowRoom;
            double maxT = wa.Bottom - Height + ShadowRoom;

            Left = Math.Min(Math.Max(Left, minL), Math.Max(minL, maxL));
            Top = Math.Min(Math.Max(Top, minT), Math.Max(minT, maxT));
        }

        private void SnapDuringDrag()
        {
            const double Snap = 26;
            Rect wa = SystemParameters.WorkArea;

            // 水平吸中间
            if (Math.Abs(BarCenterX - (wa.Left + wa.Width / 2.0)) < Snap)
                Left = wa.Left + wa.Width / 2.0 - Width / 2.0;

            // 竖向吸顶 / 吸底
            double barTop = BarTopScreen;
            double offset = BarTopScreen - Top;
            double topY = wa.Top + _s.EdgeMargin;
            double bottomY = wa.Bottom - _s.EdgeMargin - BarH;

            if (Math.Abs(barTop - topY) < Snap) Top = topY - offset;
            else if (Math.Abs(barTop - bottomY) < Snap) Top = bottomY - offset;
        }

        /// <summary>进入"自定义位置"拖动模式。</summary>
        public void BeginPositionEdit()
        {
            if (_editing) return;

            _editing = true;
            _origPosition = _s.Position;
            _origPosX = _s.PosX;
            _origPosY = _s.PosY;

            _hideTimer.Stop();
            if (_hidden) ShowDock();
            CloseFlyout();
            ResetMagnify();

            try
            {
                PresentationSource src = PresentationSource.FromVisual(this);
                _dpiScale = (src != null && src.CompositionTarget != null)
                    ? src.CompositionTarget.TransformToDevice.M11 : 1.0;
                if (_dpiScale <= 0) _dpiScale = 1.0;
            }
            catch { _dpiScale = 1.0; }

            Topmost = true;                       // 编辑期间浮到最上层，免得被窗口挡住
            _stack.IsHitTestVisible = false;      // 图标不响应，整条都能拖
            _bar.Cursor = Cursors.SizeAll;
            _bar.BorderBrush = _pal.Accent;       // 高亮边框提示"编辑中"
            _bar.BorderThickness = new Thickness(2);

            // 先把自己切到前台：否则第一次按下会被"激活窗口"吃掉，首拖无效
            try
            {
                Activate();
                IntPtr h = new WindowInteropHelper(this).Handle;
                if (h != IntPtr.Zero) WindowService.Activate(h);
            }
            catch { }

            if (_editorWnd == null)
            {
                _editorWnd = new PositionEditorWindow(this);
                var w = _editorWnd;
                _editorWnd.Closed += delegate
                {
                    // 用户用 Alt+F4 之类关掉 → 视作取消
                    if (ReferenceEquals(_editorWnd, w)) CancelPositionEdit();
                };
                _editorWnd.Show();
            }
            _editorWnd.Follow(this);
        }

        /// <summary>保存当前拖到的位置。</summary>
        public void CommitPositionEdit()
        {
            if (!_editing) return;

            _editing = false;
            EndEditVisuals();

            Rect wa = SystemParameters.WorkArea;
            _s.Position = DockPosition.Custom;
            _s.PosX = Math.Min(1.0, Math.Max(0.0, (BarCenterX - wa.Left) / Math.Max(1.0, wa.Width)));
            _s.PosY = Math.Min(1.0, Math.Max(0.0, (BarCenterY - wa.Top) / Math.Max(1.0, wa.Height)));

            CloseEditorWindow();
            ApplyZOrder();
            SaveSettings();
            ApplyGeometry();
            FlushPendingRebuild();
        }

        /// <summary>放弃本次拖动，回到原来的位置。</summary>
        public void CancelPositionEdit()
        {
            if (!_editing) return;

            _editing = false;
            EndEditVisuals();

            _s.Position = _origPosition;
            _s.PosX = _origPosX;
            _s.PosY = _origPosY;

            CloseEditorWindow();
            ApplyZOrder();
            ApplyGeometry();
            FlushPendingRebuild();
        }

        private void EndEditVisuals()
        {
            _stack.IsHitTestVisible = true;
            _bar.Cursor = Cursors.Arrow;
            _bar.BorderThickness = new Thickness(1);
            _bar.BorderBrush = _pal.BarBorder;
        }

        private void CloseEditorWindow()
        {
            if (_editorWnd == null) return;
            var w = _editorWnd;
            _editorWnd = null;
            try { w.Close(); } catch { }
        }

        // ---- 拖动 ----
        private void OnEditDragStart(object sender, MouseButtonEventArgs e)
        {
            if (!_editing) return;

            Native.POINT pt;
            Native.GetCursorPos(out pt);
            _editStartPx = pt;
            _editStartLeft = Left;
            _editStartTop = Top;
            _editDragging = true;

            _bar.CaptureMouse();
            e.Handled = true;
        }

        private void OnEditDragMove(object sender, MouseEventArgs e)
        {
            if (!_editing || !_editDragging) return;

            Native.POINT pt;
            if (!Native.GetCursorPos(out pt)) return;

            Left = _editStartLeft + (pt.X - _editStartPx.X) / _dpiScale;
            Top = _editStartTop + (pt.Y - _editStartPx.Y) / _dpiScale;

            SnapDuringDrag();
            ClampIntoWorkArea(SystemParameters.WorkArea);
            if (_editorWnd != null) _editorWnd.Follow(this);

            e.Handled = true;
        }

        private void OnEditDragEnd(object sender, MouseButtonEventArgs e)
        {
            if (!_editing || !_editDragging) return;
            _editDragging = false;
            try { _bar.ReleaseMouseCapture(); } catch { }
            e.Handled = true;
        }

        private double ShownTop()
        {
            Rect wa = SystemParameters.WorkArea;
            if (_s.Edge == DockEdge.Bottom)
                return wa.Bottom - _s.EdgeMargin - Height + ShadowRoom;
            return wa.Top + _s.EdgeMargin - ShadowRoom;
        }

        private double HiddenTop()
        {
            Rect wa = SystemParameters.WorkArea;
            if (_s.Edge == DockEdge.Bottom)
                return wa.Bottom - StripVisible - Head - ShadowRoom;
            return wa.Top - BarH + StripVisible - ShadowRoom;
        }

        private void SetTopImmediate(double top)
        {
            BeginAnimation(Window.TopProperty, null);
            Top = top;
        }

        private void AnimateTop(double target, int ms)
        {
            var anim = new DoubleAnimation(Top, target, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            BeginAnimation(Window.TopProperty, anim, HandoffBehavior.SnapshotAndReplace);
        }

        // =====================================================================
        // 自动隐藏
        // =====================================================================
        /// <summary>
        /// 条的外观。特别处理"完全透明"（不透明度 0%）：
        /// 平时一点底都没有（只留 Transparent 保证可点击、可接收拖放），
        /// 鼠标进入时临时显示 1px 细轮廓 + 很淡的一层底色，避免"看不见也找不到"。
        /// </summary>
        private void UpdateBarSurface()
        {
            if (_bar == null || _pal == null) return;

            bool invisible = _s.BarOpacity <= 0.001;
            if (_halo != null)
                _halo.Visibility = (_views.Count == 0 || invisible) ? Visibility.Collapsed : Visibility.Visible;
            if (!invisible)
            {
                _bar.Background = _pal.BarBackground;
                _bar.BorderBrush = _pal.BarBorder;
                _bar.BorderThickness = new Thickness(1);
                return;
            }

            // 完全透明：平时只有一层"不可见但可命中"的画笔
            if (_inside || _hoverReveal)
            {
                // 悬停显形：一层很淡的底色 + 1px 细轮廓。
                // 太淡会"看不见"，太重会破坏全透明的意思，这里取 24%/40% 左右。
                _bar.Background = new SolidColorBrush(_pal.IsDark
                    ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)
                    : Color.FromArgb(0x1E, 0x00, 0x00, 0x00));
                _bar.BorderBrush = new SolidColorBrush(_pal.IsDark
                    ? Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)
                    : Color.FromArgb(0x44, 0x00, 0x00, 0x00));
                _bar.BorderThickness = new Thickness(1);
            }
            else
            {
                // 关键：不能是 alpha=0。分层窗口是**按像素命中**的，全透明像素会把鼠标透传下去，
                // 于是"完全透明"就变成点不到、也 hover 不到。这里用 alpha=1（视觉上等于全透明），
                // 既保留可发现/可点击，又看不出来。
                _bar.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
                _bar.BorderBrush = Brushes.Transparent;
                _bar.BorderThickness = new Thickness(1);
            }
        }

        private void OnEnter()
        {
            _inside = true;
            _hoverReveal = true;
            UpdateBarSurface();
            _hideTimer.Stop();
            if (_hidden) ShowDock();
        }

        private void OnLeave()
        {
            _inside = false;
            _hoverReveal = false;
            UpdateBarSurface();
            ResetMagnify();          // 鼠标离开必须复员，否则图标会停在放大状态
            if (!_s.AutoHide) return;
            _hideTimer.Stop();
            _hideTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(120, _s.AutoHideDelayMs));
            _hideTimer.Start();
        }

        private void ShowDock()
        {
            if (!_hidden) return;
            _hidden = false;
            AnimateTop(ShownTop(), 170);
        }

        private void HideDock()
        {
            if (_hidden) return;
            if (!_s.AutoHide) return;
            if (_editing) return;                                  // 编辑位置时不收起
            if (_s.Position == DockPosition.Custom) return;         // 自定义位置时自动隐藏没意义
            CloseSystemPanel();                                    // 面板不能悬在被收起的 Dock 外面
            _hidden = true;
            ResetMagnify();
            AnimateTop(HiddenTop(), 230);
        }

        private bool HasOpenPopup()
        {
            if (_flyout != null && _flyout.IsVisible) return true;
            if (_sysPanel != null && _sysPanel.IsVisible) return true;
            return false;
        }

        // =====================================================================
        // 系统区（三段式：铃铛 / 状态胶囊 / 溢出入口 ^）
        // =====================================================================
        /// <summary>Dock 末尾系统区的宽度（关闭时为 0，Geometry 用它算窗口宽度）。</summary>
        private double AreaW
        {
            get
            {
                if (!_s.SystemArea || _sysArea == null) return 0;
                return _sysArea.BaseWidth + TileW * 0.18;   // 末尾多留一点呼吸间距
            }
        }

        /// <summary>把系统区挂到图标栈末尾。图标重排、主题切换后都会重建。</summary>
        private void BuildSystemArea()
        {
            _sysArea = null;
            if (!_s.SystemArea) return;

            var area = new SystemAreaView(_pal, IconSize);
            area.VerticalAlignment = VerticalAlignment.Center;
            area.Margin = new Thickness(TileW * 0.09, 0, 0, 0);

            area.Clicked += delegate (SystemSegment seg) { OnSystemSegmentClick(seg, area); };
            area.MenuRequested += delegate (SystemSegment seg) { ShowSystemAreaMenu(area); };

            // 顺序很重要：先填数据（时间/电量/角标文字会撑宽胶囊），
            // 再定溢出入口显隐，最后入栈。反过来的话 BaseWidth 会量到偏小的值并被缓存。
            area.ApplyStatus(_status, _toast != null ? _toast.Unread : 0, _s);
            area.SetOverflowVisible(_s.TrayOverflow);
            _stack.Children.Add(area);
            _sysArea = area;

            // 只在第一次构建时记一行（发布版日志要干净），宽度异常才值得排查
            if (!_sysAreaLogged)
            {
                _sysAreaLogged = true;
                SettingsStore.Log(string.Format("SystemArea: baseW={0:F0} overflow={1} icon={2}",
                    area.BaseWidth, _s.TrayOverflow, IconSize));
            }
        }

        private void TickSystemArea()
        {
            if (_status != null) _status.Tick();
            if (_toast != null) _toast.PollFallback();

            if (_sysArea == null) return;

            // 全屏游戏/视频：系统区跟着 Dock 一起让路（默认开）。
            // 去抖：连续 3 次（约 3.6 秒）判定全屏才真正隐藏，恢复则立即生效——
            // 宁可晚一点藏，也绝不因为一次误判把系统区藏掉。
            // 只有 Dock 会"浮在别人上面"时，隐藏系统区才有意义：
            //  · 始终置顶 / 自动隐藏 → Dock 会出现在全屏应用之上，必须让路；
            //  · 桌面层（默认）→ 全屏应用本来就把 Dock 整条盖住了，藏系统区没有收益，
            //    却要承担"误判导致系统区消失"的风险，所以这里不藏。
            bool needHide = _s.AlwaysOnTop || _s.AutoHide;
            bool fullNow = _s.FullscreenHide && needHide && SystemStatus.FullscreenBusy();
            _fullStreak = fullNow ? Math.Min(_fullStreak + 1, 99) : 0;
            bool full = _fullStreak >= 3;
            if (full != _sysDimmed)
            {
                _sysDimmed = full;
                _sysArea.SetDimmed(full);
                _sysArea.IsHitTestVisible = !full;
                _sysArea.Visibility = full ? Visibility.Collapsed : Visibility.Visible;
                if (full) CloseSystemPanel();
                ApplyGeometry();          // 让 Dock 宽度跟着收/放
                SettingsStore.Log("SystemArea dimmed=" + full + " areaW=" + AreaW.ToString("F0") + " quns=" + SystemStatus.LastFullscreenRaw + " fg=" + SystemStatus.LastProbe);
            }
            else if (!_sysDimmed && !_sysArea.IsVisible)
            {
                // 自愈：没在全屏、却处于隐藏状态（比如上次收起后没恢复）→ 拉回来
                _sysArea.SetDimmed(false);
                _sysArea.IsHitTestVisible = true;
                _sysArea.Visibility = Visibility.Visible;
                ApplyGeometry();
                SettingsStore.Log("SystemArea self-heal: visible restored, areaW=" + AreaW.ToString("F0"));
            }

            // 自愈：布局宽度和实际系统区宽度不一致（窗口可能被算窄并把系统区裁掉）
            double want = AreaW;
            if (!_sysDimmed && want > 0 && Math.Abs(want - _appliedAreaW) > 1.5)
            {
                ApplyGeometry();
                SettingsStore.Log(string.Format("SystemArea geometry heal: want={0:F0} applied={1:F0}",
                    want, _appliedAreaW));
            }

            _sysArea.ApplyStatus(_status, _toast != null ? _toast.Unread : 0, _s);
        }

        private void OnSystemSegmentClick(SystemSegment seg, FrameworkElement anchor)
        {
            SettingsStore.Log("SystemArea click: " + seg + " dimmed=" + _sysDimmed);
            if (_sysDimmed) return;

            switch (seg)
            {
                case SystemSegment.Bell:
                    if (_s.PanelFirst) { ToggleSystemPanel(SystemPanelMode.Notify, anchor); break; }
                    // 默认：一次点击直达 Windows 通知中心；打不开才退到我们的面板
                    if (_toast != null) _toast.MarkRead();
                    if (!ShellActions.NotificationCenter()) OpenSystemPanel(SystemPanelMode.Notify, anchor);
                    break;

                case SystemSegment.Capsule:
                    if (_s.PanelFirst) { ToggleSystemPanel(SystemPanelMode.Quick, anchor); break; }
                    if (!ShellActions.QuickSettings()) OpenSystemPanel(SystemPanelMode.Quick, anchor);
                    break;

                default:
                    // 溢出入口：我们自己的面板（第三方图标永远不铺到 Dock 上）
                    ToggleSystemPanel(SystemPanelMode.Tray, anchor);
                    break;
            }
        }

        private void ToggleSystemPanel(SystemPanelMode mode, FrameworkElement anchor)
        {
            if (_sysPanel != null && _sysPanel.IsVisible)
            {
                CloseSystemPanel();
                return;
            }
            OpenSystemPanel(mode, anchor);
        }

        /// <summary>给面板的「刷新」用：读一遍托盘列表后重新开面板。</summary>
        public void OpenSystemPanelPublic(SystemPanelMode mode, FrameworkElement anchor)
        {
            OpenSystemPanel(mode, anchor);
        }

        private void OpenSystemPanel(SystemPanelMode mode, FrameworkElement anchor)
        {
            CloseSystemPanel();
            try
            {
                var p = new SystemPanel(this, _pal, mode, _status, _toast != null ? _toast.Unread : 0);
                p.Closed += delegate { if (ReferenceEquals(_sysPanel, p)) _sysPanel = null; };
                _sysPanel = p;
                p.ShowNear(this, anchor);
            }
            catch (Exception ex)
            {
                SettingsStore.Log("OpenSystemPanel: " + ex.Message);
                _sysPanel = null;
            }
        }

        public void CloseSystemPanel()
        {
            try
            {
                if (_sysPanel != null && _sysPanel.IsVisible) _sysPanel.Close();
            }
            catch { }
            _sysPanel = null;
        }

        /// <summary>系统区的右键菜单：只放最常用几条，不做系统托盘那种层叠菜单。</summary>
        private void ShowSystemAreaMenu(FrameworkElement anchor)
        {
            var m = NewMenu();
            m.Items.Add(Item(L.T("打开快速设置（Win + A）"), delegate { ShellActions.QuickSettings(); }));
            m.Items.Add(Item(L.T("打开通知中心（Win + N）"), delegate { ShellActions.NotificationCenter(); }));
            m.Items.Add(new Separator());
            m.Items.Add(Item(L.T("通知设置…"), delegate { ShellActions.NotificationSettings(); }));
            m.Items.Add(Item(L.T("任务栏设置…"), delegate { ShellActions.TaskbarSettings(); }));
            m.Items.Add(Item(L.T("修复：重启资源管理器（重建任务栏）"), delegate { TaskbarManager.RepairShell(); }));
            m.Items.Add(Check(L.T("显示时间"), _s.SystemCapsuleTime, delegate (bool on)
            {
                _s.SystemCapsuleTime = on; SaveSettings();
                if (_sysArea != null) _sysArea.ApplyStatus(_status, _toast != null ? _toast.Unread : 0, _s);
                ApplyGeometry();
            }));
            m.Items.Add(Check(L.T("通知未读角标"), _s.SystemBadge, delegate (bool on)
            {
                _s.SystemBadge = on; SaveSettings();
                if (_sysArea != null) _sysArea.ApplyStatus(_status, _toast != null ? _toast.Unread : 0, _s);
            }));
            m.Items.Add(new Separator());
            m.Items.Add(Item(L.T("隐藏系统区"), delegate
            {
                _s.SystemArea = false; SaveSettings(); Rebuild();
            }));
            // 只做"显示"，不给"点了反而藏起来"的机会：这一项以前是勾选框，
            // 在 Windows 任务栏自动隐藏时会显示成"已勾选"，点一下就把任务栏真藏了。
            m.Items.Add(Item(L.T("显示 Windows 任务栏"), delegate
            {
                TaskbarManager.ForceShow();
                _s.HideTaskbar = false;
                SaveSettings();
            }));


            MenuTheme.ApplyTo(m, _pal);
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            m.PlacementTarget = anchor;
            m.IsOpen = true;
        }


        public void ToggleVisibility()
        {
            if (_s.AutoHide)
            {
                if (_hidden) ShowDock(); else HideDock();
            }
            else
            {
                Visibility = Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
                if (Visibility == Visibility.Visible) ApplyZOrder();
            }
        }

        // =====================================================================
        // 放大效果
        // =====================================================================
        private void OnBarMouseMove(object sender, MouseEventArgs e)
        {
            if (_editing) return;
            if (!_s.Magnify || _views.Count == 0) return;
            if (_dragging) return;

            double x = e.GetPosition(_bar).X;
            double radius = Math.Max(IconSize * 1.7, IconSize * 2.6);
            double amp = Math.Max(0, MaxScale - 1);

            foreach (var v in _views)
            {
                Point c = v.TranslatePoint(new Point(v.ActualWidth / 2.0, 0), _bar);
                double d = Math.Abs(x - c.X);
                double t = 1.0 - d / radius;
                if (t < 0) t = 0;
                double eased = t * t * (3 - 2 * t);
                double scale = 1 + amp * eased;
                v.SetScale(scale);
                v.Width = TileW * scale;
            }

            // 系统区按同一套规则放大（宽度联动，让两侧一起让位）
            if (_sysArea != null && _sysArea.Visibility == Visibility.Visible)
            {
                Point c = _sysArea.TranslatePoint(new Point(_sysArea.ActualWidth / 2.0, 0), _bar);
                double d = Math.Abs(x - c.X);
                double t = 1.0 - d / radius;
                if (t < 0) t = 0;
                double eased = t * t * (3 - 2 * t);
                _sysArea.SetScale(1 + amp * eased * 0.75);   // 系统区只轻微放大，别抢图标戏
            }
        }

        private void ResetMagnify()
        {
            foreach (var v in _views)
            {
                v.SetScale(1);
                v.Width = TileW;
            }
            if (_sysArea != null) _sysArea.SetScale(1);
        }

        // =====================================================================
        // 图标交互
        // =====================================================================
        private void OnTileDown(object sender, MouseButtonEventArgs e)
        {
            var v = sender as DockTileView;
            if (v == null) return;
            _dragView = v;
            _dragging = false;
            _dragStart = e.GetPosition(_bar);
            v.CaptureMouse();
            e.Handled = true;
        }

        private void OnTileMove(object sender, MouseEventArgs e)
        {
            if (_dragView == null) return;
            Point p = e.GetPosition(_bar);

            if (!_dragging)
            {
                if (Math.Abs(p.X - _dragStart.X) < 7) return;
                _dragging = true;

                ResetMagnify();
                _stack.UpdateLayout();          // 复位放大后先刷新布局，避免用旧坐标算目标

                // 拖动期间所有格子等宽、总宽不变，所以槽位网格是固定的：
                // 用固定网格换算目标下标，避免"换位→中心变化→又换回来"的死循环抖动
                _dragSlotOrigin = _views[0].TranslatePoint(new Point(0, 0), _bar).X - _views[0].ShiftX;
                _dragTileWidth = TileW;

                _dragView.SetLift(true);
                Panel.SetZIndex(_dragView, 100);
                _bar.Cursor = Cursors.SizeAll;
            }

            int cur = _views.IndexOf(_dragView);
            if (cur < 0) return;

            int last = Math.Min(_pinnedCount, _views.Count) - 1;
            if (last < 0) return;

            int slot = (int)Math.Floor((p.X - _dragSlotOrigin) / Math.Max(1.0, _dragTileWidth));
            if (slot < 0) slot = 0;
            if (slot > last) slot = last;

            if (slot != cur) MoveViewAnimated(cur, slot);
        }

        private void OnTileUp(object sender, MouseButtonEventArgs e)
        {
            var v = _dragView;
            if (v == null) return;
            _dragView = null;
            try { v.ReleaseMouseCapture(); } catch { }
            v.Opacity = 1.0;

            if (_dragging) FinishReorder(v);
            else if (v.Model != null) ActivateTile(v.Model, v);

            e.Handled = true;
        }

        /// <summary>拖动结束：收尾视觉状态并落盘（鼠标抬起和捕获丢失都走这里）。</summary>
        private void FinishReorder(DockTileView v)
        {
            _dragging = false;
            if (v != null)
            {
                v.SetLift(false);
                v.ResetShift();
                Panel.SetZIndex(v, 0);
            }
            _bar.Cursor = Cursors.Arrow;
            PersistOrder();
            FlushPendingRebuild();
        }

        /// <summary>把拖动期间跳过的刷新补上。</summary>
        private void FlushPendingRebuild()
        {
            if (!_rebuildPending) return;
            _rebuildPending = false;
            RefreshRunning(true);
        }

        /// <summary>带动画的换位：其它图标从旧位置平滑滑到新位置，被抓起的那个直接跟手。</summary>
        private void MoveViewAnimated(int from, int to)
        {
            if (from == to) return;

            // 记录"视觉位置"和当前位移量，换位后用 视觉位置 - 位移 = 布局位置 换算
            var visual = new Dictionary<DockTileView, double>();
            var shift = new Dictionary<DockTileView, double>();
            for (int i = 0; i < _views.Count; i++)
            {
                var tv = _views[i];
                visual[tv] = tv.TranslatePoint(new Point(0, 0), _bar).X;
                shift[tv] = tv.ShiftX;
            }

            MoveView(from, to);
            _stack.UpdateLayout();              // 立刻拿到新坐标，避免连续事件来回抖

            for (int i = 0; i < _views.Count; i++)
            {
                var v = _views[i];
                if (ReferenceEquals(v, _dragView)) continue;   // 抓着的那个跟手，不做让位动画

                double layoutNow = v.TranslatePoint(new Point(0, 0), _bar).X - shift[v];
                double dx;
                if (!visual.TryGetValue(v, out dx)) continue;
                v.SlideFrom(dx - layoutNow);    // 从旧视觉位置滑到新布局位置
            }
        }

        private void MoveView(int from, int to)
        {
            if (from == to || from < 0 || to < 0 || from >= _views.Count || to >= _views.Count) return;

            var v = _views[from];
            _views.RemoveAt(from);
            _views.Insert(to, v);

            // 注意：换位后 _views[to] 就是 v 自己，不能拿它当插入参照点。
            // 正确做法是找到"在 _views 里排在 v 后面"的第一个图标，插到它前面。
            _stack.Children.Remove(v);
            int newIdx = _views.IndexOf(v);
            int childIdx = -1;
            for (int i = newIdx + 1; i < _views.Count; i++)
            {
                childIdx = _stack.Children.IndexOf(_views[i]);
                if (childIdx >= 0) break;
            }
            if (childIdx >= 0) _stack.Children.Insert(childIdx, v);
            else _stack.Children.Add(v);
        }

        private void PersistOrder()
        {
            var list = new List<PinnedEntry>();
            for (int i = 0; i < _pinnedCount && i < _views.Count; i++)
            {
                var m = _views[i].Model;
                if (m == null) continue;
                list.Add(new PinnedEntry { Path = m.TargetPath, Name = m.DisplayName, Args = m.LaunchArgs });
            }
            if (list.Count != _s.Pinned.Count) return;   // 状态异常，别写坏配置
            _s.Pinned = list;
            SaveSettings();
        }

        private void ActivateTile(DockTile t, FrameworkElement anchor)
        {
            CloseFlyout();
            if (t.Windows != null && t.Windows.Count > 0)
            {
                if (t.Windows.Count == 1)
                {
                    IntPtr h = t.Windows[0].Handle;
                    if (WindowService.IsForeground(h)) WindowService.Minimize(h);
                    else WindowService.Activate(h);
                    DelayedRefresh(300);
                }
                else
                {
                    ShowFlyout(t, anchor);
                }
            }
            else
            {
                LaunchService.Launch(t.TargetPath, t.LaunchArgs);
                DelayedRefresh(900);
            }
        }

        // =====================================================================
        // 悬停窗口预览
        // =====================================================================
        private void OnTileHoverEnter(DockTileView v)
        {
            if (_dragging || _editing) return;
            if (v.Model == null || !v.Model.IsRunning) return;

            _previewTile = v;
            _previewTimer.Stop();
            _previewTimer.Interval = TimeSpan.FromMilliseconds(430);
            _previewTimer.Start();
        }

        private void OnTileHoverLeave(DockTileView v)
        {
            if (ReferenceEquals(_previewTile, v)) _previewTimer.Stop();
        }

        private void OnPreviewTick()
        {
            _previewTimer.Stop();

            DockTileView v = _previewTile;
            if (v == null || _dragging || _editing) return;
            if (!v.IsMouseOver) return;
            if (v.Model == null || !v.Model.IsRunning) return;

            if (_flyout != null && _flyout.IsVisible) CloseFlyout();
            ShowFlyout(v.Model, v);
        }

        /// <summary>预览打开后盯着鼠标：离开 Dock 和预览就收起。</summary>
        private void OnPreviewWatch()
        {
            if (_flyout == null || !_flyout.IsVisible)
            {
                _previewWatch.Stop();
                _previewOutside = 0;
                return;
            }

            bool overTile = _previewTile != null && _previewTile.IsMouseOver;
            bool overBar = _bar != null && _bar.IsMouseOver;
            bool overFlyout = _flyout.IsMouseOver;

            if (overTile || overBar || overFlyout)
            {
                _previewOutside = 0;
                return;
            }

            // 从图标移到预览要跨过一条空隙，连续两次都不在范围内才收起
            _previewOutside++;
            if (_previewOutside >= 2)
            {
                _previewOutside = 0;
                CloseFlyout();
                _previewWatch.Stop();
            }
        }

        /// <summary>后台轮换给运行中的窗口抓缩略图，最小化后才有"最后一帧"可放。</summary>
        private void WarmupThumbnails()
        {
            if (_views.Count == 0) return;
            _warmList.Clear();
            for (int i = 0; i < _views.Count; i++)
            {
                var m = _views[i].Model;
                if (m == null || m.Windows == null) continue;
                for (int j = 0; j < m.Windows.Count; j++) _warmList.Add(m.Windows[j].Handle);
            }
            ThumbnailCache.Warmup(_warmList);
        }

        private void StartPreviewWatch()
        {
            _previewWatch.Stop();
            _previewWatch.Interval = TimeSpan.FromMilliseconds(120);
            _previewWatch.Start();
        }

        private void DelayedRefresh(int ms)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += delegate { t.Stop(); RefreshRunning(true); };
            t.Start();
        }

        // =====================================================================
        // 右键菜单
        // =====================================================================
        private ContextMenu NewMenu()
        {
            return new ContextMenu();   // 样式在打开前由 MenuTheme.ApplyTo 递归套上
        }

        private MenuItem Item(string header, Action action, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled, Foreground = _pal.Text };
            if (action != null) mi.Click += delegate { action(); };
            return mi;
        }

        private MenuItem Check(string header, bool isChecked, Action<bool> action)
        {
            var mi = new MenuItem
            {
                Header = header,
                IsCheckable = true,
                IsChecked = isChecked,
                Foreground = _pal.Text
            };
            mi.Click += delegate { action(mi.IsChecked); };
            return mi;
        }

        private void OnTileRightClick(object sender, MouseButtonEventArgs e)
        {
            var v = sender as DockTileView;
            if (v == null || v.Model == null) return;
            var t = v.Model;

            var m = NewMenu();

            if (t.IsRunning)
            {
                foreach (var w in t.Windows)
                {
                    if (t.Windows.Count == 1) break;
                    var win = w;
                    string title = win.Title ?? "";
                    if (title.Length > 42) title = title.Substring(0, 42) + "…";
                    m.Items.Add(Item(L.T("切换到： {0}", title), delegate { WindowService.Activate(win.Handle); }));
                }
                if (t.Windows.Count > 1) m.Items.Add(new Separator());

                if (t.Windows.Count == 1)
                    m.Items.Add(Item(L.T("最小化 / 还原"), delegate
                    {
                        IntPtr h = t.Windows[0].Handle;
                        if (WindowService.IsForeground(h)) WindowService.Minimize(h);
                        else WindowService.Activate(h);
                    }));

                m.Items.Add(Item(L.T("关闭窗口"), delegate
                {
                    foreach (var w in new List<AppWindow>(t.Windows)) WindowService.Close(w.Handle);
                    DelayedRefresh(500);
                }));
                m.Items.Add(new Separator());
            }

            if (!t.IsRunning && !t.Pinned)
                m.Items.Add(Item(L.T("打开"), delegate { LaunchService.Launch(t.TargetPath, t.LaunchArgs); DelayedRefresh(900); }));

            if (t.Pinned)
            {
                m.Items.Add(Item(L.T("从 Dock 移除"), delegate
                {
                    _s.Pinned.RemoveAll(delegate (PinnedEntry p)
                    {
                        return string.Equals(LaunchService.MatchKey(p), t.MatchExe, StringComparison.OrdinalIgnoreCase);
                    });
                    SaveSettings();
                    RefreshRunning(true);
                }));
            }
            else
            {
                m.Items.Add(Item(L.T("固定到 Dock"), delegate
                {
                    _s.Pinned.Add(new PinnedEntry { Path = t.TargetPath, Name = t.DisplayName, Args = t.LaunchArgs });
                    SaveSettings();
                    RefreshRunning(true);
                }));
            }

            m.Items.Add(Item(L.T("打开文件所在位置"), delegate { LaunchService.ShowInExplorer(t.TargetPath); }));
            m.Items.Add(new Separator());
            m.Items.Add(Item(L.T("Dock 设置…"), OpenSettings));

            MenuTheme.ApplyTo(m, _pal);
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            m.PlacementTarget = v;
            m.IsOpen = true;
            e.Handled = true;
        }

        private void OnBarRightClick(object sender, MouseButtonEventArgs e)
        {
            var m = NewMenu();

            m.Items.Add(Item(L.T("添加应用…"), AddAppsDialog));
            m.Items.Add(Item(L.T("Dock 设置…"), OpenSettings));
            m.Items.Add(new Separator());
            m.Items.Add(Check(L.T("自动隐藏"), _s.AutoHide, delegate (bool on)
            {
                _s.AutoHide = on;
                SaveSettings();
                if (!on) ShowDock();
            }));
            m.Items.Add(Check(L.T("悬停放大"), _s.Magnify, delegate (bool on)
            {
                _s.Magnify = on;
                SaveSettings();
                ApplyGeometry();
            }));
            m.Items.Add(Check(L.T("显示未固定的运行应用"), _s.ShowUnpinnedRunning, delegate (bool on)
            {
                _s.ShowUnpinnedRunning = on;
                SaveSettings();
                RefreshRunning(true);
            }));

            m.Items.Add(Check(L.T("始终置顶显示（会盖住其他窗口）"), _s.AlwaysOnTop, delegate (bool on)
            {
                SetAlwaysOnTop(on);
            }));

            string hk = _s.HotkeyVk > 0 ? "（" + Hotkey.Text(_s.HotkeyMods, _s.HotkeyVk) + "）" : "";
            m.Items.Add(Check(L.T("隐藏 Windows 任务栏") + hk, TaskbarManager.IsHidden, delegate (bool on)
            {
                SetHideTaskbar(on);
            }));

            var pos = new MenuItem { Header = L.T("停靠位置"), Foreground = _pal.Text };
            pos.Items.Add(Check(L.T("屏幕底部"), _s.Edge == DockEdge.Bottom && _s.Position == DockPosition.Edge, delegate (bool on)
            {
                if (!on) return;
                _s.Position = DockPosition.Edge;
                _s.Edge = DockEdge.Bottom; SaveSettings(); ApplyPalette(); ApplyGeometry();
            }));
            pos.Items.Add(Check(L.T("屏幕顶部"), _s.Edge == DockEdge.Top && _s.Position == DockPosition.Edge, delegate (bool on)
            {
                if (!on) return;
                _s.Position = DockPosition.Edge;
                _s.Edge = DockEdge.Top; SaveSettings(); ApplyPalette(); ApplyGeometry();
            }));
            pos.Items.Add(new Separator());
            pos.Items.Add(Check(L.T("自定义位置（拖动调整）…"), _s.Position == DockPosition.Custom,
                delegate (bool on) { BeginPositionEdit(); }));
            m.Items.Add(pos);

            var theme = new MenuItem { Header = L.T("主题"), Foreground = _pal.Text };
            theme.Items.Add(Check(L.T("跟随系统"), _s.Theme == DockTheme.Auto, delegate (bool on) { if (!on) return; _s.Theme = DockTheme.Auto; SaveSettings(); ApplyPalette(); }));
            theme.Items.Add(Check(L.T("深色"), _s.Theme == DockTheme.Dark, delegate (bool on) { if (!on) return; _s.Theme = DockTheme.Dark; SaveSettings(); ApplyPalette(); }));
            theme.Items.Add(Check(L.T("浅色"), _s.Theme == DockTheme.Light, delegate (bool on) { if (!on) return; _s.Theme = DockTheme.Light; SaveSettings(); ApplyPalette(); }));
            m.Items.Add(theme);

            m.Items.Add(Check(L.T("开机自动启动"), _s.LaunchAtStartup, delegate (bool on)
            {
                _s.LaunchAtStartup = on;
                StartupManager.SetEnabled(on);
                SaveSettings();
            }));

            m.Items.Add(new Separator());
            m.Items.Add(Item(L.T("退出 Dock"), delegate { App.Quit(); }));

            MenuTheme.ApplyTo(m, _pal);
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            m.PlacementTarget = _bar;
            m.IsOpen = true;
            e.Handled = true;
        }

        public void AddAppsDialog()
        {
            var dlg = new OpenFileDialog
            {
                Title = L.T("选择要固定到 Dock 的应用"),
                Filter = L.T("应用程序 (*.exe;*.lnk;*.bat)|*.exe;*.lnk;*.bat|所有文件 (*.*)|*.*"),
                Multiselect = true,
                CheckFileExists = true
            };
            bool? ok = dlg.ShowDialog(this);
            if (ok != true) return;

            foreach (string f in dlg.FileNames) AddPin(f);
            RefreshRunning(true);
        }

        /// <summary>固定一个应用；返回是否真的新加（已存在则返回 false）。</summary>
        public bool AddPin(string file)
        {
            var e = LaunchService.FromFile(file);
            if (e == null) return false;
            foreach (var p in _s.Pinned)
                if (string.Equals(LaunchService.MatchKey(p), LaunchService.MatchKey(e), StringComparison.OrdinalIgnoreCase))
                    return false;
            _s.Pinned.Add(e);
            SaveSettings();
            return true;
        }

        // =====================================================================
        // 拖入添加应用
        // =====================================================================
        private const string ShellIdListFormat = "Shell IDList Array";

        private static bool CanAccept(IDataObject data)
        {
            if (data == null) return false;
            if (data.GetDataPresent(DataFormats.FileDrop)) return true;
            return data.GetDataPresent(ShellIdListFormat);
        }

        /// <summary>拖拽经过 Dock 时给出明确反馈（用滞留计时器避免子元素切换导致闪烁）。</summary>
        private void OnDragOver(object sender, DragEventArgs e)
        {
            bool ok = CanAccept(e.Data);
            e.Effects = ok ? DragDropEffects.Link : DragDropEffects.None;
            if (ok)
            {
                ShowDropHint(L.T("松手添加到 Dock"));
                _dragHintTimer.Stop();
                _dragHintTimer.Start();
            }
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            HideDropHint();

            var files = new List<string>();

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var arr = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (arr != null) files.AddRange(arr);
            }

            // 从开始菜单 / 任务栏拖出来的不是 FileDrop，而是 Shell IDList
            if (files.Count == 0) files.AddRange(GetShellIdListPaths(e.Data));

            SettingsStore.Log("Drop: " + files.Count + " item(s) -> " + string.Join(" | ", files.ToArray()).Trim());
            int added = 0;
            foreach (string f in files)
            {
                if (!IsAppFile(f)) continue;
                if (AddPin(f)) added++;
            }

            if (added > 0)
            {
                RefreshRunning(true);
                ShowDropHint(added == 1 ? L.T("已添加到 Dock") : L.T("已添加 {0} 个应用", added));
                _dragHintTimer.Stop();
                _dragHintTimer.Start();
            }
            else if (files.Count > 0)
            {
                ShowDropHint(L.T("只能添加 exe / lnk / bat / cmd / url"));
                _dragHintTimer.Stop();
                _dragHintTimer.Start();
            }
            e.Handled = true;
        }

        private static bool IsAppFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string low = path.Trim().Trim('"').ToLowerInvariant();
            if (!(low.EndsWith(".exe") || low.EndsWith(".lnk") || low.EndsWith(".bat")
                  || low.EndsWith(".cmd") || low.EndsWith(".url"))) return false;
            try { return System.IO.File.Exists(path.Trim().Trim('"')); }
            catch { return false; }
        }

        /// <summary>解析 CFSTR_SHELLIDLIST（开始的菜单、任务栏、资源管理器里的位置）。</summary>
        private static string[] GetShellIdListPaths(IDataObject data)
        {
            var result = new List<string>();
            if (data == null || !data.GetDataPresent(ShellIdListFormat)) return result.ToArray();

            IntPtr p = IntPtr.Zero;
            try
            {
                byte[] buffer = null;

                var stream = data.GetData(ShellIdListFormat) as System.IO.MemoryStream;
                if (stream != null) buffer = stream.ToArray();

                if (buffer == null || buffer.Length < 8) return result.ToArray();

                p = System.Runtime.InteropServices.Marshal.AllocHGlobal(buffer.Length);
                System.Runtime.InteropServices.Marshal.Copy(buffer, 0, p, buffer.Length);

                int count = System.Runtime.InteropServices.Marshal.ReadInt32(p, 0);
                if (count <= 0 || count > 256) return result.ToArray();

                for (int i = 0; i < count; i++)
                {
                    int offset = System.Runtime.InteropServices.Marshal.ReadInt32(p, 4 + 4 * i);
                    if (offset <= 0 || offset >= buffer.Length) continue;

                    var sb = new System.Text.StringBuilder(1024);
                    if (Native.SHGetPathFromIDList(new IntPtr(p.ToInt64() + offset), sb))
                    {
                        string path = sb.ToString();
                        if (path.Length > 0 && !result.Contains(path)) result.Add(path);
                    }
                }
            }
            catch (Exception ex)
            {
                SettingsStore.Log("GetShellIdListPaths: " + ex.Message);
            }
            finally
            {
                if (p != IntPtr.Zero) System.Runtime.InteropServices.Marshal.FreeHGlobal(p);
            }

            return result.ToArray();
        }

        private void ShowDropHint(string text)
        {
            if (_dropHint == null) return;
            if (_dropHintText != null) _dropHintText.Text = text;
            _dropHint.Visibility = Visibility.Visible;
        }

        private void HideDropHint()
        {
            if (_dropHint == null) return;
            _dropHint.Visibility = Visibility.Collapsed;
        }

        // =====================================================================
        // 刷新
        // =====================================================================
        public void RefreshRunning(bool force)
        {
            if (_busy) return;

            // 拖动排序 / 拖动位置期间绝对不要重建视图：否则会把用户的拖动结果冲掉
            if (_dragging || _dragView != null || _editing)
            {
                _rebuildPending = true;
                return;
            }

            _busy = true;
            try
            {
                var apps = WindowService.Group(WindowService.Snapshot());
                string sig = Signature(apps);
                bool structural = force || sig != _sig;

                if (structural)
                {
                    _sig = sig;
                    BuildItems(apps);
                    RebuildViews();
                    ApplyGeometry();
                }
                else
                {
                    foreach (var v in _views)
                    {
                        var t = v.Model;
                        if (t == null) continue;
                        t.Windows = t.Pinned ? FindWindows(apps, t.MatchExe)
                                             : FindWindows(apps, t.MatchExe);
                        v.ApplyPalette(_pal);
                        v.ToolTip = TooltipFor(t);
                    }
                }
            }
            catch (Exception ex)
            {
                SettingsStore.Log("RefreshRunning: " + ex);
            }
            finally { _busy = false; }
        }

        // =====================================================================
        // 设置
        // =====================================================================
        public void SaveSettings()
        {
            _s.Clamp();
            SettingsStore.Save(_s);
            var h = SettingsApplied;
            if (h != null) h(this, EventArgs.Empty);
        }

        /// <summary>
        /// 只把"倒影"参数刷到现有图标上（不重建图标、不重套配色）。
        /// 拖倒影滑块时必须用这个：以前每次 tick 都走 ApplyAllChanges →
        /// 整条 Dock 重建 + 写盘，拖起来又卡又闪，条高还会一跳一跳。
        /// </summary>
        public void ApplyReflectionPreview()
        {
            foreach (var v in _views) v.ApplyReflection(_s);
            // 和"上次真正套用到窗口上的 BarH"比较——不能和当前 BarH 比，
            // 那样永远相等，窗口高度就永远不跟着变（拖"高度"时看不到效果就是这个原因）。
            if (_lastBarH > 0 && Math.Abs(BarH - _lastBarH) > 0.5) ApplyGeometry();
        }

        /// <summary>
        /// 界面语言变了：标题、托盘菜单、图标提示、系统区提示、菜单都按新语言重来。
        /// 菜单本来就是每次弹出时现建的，这里只处理"建好就不再变"的那几处。
        /// </summary>
        public void RefreshLanguage()
        {
            try
            {
                Title = AppInfo.Name;
                if (App.Tray != null) App.Tray.Reload();
                if (_sysArea != null) _sysArea.RefreshLanguage();
                ApplyAllChanges();
            }
            catch (Exception ex) { SettingsStore.Log("RefreshLanguage: " + ex.Message); }
        }
        public void ApplyAllChanges()
        {
            ApplyPalette();
            RefreshRunning(true);
        }

        public void OpenSettings()
        {
            App.ShowSettings(this);
        }

        // =====================================================================
        // 弹出层
        // =====================================================================
        private void ShowFlyout(DockTile t, FrameworkElement anchor)
        {
            CloseFlyout();
            _flyout = new FlyoutWindow(t, _pal, this);
            _flyout.Closed += delegate { _flyout = null; };
            _flyout.ShowNear(this, anchor != null ? anchor : _bar);
            StartPreviewWatch();
        }

        public void CloseFlyout()
        {
            if (_flyout != null)
            {
                var f = _flyout;
                _flyout = null;
                try { f.Close(); } catch { }
            }
        }
    }
}
