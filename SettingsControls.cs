using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Fudock
{
    /// <summary>设置面板的 UI 积木：全部自绘，避免默认 WPF 控件的 Win7 观感。</summary>
    internal static class Sx
    {
        public static readonly FontFamily UiFont =
            new FontFamily("Microsoft YaHei UI, Segoe UI, sans-serif");

        public static TextBlock Txt(string s, double size, Brush fg)
        {
            return new TextBlock
            {
                Text = s,
                FontSize = size,
                Foreground = fg,
                FontFamily = UiFont,
                TextWrapping = TextWrapping.NoWrap
            };
        }

        public static TextBlock TxtBold(string s, double size, Brush fg)
        {
            TextBlock t = Txt(s, size, fg);
            t.FontWeight = FontWeights.SemiBold;
            return t;
        }

        // ---- 圆角换算：pal.UiRadius = 0 时全部变直角 ----
        public static double RCard(Palette p) { return Math.Round(p.UiRadius * 0.83); }
        public static double RButton(Palette p) { return Math.Round(p.UiRadius * 0.5); }
        public static double RSegment(Palette p) { return Math.Round(p.UiRadius * 0.66); }
        public static double RPill(Palette p) { return Math.Round(p.UiRadius * 0.5); }

        /// <summary>一张圆角卡片。</summary>
        public static Border Card(Palette pal, string title, params UIElement[] rows)
        {
            var sp = new StackPanel();
            if (!string.IsNullOrEmpty(title))
            {
                TextBlock h = TxtBold(title, 12, pal.TextMuted);
                h.Margin = new Thickness(2, 0, 0, 8);
                sp.Children.Add(h);
            }
            foreach (UIElement r in rows) sp.Children.Add(r);

            return new Border
            {
                Background = pal.Glass ? pal.GlassCardBg : pal.CardBg,
                BorderBrush = pal.Glass ? pal.GlassBorder : pal.CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(pal.Glass ? Math.Round(RCard(pal) + 5) : RCard(pal)),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 12),
                Child = sp
            };
        }

        /// <summary>左标题 + 右控件的一行。</summary>
        public static Grid Row(Palette pal, string title, string desc, FrameworkElement right)
        {
            var g = new Grid { Margin = new Thickness(0, 7, 0, 7) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(Txt(title, 13, pal.Text));
            if (!string.IsNullOrEmpty(desc))
            {
                TextBlock d = Txt(desc, 11.5, pal.TextMuted);
                d.Margin = new Thickness(0, 3, 0, 0);
                d.TextWrapping = TextWrapping.Wrap;
                sp.Children.Add(d);
            }
            Grid.SetColumn(sp, 0);
            g.Children.Add(sp);

            if (right != null)
            {
                right.VerticalAlignment = VerticalAlignment.Center;
                right.Margin = new Thickness(14, 0, 0, 0);
                Grid.SetColumn(right, 1);
                g.Children.Add(right);
            }
            return g;
        }

        /// <summary>标题 + 右侧数值 + 整行滑块。</summary>
        public static StackPanel SliderBlock(Palette pal, string title, TextBlock valueText, FrameworkElement slider)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock t = Txt(title, 13, pal.Text);
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, 0);
            g.Children.Add(t);

            valueText.Foreground = pal.TextMuted;
            valueText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(valueText, 1);
            g.Children.Add(valueText);

            var sp = new StackPanel { Margin = new Thickness(0, 9, 0, 9) };
            sp.Children.Add(g);
            slider.Margin = new Thickness(0, 4, 0, 0);
            sp.Children.Add(slider);
            return sp;
        }

        public static Border Line(Palette pal)
        {
            return new Border { Height = 1, Background = pal.Divider, Margin = new Thickness(0, 6, 0, 6) };
        }

        public static Border Button(Palette pal, string text, bool primary, Action onClick)
        {
            var tb = Txt(text, 12.5, primary ? pal.OnAccent : pal.Text);
            tb.HorizontalAlignment = HorizontalAlignment.Center;

            var b = new Border
            {
                Child = tb,
                Padding = new Thickness(18, 7, 18, 7),
                CornerRadius = new CornerRadius(RButton(pal)),
                Background = primary ? pal.Accent : pal.Subtle,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            b.MouseEnter += delegate { b.Opacity = 0.82; };
            b.MouseLeave += delegate { b.Opacity = 1.0; };
            b.MouseLeftButtonUp += delegate { if (onClick != null) onClick(); };
            return b;
        }
    }

    /// <summary>Fluent 风格开关。</summary>
    internal class ToggleSwitch : Grid
    {
        private readonly Border _track;
        private readonly Border _thumb;
        private readonly TranslateTransform _move;
        private readonly Action<bool> _onChange;
        private bool _on;

        public bool IsOn { get { return _on; } }

        public ToggleSwitch(Palette pal, bool on, Action<bool> onChange)
        {
            _on = on;
            _onChange = onChange;

            Width = 46;
            Height = 26;
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
            VerticalAlignment = VerticalAlignment.Center;

            _track = new Border
            {
                CornerRadius = new CornerRadius(Math.Min(13, Sx.RPill(pal))),
                BorderThickness = new Thickness(1)
            };
            Children.Add(_track);

            _thumb = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(Math.Min(9, pal.UiRadius * 0.75)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            };
            _move = new TranslateTransform(on ? 20 : 0, 0);
            _thumb.RenderTransform = _move;
            Children.Add(_thumb);

            ApplyColors(pal);
            MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            MouseLeftButtonUp += delegate { Toggle(); };
        }

        private void Toggle()
        {
            _on = !_on;
            var anim = new DoubleAnimation(_on ? 20 : 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            _move.BeginAnimation(TranslateTransform.XProperty, anim);

            _track.Background = _on ? _onBrush : _offBrush;
            _track.BorderBrush = _on ? _onBrush : _offBorder;
            _thumb.Background = _on ? Brushes.White : _offThumb;

            if (_onChange != null) _onChange(_on);
        }

        private Brush _onBrush, _offBrush, _offBorder, _offThumb;

        private void ApplyColors(Palette pal)
        {
            _onBrush = pal.Accent;
            _offBrush = Brushes.Transparent;
            _offBorder = pal.Subtle;
            _offThumb = pal.TextMuted;

            _track.Background = _on ? _onBrush : _offBrush;
            _track.BorderBrush = _on ? _onBrush : _offBorder;
            _thumb.Background = _on ? Brushes.White : _offThumb;
        }
    }

    /// <summary>Fluent 风格滑块（自绘）。</summary>
    internal class FluentSlider : Grid
    {
        private readonly Border _fill;
        private readonly Border _thumb;
        private readonly double _min, _max;
        private readonly Action<double> _onChange;
        private double _value;
        private bool _drag;

        /// <summary>松手时回调（用于整窗重绘，避免拖动中被重建）。</summary>
        public Action OnCommit;

        public double Value { get { return _value; } }

        public FluentSlider(Palette pal, double min, double max, double value, Action<double> onChange)
        {
            _min = min;
            _max = max;
            _value = value;
            _onChange = onChange;

            Height = 26;
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;

            double rTrack = Math.Round(pal.UiRadius * 0.14);

            var track = new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(rTrack),
                Background = pal.Subtle,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Children.Add(track);

            _fill = new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(rTrack),
                Background = pal.Accent,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Children.Add(_fill);

            _thumb = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(Math.Min(8, pal.UiRadius * 0.66)),
                Background = pal.Accent,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            Children.Add(_thumb);

            SizeChanged += delegate { UpdateVisual(); };
            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
            LostMouseCapture += delegate { _drag = false; };
            UpdateVisual();
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            _drag = true;
            CaptureMouse();
            SetFromX(e.GetPosition(this).X);
            e.Handled = true;
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (!_drag) return;
            SetFromX(e.GetPosition(this).X);
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!_drag) return;
            _drag = false;
            try { ReleaseMouseCapture(); } catch { }
            e.Handled = true;
            if (OnCommit != null) OnCommit();
        }

        private void SetFromX(double x)
        {
            double w = ActualWidth;
            if (w <= 18) return;
            double pct = (x - 8) / (w - 16);
            if (pct < 0) pct = 0;
            if (pct > 1) pct = 1;
            SetValue(_min + pct * (_max - _min), true);
        }

        public void SetValue(double v, bool raise)
        {
            if (v < _min) v = _min;
            if (v > _max) v = _max;
            if (Math.Abs(v - _value) < 1e-9) return;
            _value = v;
            UpdateVisual();
            if (raise && _onChange != null) _onChange(_value);
        }

        private void UpdateVisual()
        {
            double w = ActualWidth;
            if (w <= 0) return;
            double span = _max - _min;
            double pct = span <= 0 ? 0 : (_value - _min) / span;
            if (pct < 0) pct = 0;
            if (pct > 1) pct = 1;

            double x = 8 + pct * Math.Max(1, w - 16);
            _fill.Width = Math.Max(0, x);
            _thumb.Margin = new Thickness(x - 8, 0, 0, 0);
        }
    }

    /// <summary>可录制的快捷键输入框：点一下，按下组合键即可。</summary>
    internal class HotkeyBox : Border
    {
        private readonly Palette _pal;
        private readonly TextBlock _text;
        private readonly Action<int, int> _onChange;
        private int _mods, _vk;
        private bool _recording;

        public HotkeyBox(Palette pal, int mods, int vk, Action<int, int> onChange)
        {
            _pal = pal;
            _mods = mods;
            _vk = vk;
            _onChange = onChange;

            CornerRadius = new CornerRadius(Sx.RButton(pal) + 2);
            BorderThickness = new Thickness(1);
            Padding = new Thickness(12, 6, 12, 6);
            Cursor = Cursors.Hand;
            Focusable = true;
            MinWidth = 120;
            ToolTip = "点击后按下组合键；右键清除";

            _text = Sx.Txt("", 12, pal.Text);
            _text.HorizontalAlignment = HorizontalAlignment.Center;
            Child = _text;

            MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                StartRecording();
            };
            MouseRightButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                _recording = false;
                _mods = 0;
                _vk = 0;
                UpdateVisual();
                if (_onChange != null) _onChange(0, 0);
            };
            PreviewKeyDown += OnKeyDown;
            LostKeyboardFocus += delegate
            {
                if (!_recording) return;
                _recording = false;
                UpdateVisual();
            };

            UpdateVisual();
        }

        private void StartRecording()
        {
            _recording = true;
            Focus();
            Keyboard.Focus(this);
            UpdateVisual();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (!_recording) return;

            Key k = e.Key == Key.System ? e.SystemKey : e.Key;
            if (Hotkey.IsModifier(k)) { e.Handled = true; return; }   // 还没按主键

            e.Handled = true;

            if (k == Key.Escape)
            {
                _recording = false;
                UpdateVisual();
                return;
            }

            int mods = Hotkey.ModsFromKeyboard();
            if (mods == 0)
            {
                _text.Text = "需要 Ctrl / Alt / Shift / Win";
                return;
            }

            _mods = mods;
            _vk = KeyInterop.VirtualKeyFromKey(k);
            _recording = false;
            UpdateVisual();
            if (_onChange != null) _onChange(_mods, _vk);
        }

        private void UpdateVisual()
        {
            Background = _pal.Subtle;
            if (_recording)
            {
                _text.Text = "按下组合键…";
                _text.Foreground = _pal.TextMuted;
                BorderBrush = _pal.Accent;
            }
            else
            {
                _text.Text = Hotkey.Text(_mods, _vk);
                _text.Foreground = _vk > 0 ? _pal.Text : _pal.TextMuted;
                BorderBrush = _vk > 0 ? _pal.Accent : _pal.CardBorder;
            }
        }
    }

    /// <summary>分段选择器。</summary>
    internal class Segmented : Border
    {
        private readonly List<Border> _pills = new List<Border>();
        private readonly List<TextBlock> _labels = new List<TextBlock>();
        private readonly Palette _pal;
        private readonly Action<int> _onChange;
        private int _index;

        public int SelectedIndex { get { return _index; } }

        public Segmented(Palette pal, string[] items, int selected, Action<int> onChange)
        {
            _pal = pal;
            _onChange = onChange;
            _index = selected;

            CornerRadius = new CornerRadius(Sx.RSegment(pal));
            Background = pal.Subtle;
            Padding = new Thickness(3);
            VerticalAlignment = VerticalAlignment.Center;

            var grid = new Grid();
            for (int i = 0; i < items.Length; i++) grid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int i = 0; i < items.Length; i++)
            {
                int idx = i;
                var tb = Sx.Txt(items[i], 12, pal.TextMuted);
                tb.HorizontalAlignment = HorizontalAlignment.Center;
                tb.VerticalAlignment = VerticalAlignment.Center;

                var pill = new Border
                {
                    Child = tb,
                    CornerRadius = new CornerRadius(Sx.RPill(pal)),
                    Padding = new Thickness(12, 4, 12, 4),
                    Margin = new Thickness(1, 0, 1, 0),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand
                };
                pill.MouseLeftButtonUp += delegate { Select(idx, true); };
                grid.Children.Add(pill);
                Grid.SetColumn(pill, i);
                _pills.Add(pill);
                _labels.Add(tb);
            }

            Child = grid;
            Select(selected, false);
        }

        private void Select(int idx, bool raise)
        {
            _index = idx;
            for (int i = 0; i < _pills.Count; i++)
            {
                bool on = i == idx;
                _pills[i].Background = on ? _pal.CardBg : Brushes.Transparent;
                _labels[i].Foreground = on ? _pal.Text : _pal.TextMuted;
                _labels[i].FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
            if (raise && _onChange != null) _onChange(idx);
        }
    }
}
