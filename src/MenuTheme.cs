using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Fudock
{
    /// <summary>给右键菜单套一套和 Dock 一致的 Fluent 样式（圆角卡片 + 悬停高亮 + 勾选/子菜单箭头）。</summary>
    internal static class MenuTheme
    {
        private static bool _built;
        private static bool _dark;
        private static Style _menuStyle, _itemStyle, _separatorStyle;

        /// <summary>把样式挂到菜单上（同一主题下模板只解析一次）。</summary>
        public static void Apply(ContextMenu menu, Palette pal)
        {
            EnsureBuilt(pal);

            if (_menuStyle == null)
            {
                // 模板解析失败时的兜底：至少把配色对上
                menu.Background = pal.IsDark
                    ? new SolidColorBrush(Color.FromArgb(0xFF, 0x2B, 0x2B, 0x33))
                    : new SolidColorBrush(Colors.White);
                menu.Foreground = pal.Text;
                menu.BorderBrush = pal.BarBorder;
                menu.BorderThickness = new Thickness(1);
                menu.Padding = new Thickness(4);
                return;
            }

            menu.Resources[typeof(MenuItem)] = _itemStyle;
            menu.Resources[typeof(Separator)] = _separatorStyle;
            menu.Resources[MenuItem.SeparatorStyleKey] = _separatorStyle;
            menu.Style = _menuStyle;
            menu.HasDropShadow = false;
        }

        /// <summary>菜单打开前调用：套容器样式，并把样式递归铺到所有项（含子菜单）。</summary>
        public static void ApplyTo(ContextMenu menu, Palette pal)
        {
            Apply(menu, pal);
            if (_itemStyle == null) return;
            foreach (object item in menu.Items) AdoptDeep(item);
        }

        private static void AdoptDeep(object item)
        {
            var mi = item as MenuItem;
            if (mi != null)
            {
                mi.Style = _itemStyle;
                foreach (object child in mi.Items) AdoptDeep(child);
                return;
            }

            var sep = item as Separator;
            if (sep != null && _separatorStyle != null) sep.Style = _separatorStyle;
        }

        private static void EnsureBuilt(Palette pal)
        {
            if (_built && _dark == pal.IsDark) return;
            _built = true;
            _dark = pal.IsDark;

            string bg, border, hover, divider, text, muted, accent;
            if (pal.IsDark)
            {
                // 不透明底色：菜单颜色不随背景（壁纸/窗口）变化，主菜单和子菜单永远一致
                bg = "#FF2B2B33";
                border = "#33FFFFFF";
                hover = "#2EFFFFFF";
                divider = "#22FFFFFF";
                text = "#F2F2F7";
                muted = "#A6ACB6";
                accent = "#4CC2FF";
            }
            else
            {
                bg = "#FFFFFFFF";
                border = "#1C000000";
                hover = "#14000000";
                divider = "#16000000";
                text = "#1A1A1F";
                muted = "#6A6A75";
                accent = "#0067C0";
            }

            try
            {
                _menuStyle = BuildMenuStyle(bg, border, text);
                _itemStyle = BuildItemStyle(bg, border, hover, divider, text, muted, accent);
                _separatorStyle = BuildSeparatorStyle(divider);
            }
            catch (Exception ex)
            {
                SettingsStore.Log("MenuTheme build failed: " + ex.Message);
                _menuStyle = _itemStyle = _separatorStyle = null;
            }
        }

        // ------------------------------------------------------------------
        private static Style BuildMenuStyle(string bg, string border, string text)
        {
            string xaml = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                 TargetType='{x:Type ContextMenu}'>
  <Grid>
    <Border Margin='10' CornerRadius='11'
            Background='" + bg + @"'
            BorderBrush='" + border + @"' BorderThickness='1'>
      <Border.Effect>
        <DropShadowEffect BlurRadius='20' ShadowDepth='4' Direction='270' Opacity='0.40' Color='Black'/>
      </Border.Effect>
    </Border>
    <Border Margin='10' CornerRadius='11'
            Background='" + bg + @"'
            BorderBrush='" + border + @"' BorderThickness='1'>
      <ScrollViewer Padding='5' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'>
        <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle'/>
      </ScrollViewer>
    </Border>
  </Grid>
</ControlTemplate>";

            var style = new Style(typeof(ContextMenu));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Freeze(text)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.TemplateProperty, Parse(xaml)));
            return style;
        }

        private static Style BuildItemStyle(string bg, string border, string hover,
                                            string divider, string text, string muted, string accent)
        {
            string xaml = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                 TargetType='{x:Type MenuItem}'>
  <Grid SnapsToDevicePixels='True'>
    <Border x:Name='Bg' CornerRadius='6' Background='Transparent' Margin='2,0,2,0'/>
    <Grid Margin='10,0,8,0' Height='31'>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width='18'/>
        <ColumnDefinition Width='*'/>
        <ColumnDefinition Width='Auto'/>
        <ColumnDefinition Width='12'/>
      </Grid.ColumnDefinitions>
      <TextBlock x:Name='Check' Grid.Column='0' Text='&#x2713;' FontSize='11.5'
                 Foreground='" + accent + @"' VerticalAlignment='Center' Visibility='Collapsed'/>
      <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True'
                        VerticalAlignment='Center' Margin='0,0,6,0'/>
      <TextBlock Grid.Column='2' Text='{TemplateBinding InputGestureText}' FontSize='11.5'
                 Foreground='" + muted + @"' VerticalAlignment='Center' Margin='14,0,0,0'/>
      <Path Grid.Column='3' x:Name='Arrow' Data='M0,0 L4.2,4.2 L0,8.4'
            Stroke='" + muted + @"' StrokeThickness='1.4'
            VerticalAlignment='Center' HorizontalAlignment='Right' Visibility='Collapsed'/>
    </Grid>
    <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='-4' VerticalOffset='-8'
           IsOpen='{TemplateBinding IsSubmenuOpen}' AllowsTransparency='True'
           Focusable='False' PopupAnimation='Fade'>
      <Grid>
        <Border Margin='10' CornerRadius='11'
                Background='" + bg + @"'
                BorderBrush='" + border + @"' BorderThickness='1'>
          <Border.Effect>
            <DropShadowEffect BlurRadius='20' ShadowDepth='4' Direction='270' Opacity='0.40' Color='Black'/>
          </Border.Effect>
        </Border>
        <Border Margin='10' CornerRadius='11'
                Background='" + bg + @"'
                BorderBrush='" + border + @"' BorderThickness='1'>
          <ScrollViewer Padding='5' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'>
            <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle'/>
          </ScrollViewer>
        </Border>
      </Grid>
    </Popup>
  </Grid>
  <ControlTemplate.Triggers>
    <Trigger Property='IsHighlighted' Value='True'>
      <Setter TargetName='Bg' Property='Background' Value='" + hover + @"'/>
    </Trigger>
    <Trigger Property='IsChecked' Value='True'>
      <Setter TargetName='Check' Property='Visibility' Value='Visible'/>
    </Trigger>
    <Trigger Property='Role' Value='SubmenuHeader'>
      <Setter TargetName='Arrow' Property='Visibility' Value='Visible'/>
    </Trigger>
    <Trigger Property='IsEnabled' Value='False'>
      <Setter Property='Opacity' Value='0.42'/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";

            var style = new Style(typeof(MenuItem));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Freeze(text)));
            style.Setters.Add(new Setter(Control.TemplateProperty, Parse(xaml)));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
            return style;
        }

        private static Style BuildSeparatorStyle(string divider)
        {
            string xaml = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                 TargetType='{x:Type Separator}'>
  <Border Height='1' Margin='10,5,10,5' Background='" + divider + @"'/>
</ControlTemplate>";

            var style = new Style(typeof(Separator));
            style.Setters.Add(new Setter(Control.TemplateProperty, Parse(xaml)));
            return style;
        }

        private static ControlTemplate Parse(string xaml)
        {
            return (ControlTemplate)XamlReader.Parse(xaml);
        }

        private static Brush Freeze(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
    }
}
