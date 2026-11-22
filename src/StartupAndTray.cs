using System;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Fudock
{
    /// <summary>开机自动启动（写 HKCU Run 项）。</summary>
    internal static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Fudock";
        private const string LegacyValueName = "Win11Dock";   // 改名前的自启项

        /// <summary>删掉旧名字留下的自启项（它指向旧 exe，改名后启动会失败）。</summary>
        public static void CleanupLegacy()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (k.GetValue(LegacyValueName) != null) k.DeleteValue(LegacyValueName, false);
                }
            }
            catch { }
        }

        public static void SetEnabled(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on)
                    {
                        string exe = Assembly.GetEntryAssembly().Location;
                        k.SetValue(ValueName, "\"" + exe + "\" --autostart");
                    }
                    else if (k.GetValue(ValueName) != null)
                    {
                        k.DeleteValue(ValueName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                SettingsStore.Log("StartupManager: " + ex.Message);
            }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    return k.GetValue(ValueName) != null;
                }
            }
            catch { return false; }
        }
    }

    /// <summary>托盘图标 + 菜单。</summary>
    internal sealed class TrayService : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly DockWindow _dock;
        private ContextMenuStrip _menu;

        /// <summary>把菜单项装进一个新的 ContextMenuStrip（切换语言后要重排）。</summary>
        private ContextMenuStrip CreateMenu()
        {
            var m = new ContextMenuStrip();
            m.Items.Add(L.T("显示 / 隐藏 Dock"), null, delegate { _dock.ToggleVisibility(); });
            m.Items.Add(L.T("显示 / 隐藏 Windows 任务栏"), null, delegate { _dock.ToggleTaskbar(); });
            m.Items.Add(L.T("自定义 Dock 位置…"), null, delegate { _dock.BeginPositionEdit(); });
            m.Items.Add(L.T("添加应用…"), null, delegate { _dock.AddAppsDialog(); });
            m.Items.Add(L.T("Dock 设置…"), null, delegate { App.ShowSettings(_dock); });
            m.Items.Add(new ToolStripSeparator());
            // 紧急出口：万一任务栏被藏起来又找不回来，托盘里永远有这一项
            m.Items.Add(L.T("紧急恢复：Windows 任务栏"), null, delegate { TaskbarManager.ForceShow(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(L.T("退出"), null, delegate { App.Quit(); });
            return m;
        }

        /// <summary>语言变了：重排菜单与提示文字，不重建托盘图标本身。</summary>
        public void Reload()
        {
            try
            {
                ContextMenuStrip old = _menu;
                _menu = CreateMenu();
                _icon.ContextMenuStrip = _menu;
                _icon.Text = AppInfo.Name;
                if (old != null) old.Dispose();
            }
            catch (Exception ex) { SettingsStore.Log("TrayService.Reload: " + ex.Message); }
        }

        public TrayService(DockWindow dock)
        {
            _dock = dock;

            _menu = CreateMenu();

            _icon = new NotifyIcon
            {
                Text = AppInfo.Name,
                ContextMenuStrip = _menu,
                Visible = true
            };

            try
            {
                _icon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                    Assembly.GetEntryAssembly().Location);
            }
            catch { }

            if (_icon.Icon == null)
            {
                try { _icon.Icon = System.Drawing.SystemIcons.Application; }
                catch { }
            }

            _icon.MouseClick += delegate (object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) _dock.ToggleVisibility();
            };
        }

        public void Dispose()
        {
            try
            {
                _icon.Visible = false;
                _icon.Dispose();
                _menu.Dispose();
            }
            catch { }
        }
    }
}
