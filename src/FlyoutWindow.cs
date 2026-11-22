using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Fudock
{
    /// <summary>窗口预览弹层：显示该应用的窗口缩略图，点一下就切过去。</summary>
    public class FlyoutWindow : Window
    {
        private const double PreviewW = 300;
        private const double PreviewH = 168;
        private const int MaxRows = 5;

        private readonly DockTile _tile;
        private readonly Palette _pal;
        private readonly DockWindow _dock;

        public FlyoutWindow(DockTile tile, Palette pal, DockWindow dock)
        {
            _tile = tile;
            _pal = pal;
            _dock = dock;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowActivated = false;

            BuildUi();

            PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) Close();
            };
        }

        // =====================================================================
        private void BuildUi()
        {
            Brush cardBg = _pal.IsDark
                ? new LinearGradientBrush(Color.FromArgb(0xFF, 0x2E, 0x2E, 0x36),
                                          Color.FromArgb(0xFF, 0x24, 0x24, 0x2B), 90)
                : new LinearGradientBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
                                          Color.FromArgb(0xFF, 0xF2, 0xF2, 0xF6), 90);

            var panel = new StackPanel { Margin = new Thickness(8) };

            // 标题：应用名 + 窗口数
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 4, 8) };
            TextBlock name = Sx.TxtBold(_tile.DisplayName, 12.5, _pal.Text);
            name.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(name);

            TextBlock count = Sx.Txt(L.T("  {0} 个窗口", _tile.Windows.Count), 11,
                _pal.IsDark ? new SolidColorBrush(Color.FromArgb(0x9A, 0xFF, 0xFF, 0xFF))
                            : new SolidColorBrush(Color.FromArgb(0x99, 0x1A, 0x1A, 0x1F)));
            count.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(count);
            panel.Children.Add(head);

            int shown = 0;
            foreach (AppWindow w in _tile.Windows)
            {
                if (shown >= MaxRows) break;
                panel.Children.Add(PreviewRow(w));
                shown++;
            }

            if (_tile.Windows.Count > MaxRows)
            {
                TextBlock more = Sx.Txt(L.T("还有 {0} 个窗口…", _tile.Windows.Count - MaxRows), 11, _pal.TextMuted);
                more.Margin = new Thickness(6, 2, 0, 2);
                panel.Children.Add(more);
            }

            var card = new Border
            {
                Child = panel,
                CornerRadius = new CornerRadius(12),
                Background = cardBg,
                BorderBrush = _pal.BarBorder,
                BorderThickness = new Thickness(1)
            };

            // 投影单独一层，避免把缩略图和文字糊掉
            var shadow = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = cardBg,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 24,
                    ShadowDepth = 6,
                    Opacity = 0.42,
                    Direction = 270,
                    Color = Colors.Black
                }
            };

            var root = new Grid { Margin = new Thickness(14) };
            root.Children.Add(shadow);
            root.Children.Add(card);
            Content = root;
        }

        // =====================================================================
        private FrameworkElement PreviewRow(AppWindow w)
        {
            // 可见就现抓；最小化就回放后台缓存的"最后一帧"
            ImageSource shot = ThumbnailCache.Get(w.Handle);
            bool minimized = ThumbnailCache.IsMinimized(w.Handle);

            var frame = new Border
            {
                Height = PreviewH,
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                Background = _pal.IsDark
                    ? new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF))
                    : new SolidColorBrush(Color.FromArgb(0x14, 0x00, 0x00, 0x00))
            };
            frame.SizeChanged += delegate
            {
                if (frame.ActualWidth > 0 && frame.ActualHeight > 0)
                    frame.Clip = new RectangleGeometry(
                        new Rect(0, 0, frame.ActualWidth, frame.ActualHeight), 8, 8);
            };

            if (shot != null)
            {
                var img = new Image
                {
                    Source = shot,
                    Stretch = Stretch.UniformToFill,
                    SnapsToDevicePixels = true
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                frame.Child = img;
            }
            else
            {
                var ph = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                if (_tile.Icon != null)
                {
                    var icon = new Image { Source = _tile.Icon, Width = 40, Height = 40, Margin = new Thickness(0, 0, 0, 8) };
                    RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
                    ph.Children.Add(icon);
                }
                TextBlock tip = Sx.Txt(L.T("已最小化 / 无法预览"), 11.5, _pal.TextMuted);
                tip.HorizontalAlignment = HorizontalAlignment.Center;
                ph.Children.Add(tip);
                frame.Child = ph;
            }

            // 右上角关闭按钮（悬停才显示）
            TextBlock x = Sx.Txt("✕", 11, Brushes.White);
            x.HorizontalAlignment = HorizontalAlignment.Center;
            x.VerticalAlignment = VerticalAlignment.Center;

            var close = new Border
            {
                Child = x,
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x20, 0x20, 0x24)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, 6, 0),
                Cursor = Cursors.Hand,
                Opacity = 0,
                ToolTip = L.T("关闭窗口")
            };

            var winRef = w;
            close.MouseEnter += delegate { close.Background = new SolidColorBrush(Color.FromArgb(0xE0, 0xD1, 0x34, 0x38)); };
            close.MouseLeave += delegate { close.Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x20, 0x20, 0x24)); };
            close.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                WindowService.Close(winRef.Handle);
                Close();
                _dock.RefreshRunning(true);
            };

            var shotGrid = new Grid();
            shotGrid.Children.Add(frame);

            // 最小化时左上角标一下（缩略图是最后一次快照）
            if (minimized && shot != null)
            {
                TextBlock badgeText = Sx.Txt(L.T("已最小化"), 10, Brushes.White);
                badgeText.HorizontalAlignment = HorizontalAlignment.Center;
                badgeText.VerticalAlignment = VerticalAlignment.Center;
                var badge = new Border
                {
                    Child = badgeText,
                    CornerRadius = new CornerRadius(7),
                    Background = new SolidColorBrush(Color.FromArgb(0xC0, 0x20, 0x20, 0x24)),
                    Padding = new Thickness(7, 2, 7, 3),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(6, 0, 0, 6),
                    IsHitTestVisible = false
                };
                shotGrid.Children.Add(badge);
            }

            shotGrid.Children.Add(close);

            string title = string.IsNullOrEmpty(w.Title) ? L.T("(无标题)") : w.Title;
            TextBlock titleTb = Sx.Txt(title, 11.5, _pal.Text);
            titleTb.TextTrimming = TextTrimming.CharacterEllipsis;
            titleTb.Margin = new Thickness(2, 6, 2, 0);
            titleTb.ToolTip = title;

            var inner = new StackPanel();
            inner.Children.Add(shotGrid);
            inner.Children.Add(titleTb);

            var row = new Border
            {
                Child = inner,
                Width = PreviewW,
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent,
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = Cursors.Hand
            };

            Brush hoverBg = _pal.IsDark
                ? new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF))
                : new SolidColorBrush(Color.FromArgb(0x12, 0x00, 0x00, 0x00));

            row.MouseEnter += delegate
            {
                row.BorderBrush = _pal.Accent;
                row.Background = hoverBg;
                close.Opacity = 1;
            };
            row.MouseLeave += delegate
            {
                row.BorderBrush = Brushes.Transparent;
                row.Background = Brushes.Transparent;
                close.Opacity = 0;
            };
            row.MouseLeftButtonUp += delegate
            {
                Close();
                WindowService.Activate(winRef.Handle);
            };

            return row;
        }

        // =====================================================================
        /// <summary>贴在 Dock 条上方、对齐到被悬停的图标。</summary>
        public void ShowNear(DockWindow dock, FrameworkElement anchor)
        {
            Show();
            UpdateLayout();

            Rect wa = SystemParameters.WorkArea;

            double anchorX;
            try
            {
                Point p = anchor.PointToScreen(new Point(anchor.ActualWidth / 2.0, 0));
                PresentationSource src = PresentationSource.FromVisual(dock);
                double scale = (src != null && src.CompositionTarget != null)
                    ? src.CompositionTarget.TransformFromDevice.M11 : 1.0;
                anchorX = p.X * scale;
            }
            catch { anchorX = dock.Left + dock.Width / 2.0; }

            double x = anchorX - ActualWidth / 2.0;
            x = Math.Max(wa.Left + 8, Math.Min(x, wa.Right - ActualWidth - 8));

            double y = dock.BarTopScreen - ActualHeight - 10;
            if (y < wa.Top + 8) y = dock.BarBottomScreen + 10;
            if (y + ActualHeight > wa.Bottom - 8)
                y = Math.Max(wa.Top + 8, wa.Bottom - ActualHeight - 8);

            Left = x;
            Top = y;
        }
    }
}
