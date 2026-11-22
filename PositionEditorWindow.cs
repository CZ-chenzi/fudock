using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Fudock
{
    /// <summary>自定义位置编辑时漂浮在 Dock 旁边的小工具条（提示 + 保存 / 取消）。</summary>
    public class PositionEditorWindow : Window
    {
        private readonly DockWindow _dock;
        private readonly Palette _pal;

        public PositionEditorWindow(DockWindow dock)
        {
            _dock = dock;
            _pal = Palette.Create(dock.Settings);

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            FontFamily = Sx.UiFont;

            BuildUi();

            // 首个点击可能被"激活窗口"吃掉，先主动激活自己
            Loaded += delegate { try { Activate(); } catch { } };

            PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) _dock.CancelPositionEdit();
                else if (e.Key == Key.Enter) _dock.CommitPositionEdit();
            };
        }

        private void BuildUi()
        {
            Brush cardBg = _pal.IsDark
                ? new SolidColorBrush(Color.FromArgb(0xF5, 0x2A, 0x2A, 0x32))
                : new SolidColorBrush(Color.FromArgb(0xF5, 0xFB, 0xFB, 0xFE));

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock tip = Sx.Txt(L.T("拖动 Dock 到任意位置"), 12.5, _pal.Text);
            tip.VerticalAlignment = VerticalAlignment.Center;
            tip.Margin = new Thickness(16, 0, 14, 0);
            row.Children.Add(tip);

            Border cancel = Sx.Button(_pal, L.T("取消"), false, delegate { _dock.CancelPositionEdit(); });
            cancel.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(cancel);

            Border ok = Sx.Button(_pal, L.T("保存位置"), true, delegate { _dock.CommitPositionEdit(); });
            ok.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(ok);

            var shell = new Border
            {
                Child = row,
                Padding = new Thickness(0, 7, 0, 7),
                CornerRadius = new CornerRadius(10),
                Background = cardBg,
                BorderBrush = _pal.Accent,
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 22,
                    ShadowDepth = 4,
                    Opacity = 0.35,
                    Direction = 270,
                    Color = Colors.Black
                }
            };

            Content = new Grid { Children = { shell }, Margin = new Thickness(12) };
        }

        /// <summary>贴在 Dock 条上方（放不下就放下方）。</summary>
        public void Follow(DockWindow dock)
        {
            try
            {
                UpdateLayout();
                Rect wa = SystemParameters.WorkArea;

                double x = dock.Left + (dock.Width - ActualWidth) / 2.0;
                x = Math.Max(wa.Left + 8, Math.Min(x, wa.Right - ActualWidth - 8));

                double y = dock.BarTopScreen - ActualHeight - 12;
                if (y < wa.Top + 8) y = dock.BarBottomScreen + 12;
                if (y + ActualHeight > wa.Bottom - 8) y = Math.Max(wa.Top + 8, wa.Bottom - ActualHeight - 8);

                Left = x;
                Top = y;
            }
            catch { }
        }
    }
}
