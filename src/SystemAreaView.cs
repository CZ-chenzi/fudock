using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Fudock
{
    internal enum SystemSegment { Bell, Capsule, Overflow }

    /// <summary>
    /// Dock 末端的三段式系统区：铃铛 / 状态胶囊 / 溢出入口 ^。
    ///
    /// 视觉语言完全复用 Dock 自己的那一套：
    ///  · 高度 = 图标高度，垂直居中，圆角/悬停底色/文字色都来自 Palette
    ///  · 放大规则和图标一样（SetScale + 宽度联动，让相邻元素一起让位）
    ///  · 不用矩形分隔线，三段之间只留呼吸间距
    /// 胶囊内部也只做"间距分隔"，不做密集小图标：网络 / 音量 / 电池 / 时间揉成一个药丸。
    /// </summary>
    internal class SystemAreaView : Grid
    {
        public event Action<SystemSegment> Clicked;
        public event Action<SystemSegment> MenuRequested;

        private readonly Palette _pal;
        private readonly double _h;          // = 图标高度
        private readonly double _gap;

        private Border _bellBox;
        private Path _bellGlyph;
        private Border _badge;
        private TextBlock _badgeText;
        private Border _capsule;
        private TextBlock _netGlyph;
        private TextBlock _volGlyph;
        private Border _batteryShell;
        private Border _batteryFill;
        private TextBlock _batteryText;
        private TextBlock _timeText;
        private Border _overflowBox;
        private Path _overflowGlyph;
        private Ellipse _dot1, _dot2, _dot3;

        private StackPanel _row;      // 内部横向条：BaseWidth 用它的自然尺寸，不量自己（会被显式 Width 污染）
        private double _baseW;

        public SystemAreaView(Palette pal, double iconSize)
        {
            _pal = pal;
            _h = iconSize;
            _gap = Math.Max(4, Math.Round(iconSize * 0.14));

            Height = _h;
            VerticalAlignment = VerticalAlignment.Center;
            Background = Brushes.Transparent;
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = new ScaleTransform(1, 1);

            Build();
            ApplyPalette(pal);
        }

        // =====================================================================
        // 结构：铃铛 | 胶囊 | ^
        // =====================================================================
        private void Build()
        {
            _row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // ---- 1) 通知铃铛（角标叠在右上角）----
            _bellGlyph = Icon(IconKind.Bell, _h * 0.40);

            _bellBox = new Border
            {
                Width = _h * 0.62,
                Height = _h * 0.62,
                CornerRadius = new CornerRadius(_h * 0.16),
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Child = _bellGlyph
            };
            _bellBox.MouseLeftButtonUp += delegate { Raise(Clicked, SystemSegment.Bell); };
            _bellBox.MouseRightButtonUp += delegate { Raise(MenuRequested, SystemSegment.Bell); };
            _bellBox.MouseEnter += delegate { Hover(_bellBox, true); };
            _bellBox.MouseLeave += delegate { Hover(_bellBox, false); };
            ToolTipService.SetInitialShowDelay(_bellBox, 380);
            ToolTipService.SetPlacement(_bellBox, System.Windows.Controls.Primitives.PlacementMode.Top);
            _bellBox.ToolTip = L.T("通知中心（Win + N）");

            var bellHost = new Grid { VerticalAlignment = VerticalAlignment.Center };
            bellHost.Children.Add(_bellBox);

            _badgeText = new TextBlock
            {
                FontSize = _h * 0.19,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _badge = new Border
            {
                MinWidth = _h * 0.26,
                Height = _h * 0.26,
                CornerRadius = new CornerRadius(_h * 0.13),
                Padding = new Thickness(_h * 0.05, 0, _h * 0.05, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -_h * 0.02, -_h * 0.02, 0),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Child = _badgeText
            };
            bellHost.Children.Add(_badge);

            _row.Children.Add(bellHost);
            _row.Children.Add(Spacer(_gap));

            // ---- 2) 状态胶囊 ----
            _netGlyph = Glyph("\uE701", _h * 0.30);
            _volGlyph = Glyph("\uE767", _h * 0.30);

            // 电池用手画的（不依赖字体字形，任何字号都清楚）
            _batteryFill = new Border
            {
                CornerRadius = new CornerRadius(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 1,
                Height = Math.Max(1, _h * 0.17 - 4),   // 内芯必须有高度，否则 0 高看不见
                Margin = new Thickness(1.5, 0, 0, 0)
            };
            _batteryShell = new Border
            {
                Width = _h * 0.34,
                Height = _h * 0.17,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2.5),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Grid { Children = { _batteryFill } }
            };
            _batteryText = new TextBlock
            {
                FontSize = _h * 0.23,
                VerticalAlignment = VerticalAlignment.Center
            };

            _timeText = new TextBlock
            {
                FontSize = _h * 0.27,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };

            var capsuleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            capsuleRow.Children.Add(_netGlyph);
            capsuleRow.Children.Add(Spacer(_h * 0.12));
            capsuleRow.Children.Add(_volGlyph);
            capsuleRow.Children.Add(Spacer(_h * 0.12));
            capsuleRow.Children.Add(_batteryShell);
            capsuleRow.Children.Add(Spacer(_h * 0.05));
            capsuleRow.Children.Add(_batteryText);

            _dot3 = Dot(); _dot2 = Dot(); _dot1 = Dot();

            var capsuleInner = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            capsuleInner.Children.Add(capsuleRow);
            capsuleInner.Children.Add(_dot1);
            capsuleInner.Children.Add(_timeText);

            _capsule = new Border
            {
                Height = _h * 0.62,
                CornerRadius = new CornerRadius(_h * 0.31),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Padding = new Thickness(_h * 0.20, 0, _h * 0.22, 0),
                Child = capsuleInner
            };
            _capsule.MouseLeftButtonUp += delegate { Raise(Clicked, SystemSegment.Capsule); };
            _capsule.MouseRightButtonUp += delegate { Raise(MenuRequested, SystemSegment.Capsule); };
            _capsule.MouseEnter += delegate { Hover(_capsule, true); };
            _capsule.MouseLeave += delegate { Hover(_capsule, false); };
            ToolTipService.SetInitialShowDelay(_capsule, 380);
            ToolTipService.SetPlacement(_capsule, System.Windows.Controls.Primitives.PlacementMode.Top);
            ToolTipService.SetShowDuration(_capsule, 20000);

            _row.Children.Add(_capsule);
            _row.Children.Add(Spacer(_gap));

            // ---- 3) 溢出入口 ^ ----
            _overflowGlyph = Icon(IconKind.ChevronUp, _h * 0.34);
            _overflowBox = new Border
            {
                Width = _h * 0.46,
                Height = _h * 0.62,
                CornerRadius = new CornerRadius(_h * 0.16),
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Child = _overflowGlyph
            };
            _overflowBox.MouseLeftButtonUp += delegate { Raise(Clicked, SystemSegment.Overflow); };
            _overflowBox.MouseRightButtonUp += delegate { Raise(MenuRequested, SystemSegment.Overflow); };
            _overflowBox.MouseEnter += delegate { Hover(_overflowBox, true); };
            _overflowBox.MouseLeave += delegate { Hover(_overflowBox, false); };
            ToolTipService.SetInitialShowDelay(_overflowBox, 380);
            ToolTipService.SetPlacement(_overflowBox, System.Windows.Controls.Primitives.PlacementMode.Top);
            _overflowBox.ToolTip = L.T("系统托盘 · 第三方图标折在这里");

            _row.Children.Add(_overflowBox);

            Children.Add(_row);
        }

        private static TextBlock Glyph(string ch, double size)
        {
            return new TextBlock
            {
                Text = ch,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = size,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        internal enum IconKind { Bell, ChevronUp, ChevronRight }

        /// <summary>
        /// 画出来的小图标：不依赖 Segoe MDL2 的字形编号（不同系统版本字形会变），
        /// 只有几段路径，任意 DPI 都清晰，和 Dock 的矢量观感一致。
        /// </summary>
        internal static Path Icon(IconKind kind, double size)
        {
            var p = new Path
            {
                Stretch = Stretch.Uniform,
                Width = size,
                Height = size,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };

            switch (kind)
            {
                case IconKind.Bell:
                    // 钟身 + 铃舌（24×24 视图框，这里用 0..1 归一化坐标，Stretch 负责放大）
                    p.Data = Geometry.Parse(
                        "F1 M12 2.4 C8.9 2.4 6.4 4.9 6.4 8 L6.4 12 L4.6 15.4 " +
                        "C4.2 16 4.6 16.8 5.4 16.8 L18.6 16.8 C19.4 16.8 19.8 16 19.4 15.4 " +
                        "L17.6 12 L17.6 8 C17.6 4.9 15.1 2.4 12 2.4 Z " +
                        "M9.7 19.2 L14.3 19.2 C14.1 20.5 13.1 21.4 12 21.4 C10.9 21.4 9.9 20.5 9.7 19.2 Z");
                    break;
                case IconKind.ChevronUp:
                    p.Data = Geometry.Parse("M2 15 L12 5 L22 15");
                    p.StrokeThickness = 2.1;
                    p.Fill = null;
                    break;
                default:
                    p.Data = Geometry.Parse("M8 2 L18 12 L8 22");
                    p.StrokeThickness = 2.1;
                    p.Fill = null;
                    break;
            }
            return p;
        }

        private static FrameworkElement Spacer(double w)
        {
            return new Border { Width = w, Background = Brushes.Transparent };
        }

        private Ellipse Dot()
        {
            return new Ellipse
            {
                Width = 2,
                Height = 2,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(_h * 0.12, 0, _h * 0.12, 0),
                Opacity = 0.45
            };
        }

        private void Raise(Action<SystemSegment> handler, SystemSegment seg)
        {
            if (handler != null) handler(seg);
        }

        private void Hover(Border box, bool on)
        {
            box.Background = on ? _pal.HoverFill : Brushes.Transparent;
            box.BorderBrush = on ? _pal.HoverBorder : null;
            box.BorderThickness = on ? new Thickness(1) : new Thickness(0);
        }

        // =====================================================================
        // 主题
        // =====================================================================
        public void ApplyPalette(Palette pal)
        {
            Brush fg = pal.Text;
            Brush muted = new SolidColorBrush(pal.IsDark
                ? Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0xB4, 0x1A, 0x1A, 0x1F));

            _bellGlyph.Fill = fg;
            _netGlyph.Foreground = muted;
            _volGlyph.Foreground = muted;
            _batteryText.Foreground = muted;
            _timeText.Foreground = fg;
            _overflowGlyph.Stroke = muted;
            _dot1.Fill = _dot2.Fill = _dot3.Fill = muted;

            _capsule.Background = pal.IsDark
                ? new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF))
                : new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF));
            _capsule.BorderBrush = pal.IsDark
                ? new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF))
                : new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0x00, 0x00));
            _capsule.BorderThickness = new Thickness(1);

            _batteryShell.BorderBrush = muted;
            _batteryFill.Background = pal.Accent;

            _badge.Background = pal.Accent;
            _badgeText.Foreground = pal.OnAccent;
        }

        // =====================================================================
        // 放大（规则与 Dock 图标一致）
        // =====================================================================
        /// <summary>
        /// 系统区基准宽度（不含放大）。
        /// 它决定 Dock 窗口宽度，**绝不能被算成 0**：一旦为 0，窗口会按"没有系统区"收窄，
        /// 系统区就被裁到窗口外 —— 看起来就是"这个功能不显示"。
        /// 所以只在量到有效值时缓存，量不到也给一个保守估计。
        /// </summary>
        public double BaseWidth
        {
            get
            {
                if (_baseW > 0) return _baseW;
                if (_row == null) return _h * 3;

                _row.Measure(new Size(double.PositiveInfinity, _h));
                double w = _row.DesiredSize.Width;
                if (w > 0) _baseW = w;
                return w > 0 ? w : _h * 3;
            }
        }

        private double _scale = 1;

        public void SetScale(double scale)
        {
            if (Math.Abs(_scale - scale) < 0.001) return;
            _scale = scale;
            var st = RenderTransform as ScaleTransform;
            if (st != null) { st.ScaleX = scale; st.ScaleY = scale; }
            Width = BaseWidth * scale;
        }

        // =====================================================================
        // 数据刷新
        // =====================================================================
        /// <summary>把最新状态写进胶囊；显示配置（时间/角标/溢出入口）由调用方决定。</summary>
        public void ApplyStatus(SystemStatus st, int unread, DockSettings s)
        {
            if (st == null) return;

            // 网络：断网时用低透明度表示，不额外塞一个"红叉"图标
            _netGlyph.Text = st.Net == NetKind.Wired ? "\uE839" : "\uE701";
            _netGlyph.Opacity = st.Net == NetKind.Off ? 0.32 : 1.0;

            // 音量
            _volGlyph.Text = (st.Muted || st.Volume == 0) ? "\uE74F" : "\uE767";
            _volGlyph.Opacity = st.HasVolume ? 1.0 : 0.32;

            // 电池 / 电源
            bool showBattery = st.BatteryPercent >= 0
                && (st.Power == PowerKind.Battery || st.Power == PowerKind.Charging);
            if (st.Power == PowerKind.Ac && st.BatteryPercent < 0)
            {
                // 台式机：直接写"电源"，不画电量条
                _batteryShell.Visibility = Visibility.Collapsed;
                _batteryText.Text = L.T("电源");
            }
            else if (showBattery)
            {
                _batteryShell.Visibility = Visibility.Visible;
                _batteryText.Text = st.BatteryPercent + "%";
                double inner = Math.Max(1, (_batteryShell.Width - 5) * (st.BatteryPercent / 100.0));
                _batteryFill.Width = inner;
                _batteryFill.Background = st.Power == PowerKind.Charging ? _pal.Accent
                    : (st.BatteryPercent <= 15 ? new SolidColorBrush(Color.FromRgb(0xE0, 0x4A, 0x4A)) : _pal.Accent);
            }
            else
            {
                _batteryShell.Visibility = Visibility.Collapsed;
                _batteryText.Text = st.PowerText().Replace(L.T("已接通电源"), L.T("电源"));
            }

            // 时间
            bool showTime = s == null || s.SystemCapsuleTime;
            _timeText.Visibility = showTime ? Visibility.Visible : Visibility.Collapsed;
            _dot1.Visibility = showTime ? Visibility.Visible : Visibility.Collapsed;
            _timeText.Text = st.TimeText();

            // 文本长度会变（9:05 → 18:20、9+ → 12），基准宽度只能往上修，不能变小，避免 Dock 抖动
            if (_row != null && IsMeasureValid)
            {
                _row.Measure(new Size(double.PositiveInfinity, _h));
                double w = _row.DesiredSize.Width;
                if (w > _baseW + 4) _baseW = w;
            }

            _capsule.ToolTip =
                L.T("网络：") + st.NetworkText() + "\n" +
                L.T("音量：") + (st.HasVolume ? (st.Muted ? L.T("已静音") : st.Volume + "%") : L.T("不可用")) + "\n" +
                L.T("电源：") + st.PowerText() + "\n" +
                L.T("单击打开 Windows 快速设置（Win + A）");

            // 未读角标
            bool badge = s == null || s.SystemBadge;
            if (!badge || unread <= 0)
            {
                _badge.Visibility = Visibility.Collapsed;
                _bellBox.ToolTip = L.T("通知中心（Win + N）");
            }
            else
            {
                _badge.Visibility = Visibility.Visible;
                _badgeText.Text = unread > 9 ? "9+" : unread.ToString();
                _bellBox.ToolTip = L.T("通知中心（Win + N）· {0} 条未读", unread);
            }
        }

        /// <summary>来通知时放一次轻脉冲（一次就够，不连播、不弹 toast）。</summary>
        public void PulseBadge()
        {
            if (_badge.Visibility != Visibility.Visible) return;
            try
            {
                var st = _badge.RenderTransform as ScaleTransform;
                if (st == null)
                {
                    st = new ScaleTransform(1, 1);
                    _badge.RenderTransform = st;
                }
                var anim = new DoubleAnimation(1.0, 1.45, TimeSpan.FromMilliseconds(120))
                {
                    AutoReverse = true,
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }
            catch { }
        }

        /// <summary>溢出入口 ^ 的显隐（第三方图标本身永远折叠，这里只是入口）。</summary>
        public void SetOverflowVisible(bool on)
        {
            if (_overflowBox == null) return;
            _overflowBox.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            // 注意：这里不碰 Width、也不触发测量。显隐发生在填充数据之前，
            // 此刻量到的宽度偏小，一旦被缓存就会让 Dock 窗口按"没有系统区"算窄，
            // 系统区被裁到窗口外——这正是上一版"它不显示"的直接原因。
        }

        /// <summary>整体淡出/淡入（全屏时给 Dock 用）。</summary>
        /// <summary>语言变了：重刷那些只在构造时写一次的提示文字。</summary>
        public void RefreshLanguage()
        {
            try
            {
                if (_bellBox != null) _bellBox.ToolTip = L.T("通知中心（Win + N）");
                if (_overflowBox != null) _overflowBox.ToolTip = L.T("系统托盘 · 第三方图标折在这里");
                if (_capsule != null) _capsule.ToolTip = L.T("单击打开 Windows 快速设置（Win + A）");
            }
            catch { }
        }
        public void SetDimmed(bool dim)
        {
            double target = dim ? 0.0 : 1.0;
            var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, anim);
        }
    }
}
