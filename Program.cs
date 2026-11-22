using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Fudock
{
    internal static class App
    {
        private static Mutex _mutex;
        private static DockWindow _dock;
        private static TrayService _tray;
        private static SettingsWindow _settingsWindow;
        private static System.Windows.Application _app;

        [STAThread]
        public static void Main(string[] args)
        {
            bool created;
            _mutex = new Mutex(true, "Global\\Fudock.SingleInstance", out created);
            if (!created)
            {
                return;   // 已有实例在运行
            }

            // 崩溃/被强杀也要尽量把任务栏还回去（发布版必须有这一层）
            AppDomain.CurrentDomain.UnhandledException += delegate { SafeRestoreTaskbar(); };
            AppDomain.CurrentDomain.ProcessExit += delegate { SafeRestoreTaskbar(); };

            try
            {
                _app = new System.Windows.Application();
                _app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _app.DispatcherUnhandledException += delegate (object s, DispatcherUnhandledExceptionEventArgs e)
                {
                    SettingsStore.Log("Unhandled: " + e.Exception);
                    e.Handled = true;
                };

                // 改名遗留：清掉旧名字写入的 Run 项（它指向旧 exe，开机启动会失败）
                StartupManager.CleanupLegacy();

                // 上次如果是藏着任务栏被强杀的，先把任务栏还回去，
                // 并把"隐藏任务栏"这个设置关掉——崩溃后让用户面对一个没有任务栏的桌面
                // 是最不可接受的失败模式，宁可让设置回到安全状态。
                bool recovered = TaskbarManager.RecoverIfLeftHidden();

                DockSettings settings = SettingsStore.Load();

                // 界面语言要在建任何窗口之前定下来（标题、菜单、托盘都靠它）
                // --lang=en / --lang=zh：命令行覆盖并落盘，方便测试与便携使用
                string langArg = ValueOfArg(args, "--lang");
                if (!string.IsNullOrEmpty(langArg))
                {
                    int lang = (langArg.StartsWith("e", StringComparison.OrdinalIgnoreCase) || langArg == "1")
                        ? L.English : L.Chinese;
                    if (settings.Language != lang)
                    {
                        settings.Language = lang;
                        if (!SettingsStore.LastLoadFailed) SettingsStore.Save(settings);
                    }
                }

                // 界面语言要在建任何窗口之前定下来（标题、菜单、托盘都靠它）
                L.Apply(settings.Language);

                if (recovered && settings.HideTaskbar)
                {
                    settings.HideTaskbar = false;
                    if (!SettingsStore.LastLoadFailed) SettingsStore.Save(settings);
                    SettingsStore.Log("已把 HideTaskbar 复位为 false（上次异常退出）");
                }
                if (SettingsStore.LastLoadFailed)
                {
                    // 配置读失败：用默认值先把界面跑起来，但**不要**立刻写回，
                    // 坏文件已经另存了一份，用户还有机会找回原来的设置。
                    SettingsStore.Log("配置读取失败，本次以默认设置运行（不会自动覆盖原文件）");
                    settings.Pinned = LaunchService.BuildDefaults();
                }
                else if (settings.Pinned.Count == 0)
                {
                    settings.Pinned = LaunchService.BuildDefaults();
                    settings.LaunchAtStartup = StartupManager.IsEnabled();
                    SettingsStore.Save(settings);
                }
                if (settings.LaunchAtStartup != StartupManager.IsEnabled())
                    StartupManager.SetEnabled(settings.LaunchAtStartup);

                _dock = new DockWindow(settings);
                _dock.Show();

                _tray = new TrayService(_dock);

                // Fudock.exe --settings 直接打开设置面板
                if (HasArg(args, "--settings") || HasArg(args, "-s"))
                {
                    ShowSettings(_dock);
                }

                _app.Run();
            }
            catch (Exception ex)
            {
                SettingsStore.Log("Fatal: " + ex);
                MessageBox.Show(L.T("浮坞 启动失败：\n") + ex.Message, L.T("浮坞"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>尽力把任务栏显示回来（任何退出路径都调用，失败静默）。</summary>
        private static void SafeRestoreTaskbar()
        {
            try { TaskbarManager.ForceShow(); }
            catch { }
        }

        public static DockWindow Dock { get { return _dock; } }

        /// <summary>托盘服务（语言切换后需要重排菜单）。</summary>
        public static TrayService Tray { get { return _tray; } }

        /// <summary>取 "--lang=en" 这种参数的值（没有则返回 null）。</summary>
        private static string ValueOfArg(string[] args, string name)
        {
            if (args == null) return null;
            foreach (string a in args)
            {
                if (a == null) continue;
                if (a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                    return a.Substring(name.Length + 1).Trim();
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return "";
            }
            return null;
        }

        private static bool HasArg(string[] args, string name)
        {
            if (args == null) return false;
            foreach (string a in args)
            {
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// 切换界面语言（0 = 中文，1 = English）。设置面板的芯片和 --lang 参数都走这里：
        /// 写盘 → 立刻 Apply → 刷新 Dock/托盘 → 设置面板按新语言重开。
        /// </summary>
        public static void SetLanguage(int lang)
        {
            try
            {
                if (_dock == null) return;
                DockSettings s = _dock.Settings;
                if (s.Language != lang) { s.Language = lang; SettingsStore.Save(s); }
                L.Apply(lang);
                _dock.RefreshLanguage();

                if (_settingsWindow != null)
                {
                    SettingsWindow old = _settingsWindow;
                    _settingsWindow = null;
                    try { old.Close(); } catch { }
                    ShowSettings(_dock);
                }
            }
            catch (Exception ex) { SettingsStore.Log("SetLanguage: " + ex.Message); }
        }

        public static void ShowSettings(DockWindow dock)
        {
            try
            {
                if (_settingsWindow != null)
                {
                    BringToFront(_settingsWindow);
                    return;
                }
                _settingsWindow = new SettingsWindow(dock);
                _settingsWindow.Closed += delegate { _settingsWindow = null; };
                _settingsWindow.Show();
                BringToFront(_settingsWindow);
            }
            catch (Exception ex)
            {
                SettingsStore.Log("ShowSettings: " + ex);
            }
        }

        /// <summary>抢到前台：Topmost + AttachThreadInput 强制激活，绕过 Windows 前台锁。</summary>
        private static void BringToFront(Window w)
        {
            try
            {
                w.Topmost = true;
                w.Activate();
                IntPtr h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                if (h != IntPtr.Zero) WindowService.Activate(h);

                // SizeToContent 的窗口首帧尺寸还没定，稍后再补一次，避免激活落空
                w.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    new Action(delegate
                    {
                        try
                        {
                            w.Topmost = true;
                            w.Activate();
                            IntPtr h2 = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                            if (h2 != IntPtr.Zero) WindowService.Activate(h2);
                        }
                        catch { }
                    }));
            }
            catch (Exception ex)
            {
                SettingsStore.Log("BringToFront: " + ex.Message);
            }
        }

        public static void Quit()
        {
            try
            {
                if (_dock != null) { _dock.RestoreTaskbar(); SafeRestoreTaskbar(); SettingsStore.Save(_dock.Settings); }
                if (_tray != null) { _tray.Dispose(); _tray = null; }
                if (_app != null) _app.Shutdown();
            }
            catch { }
            finally
            {
                Environment.Exit(0);
            }
        }
    }
}
