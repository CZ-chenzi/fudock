using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Windows.Shapes;

namespace Fudock
{
    public enum SystemPanelMode { Notify, Quick, Tray }

    /// <summary>
    /// 从 Dock 边缘长出来的系统面板（铃铛 / 胶囊 / ^ 共用一个）。
    ///
    /// 为什么不是"一个独立弹窗挡在屏幕中间"：
    ///  · 它贴着 Dock 边缘对齐、从 Dock 那一侧向上（或向下）生长，尺度只有几百 DIP；
    ///  · 动画曲线、时长和 Dock 自己的放大/让位动画同一套（CubicEase / EaseOut，170ms）；
    ///  · 打开时不抢焦点式的全屏遮罩——用 Deactivated 关，点外部即消失。
    ///
    /// 面板只做"入口 + 说明 + 一次点击"，真正的通知中心/快速设置仍然是 Windows 原生的。
    /// </summary>
    public class SystemPanel : Window
    {
        private const double PanelW = 336;
        private const int AnimMs = 170;

        private readonly Palette _pal;
        private readonly DockWindow _dock;
        private readonly SystemPanelMode _mode;
        private readonly SystemStatus _status;
        private readonly int _unread;


        private FrameworkElement _card;
        private FrameworkElement _anchor;      // 用来定位的锚点（刷新后重开面板要复用）
        private DateTime _shownAt = DateTime.MinValue;   // 打开时间：用来容忍"刚显示就被系统弹层抢焦点"

        public SystemPanel(DockWindow dock, Palette pal, SystemPanelMode mode, SystemStatus status, int unread)
        {
            _dock = dock;
            _pal = pal;
            _mode = mode;
            _status = status;
            _unread = unread;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowActivated = true;         // 需要键盘（Esc）与"点外部关闭"

            BuildUi();

            // 打开一个 WPF 窗口时可能拿不到前台权（前台锁），于是刚 Show 完就收到 Deactivated，
            // 面板会"闪一下就没了"。所以这里延迟一小会儿再确认：真的失焦、且鼠标也不在面板上，才关。
            Deactivated += delegate
            {
                // 刚弹出来就被抢焦点（Windows 托盘弹层关闭时会把焦点弹回桌面）
                // 这种情况不能当成"用户点了别处"，否则面板会闪一下就没。
                if ((DateTime.UtcNow - _shownAt).TotalMilliseconds < 450)
                {
                    SettingsStore.Log("SystemPanel: Deactivated 但在宽限期内，忽略");
                    Activate();
                    return;
                }
                SettingsStore.Log("SystemPanel: Deactivated（延迟确认后决定是否关闭）");
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
                t.Tick += delegate
                {
                    t.Stop();
                    if (!IsActive && !IsMouseOver)
                    {
                        SettingsStore.Log("SystemPanel: 关闭（真的失焦且鼠标不在）");
                        Close();
                    }
                    else SettingsStore.Log("SystemPanel: 保持打开（isActive=" + IsActive + " mouseOver=" + IsMouseOver + "）");
                };
                t.Start();
            };
            PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            };

            Loaded += delegate { SettingsStore.Log("SystemPanel: Loaded mode=" + _mode + " active=" + IsActive); };
            Activated += delegate { SettingsStore.Log("SystemPanel: Activated"); };
            Closed += delegate { SettingsStore.Log("SystemPanel: Closed"); };
            SettingsStore.Log("SystemPanel: created mode=" + mode);
        }

        // =====================================================================
        private void BuildUi()
        {
            Brush cardBg = _pal.IsDark
                ? new LinearGradientBrush(Color.FromArgb(0xFF, 0x2E, 0x2E, 0x36),
                                          Color.FromArgb(0xFF, 0x23, 0x23, 0x2A), 90)
                : new LinearGradientBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
                                          Color.FromArgb(0xFF, 0xF4, 0xF4, 0xF8), 90);

            var body = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            body.Children.Add(Header());

            switch (_mode)
            {
                case SystemPanelMode.Notify: BuildNotify(body); break;
                case SystemPanelMode.Quick: BuildQuick(body); break;
                default: BuildTray(body); break;
            }

            body.Children.Add(Footer());

