using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace Fudock
{
    /// <summary>配色（跟随 Windows 11 深浅色）。</summary>
    public class Palette
    {
        public Brush BarBackground;
        public Brush BarBorder;
        public Brush Dot;
        public Brush DotIdle;
        public Brush HoverFill;
        public Brush HoverBorder;
        public Brush Text;
        public Color LetterBg;
        public Color LetterFg;
        public bool IsDark;
        public double CornerRadius = 16;
        public double UiRadius = 12;   // 设置界面圆角半径（0 = 直角）

        // ---- 设置面板（Fluent 卡片风格）用的颜色 ----
        public Brush WindowBg;
        public Brush CardBg;
        public Brush CardBorder;
        public Brush Subtle;        // 滑轨 / 未选中分段底色
        public Brush SubtleHover;   // 悬停底色
        public Brush TextMuted;     // 次要文字
        public Brush Accent;        // 强调色（滑块填充 / 开关开启）
        public Brush OnAccent;      // 强调色上的文字
        public Brush Divider;
        public Color WindowColor;   // 窗体基色（不带 alpha，配合系统材质用）

        // ---- 玻璃拟态（BackdropKind.Glass）----
        public bool Glass;
        public Brush GlassCardBg;
        public Brush GlassBorder;

        public static bool SystemUsesLightTheme()
        {
            try
            {
                object v = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", null);
                if (v is int) return (int)v != 0;
            }
            catch { }
            return false;
        }

        public static Palette Create(DockSettings s)
        {
            Palette p = Build(ResolveLight(s.Theme), s.BarOpacity);
            p.CornerRadius = Math.Max(10, Math.Round(s.IconSize * 0.34));
            return p;
        }

        /// <summary>设置界面自己的一套配色：深浅/透明度/圆角都独立于 Dock。</summary>
        public static Palette CreateUi(DockSettings s)
        {
            bool light = s.UiTheme == DockTheme.Auto
                ? ResolveLight(s.Theme)
                : s.UiTheme == DockTheme.Light;

            Palette p = Build(light, s.UiOpacity);
            p.CornerRadius = s.UiRadius;
            p.UiRadius = s.UiRadius;

            if (s.UiBackdrop == BackdropKind.Glass)
            {
                p.Glass = true;
                // 玻璃卡片：薄薄一层白 + 1px 白内亮边
                p.GlassCardBg = light
                    ? new SolidColorBrush(Color.FromArgb(0x6E, 0xFF, 0xFF, 0xFF))
                    : new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
                p.GlassBorder = light
                    ? new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF))
                    : new SolidColorBrush(Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF));
            }
            return p;
        }

        public static bool ResolveLight(DockTheme t)
        {
            if (t == DockTheme.Light) return true;
            if (t == DockTheme.Dark) return false;
            return SystemUsesLightTheme();
        }

        private static Palette Build(bool light, double alpha)
        {
            byte a = (byte)Math.Round(255 * Math.Min(1.0, Math.Max(0.2, alpha)));
            var p = new Palette();
            p.IsDark = !light;
            p.CornerRadius = 16;

            if (!light)
            {
                p.BarBackground = new LinearGradientBrush(
                    Color.FromArgb(a, 0x2A, 0x2A, 0x33),
                    Color.FromArgb(a, 0x17, 0x17, 0x1C), 90);
                p.BarBorder = new SolidColorBrush(Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF));
                p.Dot = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF));
                p.DotIdle = new SolidColorBrush(Color.FromArgb(0x4A, 0xFF, 0xFF, 0xFF));
                p.HoverFill = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
                p.HoverBorder = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
                p.Text = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF7));
                p.LetterBg = Color.FromRgb(0x36, 0x36, 0x42);
                p.LetterFg = Color.FromRgb(0xEC, 0xEC, 0xF4);

                p.WindowBg = new SolidColorBrush(Color.FromArgb(a, 0x1E, 0x1E, 0x24));
                p.CardBg = new SolidColorBrush(Color.FromArgb(a, 0x2A, 0x2A, 0x32));
                p.CardBorder = new SolidColorBrush(Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF));
                p.Subtle = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
                p.SubtleHover = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
                p.TextMuted = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
                p.Accent = new SolidColorBrush(Color.FromRgb(0x4C, 0xC2, 0xFF));
                p.OnAccent = new SolidColorBrush(Color.FromRgb(0x08, 0x1E, 0x2C));
                p.Divider = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
                p.WindowColor = Color.FromRgb(0x1E, 0x1E, 0x24);
            }
            else
            {
                p.BarBackground = new LinearGradientBrush(
                    Color.FromArgb(a, 0xFC, 0xFC, 0xFE),
                    Color.FromArgb(a, 0xEA, 0xEA, 0xF0), 90);
                p.BarBorder = new SolidColorBrush(Color.FromArgb(0x24, 0x00, 0x00, 0x00));
                p.Dot = new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xC0));
                p.DotIdle = new SolidColorBrush(Color.FromArgb(0x38, 0x00, 0x00, 0x00));
                p.HoverFill = new SolidColorBrush(Color.FromArgb(0x14, 0x00, 0x00, 0x00));
                p.HoverBorder = new SolidColorBrush(Color.FromArgb(0x20, 0x00, 0x00, 0x00));
                p.Text = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1F));
                p.LetterBg = Color.FromRgb(0xD8, 0xD8, 0xE0);
                p.LetterFg = Color.FromRgb(0x2E, 0x2E, 0x36);

                p.WindowBg = new SolidColorBrush(Color.FromArgb(a, 0xF3, 0xF3, 0xF7));
                p.CardBg = new SolidColorBrush(Color.FromArgb(a, 0xFF, 0xFF, 0xFF));
                p.CardBorder = new SolidColorBrush(Color.FromArgb(0x18, 0x00, 0x00, 0x00));
                p.Subtle = new SolidColorBrush(Color.FromArgb(0x1F, 0x00, 0x00, 0x00));
                p.SubtleHover = new SolidColorBrush(Color.FromArgb(0x12, 0x00, 0x00, 0x00));
                p.TextMuted = new SolidColorBrush(Color.FromArgb(0xA6, 0x1A, 0x1A, 0x1F));
                p.Accent = new SolidColorBrush(Color.FromRgb(0x00, 0x67, 0xC0));
                p.OnAccent = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                p.Divider = new SolidColorBrush(Color.FromArgb(0x14, 0x00, 0x00, 0x00));
                p.WindowColor = Color.FromRgb(0xF3, 0xF3, 0xF7);
            }
            return p;
        }
    }

    /// <summary>Dock 上一个图标格的视觉元素。</summary>
    internal class DockTileView : Grid
    {
        public DockTile Model;
        public readonly double IconSize;
        private readonly Grid _stack;
        private readonly Border _hl;
        private readonly Image _img;
        private readonly Ellipse _dot;
        private readonly TextBlock _letter;
        private readonly Image _reflect;              // 图标倒影（镜像 + 渐变淡出）
        private readonly Grid _reflectHost;
        private Palette _pal;
        private double _scale = 1;
        private bool _hover;

        // 拖动排序：_shift 用于"让位"位移动画，_lift* 用于把抓起的图标抬起放大
        private readonly TranslateTransform _shift = new TranslateTransform();
        private readonly ScaleTransform _liftScale = new ScaleTransform(1, 1);
        private readonly TranslateTransform _liftMove = new TranslateTransform(0, 0);

        public DockTileView(DockTile model, double iconSize, Palette pal, double dotH, double reflectRoom = 0)
        {
            Model = model;
            IconSize = iconSize;
            _pal = pal;
            // 开了倒影就多留出"倒影高度"的空间，避免倒影压到运行点或条底边
            Height = iconSize + dotH + Math.Max(0, reflectRoom);
            VerticalAlignment = VerticalAlignment.Center;
            Background = Brushes.Transparent;
            Cursor = System.Windows.Input.Cursors.Hand;

            _stack = new Grid
            {
                Width = iconSize,
                Height = iconSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Background = Brushes.Transparent,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };

            _hl = new Border
            {
                CornerRadius = new CornerRadius(iconSize * 0.24),
                Background = Brushes.Transparent
            };
            _stack.Children.Add(_hl);

            _img = new Image
            {
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true
            };
            RenderOptions.SetBitmapScalingMode(_img, BitmapScalingMode.HighQuality);
            _stack.Children.Add(_img);

            _letter = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            _stack.Children.Add(_letter);

            Children.Add(_stack);

            // 倒影：把同一张图标镜像后塞在图标正下方，用透明度渐变 + 可选模糊做淡出。
            // 只画在图标格里，系统区/弹层完全不受影响。
            _reflect = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            RenderOptions.SetBitmapScalingMode(_reflect, BitmapScalingMode.HighQuality);

            _reflectHost = new Grid
            {
                Height = 0,
                ClipToBounds = true,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            _reflectHost.Children.Add(_reflect);
            Children.Add(_reflectHost);

            // 整格的变换链：位移（让位动画）+ 抬起（拖动时放大上浮）
            RenderTransformOrigin = new Point(0.5, 1.0);
            var tileTransform = new TransformGroup();
            tileTransform.Children.Add(_shift);
            tileTransform.Children.Add(_liftScale);
            tileTransform.Children.Add(_liftMove);
            RenderTransform = tileTransform;

            _dot = new Ellipse
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, Math.Max(1, dotH * 0.16))
            };
            Children.Add(_dot);

            ApplyPalette(pal);
        }

        public void ApplyPalette(Palette pal)
        {
            _pal = pal;
            if (Model.Icon == null)
            {
                _letter.Text = string.IsNullOrEmpty(Model.DisplayName)
                    ? "?" : Model.DisplayName.Substring(0, 1).ToUpperInvariant();
                _letter.Visibility = Visibility.Visible;
                _letter.FontSize = IconSize * 0.44;
                _letter.Foreground = new SolidColorBrush(pal.LetterFg);
                _hl.CornerRadius = new CornerRadius(IconSize * 0.26);
            }
            else
            {
                _img.Source = Model.Icon;
                _letter.Visibility = Visibility.Collapsed;
            }

            double d = Math.Max(4, IconSize * 0.105);
            _dot.Width = d;
            _dot.Height = d;

            // 固定项即使没运行也给一颗很暗的点；纯运行项运行才有
            bool showDot = Model.IsRunning || Model.Pinned;
            _dot.Visibility = showDot ? Visibility.Visible : Visibility.Collapsed;
            _dot.Fill = Model.IsRunning ? pal.Dot : pal.DotIdle;
            _dot.Opacity = Model.IsRunning ? 1.0 : 0.75;
            SetHover(_hover);
        }

        /// <summary>
        /// 应用图标倒影。参数全部来自设置，0 就关掉（低配可以一键关）。
        /// 只影响 Dock 图标本身：倒影只画在这一格里，系统区和弹层不会跟着变。
        /// </summary>
        public void ApplyReflection(DockSettings s)
        {
            try
            {
                bool on = s != null && s.ReflectOn && s.ReflectStrength > 0.5 && s.ReflectHeight > 1;
                if (!on)
                {
                    _reflectHost.Visibility = Visibility.Collapsed;
                    _reflect.Visibility = Visibility.Collapsed;
                    _reflectHost.Height = 0;
                    _reflect.Source = null;
                    _reflect.Effect = null;
                    return;
                }

                double h = IconSize * (s.ReflectHeight / 100.0);      // 倒影显示多高
                if (h < 2)
                {
                    _reflectHost.Visibility = Visibility.Collapsed;
                    _reflect.Visibility = Visibility.Collapsed;
                    _reflectHost.Height = 0;
                    return;
                }

                _reflectHost.Visibility = Visibility.Visible;
                _reflect.Visibility = Visibility.Visible;   // 构造时是 Collapsed，必须显式打开
                _reflectHost.Height = h;
                _reflectHost.Margin = new Thickness(0, IconSize, 0, 0);   // 紧贴图标底部
                _reflectHost.Width = IconSize;
                _reflectHost.HorizontalAlignment = HorizontalAlignment.Center;

                _reflect.Source = Model.Icon;
                _reflect.Width = IconSize;
                _reflect.Height = IconSize;

                // 镜像：**绕自身中心**翻转（在同一个盒子里原地镜像）。
                // 绕顶边翻会把整张图翻到盒子上面去，被 clip 掉 —— 结果就是"倒了但看不见"。
                _reflect.RenderTransformOrigin = new Point(0.5, 0.5);
                _reflect.RenderTransform = new ScaleTransform(1.0, -1.0);

                // 淡出：从顶部（靠近图标处）到底部逐渐透明，Fade 越大越早开始淡
                double fade = Math.Min(0.98, Math.Max(0.05, s.ReflectFade / 100.0));
                var mask = new LinearGradientBrush
                {
                    StartPoint = new Point(0.5, 1.0),    // 注意：控件已翻转，视觉上"上浓下淡"
                    EndPoint = new Point(0.5, 0.0)
                };
                byte a = (byte)Math.Round(255 * Math.Min(1.0, s.ReflectStrength / 100.0));
                mask.GradientStops.Add(new GradientStop(Color.FromArgb(a, 255, 255, 255), 0.0));
                mask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), fade));
                _reflect.OpacityMask = mask;

                if (s.ReflectBlur > 0.15)
                {
                    _reflect.Effect = new BlurEffect
                    {
                        Radius = s.ReflectBlur,
                        KernelType = KernelType.Gaussian,
                        RenderingBias = RenderingBias.Performance   // 低配优先：性能档
                    };
                }
                else _reflect.Effect = null;
            }
            catch { }
        }

        public void SetHover(bool on)
        {
            _hover = on;
            if (_pal == null) return;
            if (on) _hl.Background = _pal.HoverFill;
            else _hl.Background = Model.Icon == null
                ? new SolidColorBrush(_pal.LetterBg) : Brushes.Transparent;
        }

        public void SetScale(double scale)
        {
            if (Math.Abs(scale - _scale) < 0.0015) return;
            _scale = scale;
            var g = new TransformGroup();
            g.Children.Add(new ScaleTransform(scale, scale));
            g.Children.Add(new TranslateTransform(0, -(IconSize * (scale - 1)) / 2.0));
            _stack.RenderTransform = g;
        }

        public double Scale { get { return _scale; } }

        /// <summary>拖动时把图标抬起来（放大 + 上浮）。</summary>
        public void SetLift(bool on)
        {
            _liftScale.ScaleX = on ? 1.16 : 1.0;
            _liftScale.ScaleY = on ? 1.16 : 1.0;
            _liftMove.Y = on ? -10 : 0;
        }

        /// <summary>让位动画：从旧位置滑到新位置（dx 是旧位置相对新位置的偏移）。</summary>
        public void SlideFrom(double dx)
        {
            _shift.BeginAnimation(TranslateTransform.XProperty, null);
            if (Math.Abs(dx) < 0.5)
            {
                _shift.X = 0;
                return;
            }
            _shift.X = 0;
            var anim = new DoubleAnimation(dx, 0, TimeSpan.FromMilliseconds(170))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            _shift.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        public void ResetShift()
        {
            _shift.BeginAnimation(TranslateTransform.XProperty, null);
            _shift.X = 0;
        }

        /// <summary>当前让位位移（用于把"视觉位置"还原成"布局位置"）。</summary>
        public double ShiftX { get { return _shift.X; } }
    }

    /// <summary>分隔条。</summary>
    internal class DockSeparatorView : Border
    {
        public DockSeparatorView(double iconSize, Palette pal)
        {
            Width = 1;
            Height = iconSize * 0.62;
            Margin = new Thickness(Math.Max(4, iconSize * 0.13), 0, Math.Max(4, iconSize * 0.13), 0);
            VerticalAlignment = VerticalAlignment.Center;
            Background = pal.IsDark
                ? new SolidColorBrush(Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF))
                : new SolidColorBrush(Color.FromArgb(0x28, 0x00, 0x00, 0x00));
        }
    }
}