            _card = new Border
            {
                Width = PanelW,
                Background = cardBg,
                BorderBrush = _pal.IsDark
                    ? new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF))
                    : new SolidColorBrush(Color.FromArgb(0x26, 0x00, 0x00, 0x00)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(Math.Max(10, _pal.CornerRadius)),
                Child = body,
                RenderTransformOrigin = new Point(0.5, 1.0)   // 底部停靠：从下往上长
            };

            Content = new Border { Padding = new Thickness(10), Child = _card };
        }

        private FrameworkElement Header()
        {
            string title, desc;
            switch (_mode)
            {
                case SystemPanelMode.Notify: title = L.T("通知"); desc = L.T("Windows 原生通知中心"); break;
                case SystemPanelMode.Quick: title = L.T("状态"); desc = L.T("网络 · 音量 · 电源"); break;
                default: title = L.T("系统托盘"); desc = L.T("第三方图标由 Windows 托管"); break;
            }

            var g = new Grid { Margin = new Thickness(2, 0, 2, 10) };
            var sp = new StackPanel();
            sp.Children.Add(Sx.TxtBold(title, 14, _pal.Text));
            TextBlock sub = Sx.Txt(desc, 11, _pal.TextMuted);
            sub.Margin = new Thickness(0, 3, 0, 0);
            sp.Children.Add(sub);
            g.Children.Add(sp);

            if (_mode == SystemPanelMode.Notify && _unread > 0)
            {
                var badge = new Border
                {
                    Background = _pal.Accent,
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(8, 2, 8, 2),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = Sx.TxtBold(L.T("{0} 条未读", _unread), 11, _pal.OnAccent)
                };
                g.Children.Add(badge);
            }
            return g;
        }

        // ---------------- 各模式的行 ----------------
        private void BuildNotify(StackPanel body)
        {
            body.Children.Add(InfoRow(L.T("通知都在系统通知中心里"),
                L.T("Dock 不复制通知内容，也不弹自己的 toast——只负责把你送过去。")));
            body.Children.Add(Row(L.T("打开通知中心"), "Win + N", delegate
            {
                Close();
                if (!ShellActions.NotificationCenter()) ShellActions.NotificationSettings();
            },
            delegate
            {
                Close();
                ShellActions.NotificationSettings();
            }));
        }

        private void BuildQuick(StackPanel body)
        {
            if (_status != null)
            {
                body.Children.Add(InfoRow(_status.NetworkText(), L.T("网络")));
                body.Children.Add(InfoRow(_status.HasVolume ? (_status.Muted ? L.T("已静音") : _status.Volume + "%") : L.T("不可用"), L.T("音量")));
                body.Children.Add(InfoRow(_status.PowerText(), L.T("电源")));
            }
            body.Children.Add(Row(L.T("打开快速设置"), "Win + A", delegate
            {
                Close();
                if (!ShellActions.QuickSettings()) ShellActions.Open("ms-settings:");
            },
            delegate
            {
                Close();
                ShellActions.Open("ms-settings:");
            }));
        }

        private void BuildTray(StackPanel body)
        {
            // 直接把后台应用的托盘图标铺在这里，不再让你去点 Windows 自己的 ^
            List<TrayIconEntry> items = TrayBridge.Cached;
            if (items.Count == 0) items = TrayBridge.Enumerate(true);

            if (items.Count > 0)
            {
                body.Children.Add(TrayGrid(items));
            }
            else
            {
                body.Children.Add(InfoRow(L.T("没读到后台应用"),
                    L.T("系统托盘当前是空的（应用可能还没注册图标），或者读取超时。可点下面刷新。")));
            }

            body.Children.Add(Row(L.T("刷新列表"), L.T("读取系统托盘"), delegate
            {
                Close();
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
                {
                    TrayBridge.Enumerate(true);
                    _dock.OpenSystemPanelPublic(SystemPanelMode.Tray, _anchor);
                }));
            },
            delegate { }));

            body.Children.Add(Row(L.T("展开系统托盘"), L.T("系统原生"), delegate
            {
                // 先关面板，等它真的消失、焦点回到桌面再注入按键——
                // 否则 Win+B 会被我们自己的窗口吃掉
                Close();
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
                {
                    ShellActions.OpenSystemTrayOverflow();
                }));
            },
            delegate
            {
                Close();
                ShellActions.TaskbarSettings();
            }));
        }

        private FrameworkElement Footer()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 8, 2, 0) };
            sp.Children.Add(Sx.Button(_pal, L.T("打开系统设置"), false, delegate
            {
                Close();
                ShellActions.Open("ms-settings:");
            }));
            return sp;
        }

        /// <summary>说明块：图标 + 主文案 + 次文案（不用矩形分隔，靠留白分组）。</summary>
        /// <summary>
        /// 后台应用图标网格：每个格子 = 真实抓到的 24×24 图标 + 悬停高亮 + tooltip，
        /// 点一下就相当于点了它在系统托盘里的图标（我们替你点，不用去开 Windows 那个 ^）。
        /// </summary>
        /// <summary>
        /// 后台应用图标网格：真实图标 + 小名字，点一下等于点它在系统托盘里的图标。
        /// 图标是按名字从运行中的进程取来的，取不到就用首字母块（不会出现空白格子）。
        /// </summary>
        private FrameworkElement TrayGrid(List<TrayIconEntry> items)
        {
            const double cell = 58;

            var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };

            foreach (TrayIconEntry it in items)
            {
                FrameworkElement visual;

                if (it.Icon != null)
                {
                    var img = new Image
                    {
                        Source = it.Icon,
                        Width = 24,
                        Height = 24,
                        Stretch = Stretch.Uniform,
                        SnapsToDevicePixels = true
                    };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    visual = img;
                }
                else
                {
                    // 兜底：首字母块（和 Dock 图标缺失时同一套做法）
                    string ch = string.IsNullOrEmpty(it.Name) ? "?" : it.Name.Substring(0, 1).ToUpperInvariant();
                    visual = new Border
                    {
                        Width = 24,
                        Height = 24,
                        CornerRadius = new CornerRadius(7),
                        Background = new SolidColorBrush(_pal.LetterBg),
                        Child = new TextBlock
                        {
                            Text = ch,
                            FontSize = 12,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = new SolidColorBrush(_pal.LetterFg),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    };
                }

                var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                var iconHost = new Grid { Height = 30, VerticalAlignment = VerticalAlignment.Center };
                visual.HorizontalAlignment = HorizontalAlignment.Center;
                visual.VerticalAlignment = VerticalAlignment.Center;
                iconHost.Children.Add(visual);
                col.Children.Add(iconHost);

                TextBlock label = Sx.Txt(it.Name, 9.5, _pal.TextMuted);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.MaxWidth = cell - 6;
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                label.TextAlignment = TextAlignment.Center;
                col.Children.Add(label);

                var box = new Border
                {
                    Width = cell,
                    Height = cell,
                    CornerRadius = new CornerRadius(Math.Max(6, _pal.CornerRadius * 0.7)),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    Child = col,
                    Margin = new Thickness(0, 0, 2, 2)
                };
                ToolTipService.SetInitialShowDelay(box, 320);
                ToolTipService.SetPlacement(box, System.Windows.Controls.Primitives.PlacementMode.Top);
                box.ToolTip = it.Name;

                TrayIconEntry target = it;
                box.MouseEnter += delegate { box.Background = _pal.HoverFill; };
                box.MouseLeave += delegate { box.Background = Brushes.Transparent; };
                box.MouseLeftButtonUp += delegate
                {
                    Close();
                    Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
                    {
                        if (!TrayBridge.Invoke(target))
                        {
                            SettingsStore.Log("TrayBridge.Invoke 失败: " + target.Name);
                            ShellActions.OpenSystemTrayOverflow();
                        }
                    }));
                };
                wrap.Children.Add(box);
            }
            return wrap;
        }
        private FrameworkElement InfoRow(string main, string sub)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 4, 2, 8) };

            // 用一条 3px 的强调色竖条代替小图标：不依赖图标字体，也不会显得"密集小图标"
            row.Children.Add(new Border
            {
                Width = 3,
                Height = 14,
                CornerRadius = new CornerRadius(2),
                Background = _pal.Accent,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(2, 4, 12, 0)
            });

            var col = new StackPanel();
            col.Children.Add(Sx.Txt(main, 12.5, _pal.Text));
            if (!string.IsNullOrEmpty(sub))
            {
                TextBlock t = Sx.Txt(sub, 10.5, _pal.TextMuted);
                t.TextWrapping = TextWrapping.Wrap;
                t.Margin = new Thickness(0, 3, 0, 0);
                t.MaxWidth = PanelW - 90;
                col.Children.Add(t);
            }
            row.Children.Add(col);
            return row;
        }

        /// <summary>可点行：左边图标+文案，右边"按键/方式"提示；悬停有 Fluent 底色。</summary>
        private FrameworkElement Row(string text, string hint, Action onClick, Action onRightClick)
        {
            var g = new Grid();

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new Border
            {
                Width = 3,
                Height = 3,
                CornerRadius = new CornerRadius(2),
                Background = _pal.Accent,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 12, 0)
            });
            sp.Children.Add(Sx.Txt(text, 12.5, _pal.Text));
            g.Children.Add(sp);

            var h = Sx.Txt(hint, 10.5, _pal.TextMuted);
            h.HorizontalAlignment = HorizontalAlignment.Right;
            h.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(h);

            var b = new Border
            {
                Child = g,
                CornerRadius = new CornerRadius(Math.Max(6, _pal.CornerRadius * 0.7)),
                Padding = new Thickness(10, 9, 10, 9),
                Margin = new Thickness(0, 2, 0, 2),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand
            };
            b.MouseEnter += delegate { b.Background = _pal.HoverFill; };
            b.MouseLeave += delegate { b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += delegate { if (onClick != null) onClick(); };
            b.MouseRightButtonUp += delegate
            {
                if (onRightClick != null) onRightClick();
            };
            return b;
        }

        // =====================================================================
        // 展开 / 收起
        // =====================================================================
        /// <summary>贴在 Dock 条上方（或下方）并对齐锚点，然后按 Dock 的动效语言长出来。</summary>
        public void ShowNear(DockWindow dock, FrameworkElement anchor)
        {
            _anchor = anchor;
            _shownAt = DateTime.UtcNow;
            bool fromBottom = dock.Settings.Edge == DockEdge.Bottom;

            Show();
            UpdateLayout();

            Rect wa = SystemParameters.WorkArea;
            double anchorX = anchorXOf(dock, anchor);

            double x = anchorX - ActualWidth / 2.0;
            x = Math.Max(wa.Left + 8, Math.Min(x, wa.Right - ActualWidth - 8));

            double y = fromBottom
                ? dock.BarTopScreen - ActualHeight - 8
                : dock.BarBottomScreen + 8;
            if (y < wa.Top + 8) y = dock.BarBottomScreen + 8;
            if (y + ActualHeight > wa.Bottom - 8) y = Math.Max(wa.Top + 8, wa.Bottom - ActualHeight - 8);

            Left = x;
            Top = y;

            if (_card != null)
                _card.RenderTransformOrigin = new Point(0.5, fromBottom ? 1.0 : 0.0);

            PlayGrow(dock.Settings.PanelAnim, fromBottom);
            try { Activate(); } catch { }     // 抢回焦点，免得被系统弹层的收尾动画带走
        }

        private static double anchorXOf(DockWindow dock, FrameworkElement anchor)
        {
            try
            {
                Point p = anchor.PointToScreen(new Point(anchor.ActualWidth / 2.0, 0));
                PresentationSource src = PresentationSource.FromVisual(dock);
                double scale = (src != null && src.CompositionTarget != null)
                    ? src.CompositionTarget.TransformFromDevice.M11 : 1.0;
                return p.X * scale;
            }
            catch
            {
                return dock.Left + dock.Width / 2.0;
            }
        }

        /// <summary>150–200ms、ease-out、从 Dock 那一条边向上/向下展开，同时淡入。</summary>
        private void PlayGrow(bool animate, bool fromBottom)
        {
            if (_card == null) return;

            if (!animate)
            {
                _card.Opacity = 1;
                _card.RenderTransform = new ScaleTransform(1, 1);
                return;
            }

            var scale = new ScaleTransform(1, 0.86);
            _card.RenderTransform = scale;
            _card.Opacity = 0;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var sy = new DoubleAnimation(0.86, 1.0, TimeSpan.FromMilliseconds(AnimMs)) { EasingFunction = ease };
            var fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(AnimMs)) { EasingFunction = ease };
            var rise = new DoubleAnimation(fromBottom ? 10 : -10, 0,
                TimeSpan.FromMilliseconds(AnimMs)) { EasingFunction = ease };

            var move = new TranslateTransform(0, fromBottom ? 10 : -10);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(move);
            _card.RenderTransform = group;

            move.BeginAnimation(TranslateTransform.YProperty, rise);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
            _card.BeginAnimation(OpacityProperty, fade);
        }
    }
}
