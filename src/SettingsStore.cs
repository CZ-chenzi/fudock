using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace Fudock
{
    /// <summary>设置读写（%AppData%\Fudock\settings.json）。</summary>
    internal static class SettingsStore
    {
        public static readonly string AppDir = ResolveAppDir();

        /// <summary>
        /// 1.0 之前这个程序叫 Win11Dock，配置放在 %AppData%\Win11Dock。
        /// 改名后老用户的图标栏、位置、外观要能原样接着用：新目录不存在而老目录存在时，
        /// 把 settings.json 复制过来（老目录原样保留，出问题还能回去）。
        /// </summary>
        private static string ResolveAppDir()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "Fudock");
            try
            {
                if (!Directory.Exists(dir))
                {
                    string legacy = Path.Combine(appData, "Win11Dock");
                    if (Directory.Exists(legacy))
                    {
                        Directory.CreateDirectory(dir);
                        string src = Path.Combine(legacy, "settings.json");
                        if (File.Exists(src))
                        {
                            File.Copy(src, Path.Combine(dir, "settings.json"), false);
                            // 这里 AppDir 还没赋值完，不能走 Log()，直接写文件。
                            File.AppendAllText(Path.Combine(dir, "error.log"),
                                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                                + "  已从旧目录 %AppData%\\Win11Dock 迁移配置" + Environment.NewLine,
                                System.Text.Encoding.UTF8);
                        }
                    }
                }
            }
            catch { }
            return dir;
        }

        public static readonly string SettingsPath = Path.Combine(AppDir, "settings.json");
        public static readonly string LogPath = Path.Combine(AppDir, "error.log");

        /// <summary>上次 Load 是否失败（失败时不要立刻 Save，免得把用户配置覆盖成默认值）。</summary>
        public static bool LastLoadFailed;

        public static DockSettings Load()
        {
            LastLoadFailed = false;
            try
            {
                if (!File.Exists(SettingsPath)) return DockSettings.CreateDefault();
                string json = File.ReadAllText(SettingsPath);
                var ser = new JavaScriptSerializer();
                ser.MaxJsonLength = 4 * 1024 * 1024;
                var s = ser.Deserialize<DockSettings>(json);
                if (s == null) return DockSettings.CreateDefault();
                if (s.Pinned == null) s.Pinned = new List<PinnedEntry>();
                Migrate(s);
                s.Clamp();
                return s;
            }
            catch (Exception ex)
            {
                LastLoadFailed = true;
                Log("Load settings failed: " + ex.Message);
                // 出版级要求：**绝不能**把读不出来的用户配置直接覆盖掉。
                // 先把坏文件另存一份（带时间戳），让人有救回来的机会。
                try
                {
                    string keep = Path.Combine(AppDir,
                        "settings.broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                    File.Copy(SettingsPath, keep, true);
                    Log("坏配置已另存为 " + Path.GetFileName(keep));
                }
                catch (Exception ex2) { Log("备份坏配置失败: " + ex2.Message); }
                return DockSettings.CreateDefault();
            }
        }

        /// <summary>
        /// 老配置文件没有新字段时，数值型字段会反序列化成 0（不是默认值），
        /// 所以按版本号补一次默认。v1 -> v2：新增"设置界面"独立样式。
        /// </summary>
        private static void Migrate(DockSettings s)
        {
            if (s.Version >= 9) return;

            if (s.Version < 2)
            {
                // v1 -> v2：新增"设置界面"独立样式
                s.UiTheme = DockTheme.Auto;
                s.UiOpacity = 0.97;
                s.UiRadius = 12;
                s.Version = 2;
            }
            if (s.Version < 3)
            {
                // v2 -> v3：新增"自定义位置"
                s.Position = DockPosition.Edge;
                s.PosX = 0.5;
                s.PosY = 0.95;
                s.Version = 3;
            }
            if (s.Version < 4)
            {
                // v3 -> v4：新增背景材质预设
                s.UiBackdrop = BackdropKind.Frosted;
                s.Version = 4;
            }
            if (s.Version < 5)
            {
                // v4 -> v5：隐藏任务栏的全局快捷键
                s.HotkeyMods = 0x0003;   // Ctrl + Alt
                s.HotkeyVk = 0x48;       // H
                s.Version = 5;
            }
            if (s.Version < 6)
            {
                // v5 -> v6：Dock 末端的系统区（铃铛 / 状态胶囊 / 溢出入口）
                s.SystemArea = true;
                s.SystemCapsuleTime = true;
                s.SystemBadge = true;
                s.TrayOverflow = true;
                s.FullscreenHide = true;
                s.PanelAnim = true;
                s.PanelFirst = false;
                s.Version = 6;
            }
            if (s.Version < 7)
            {
                // v6 -> v7：图标倒影 + 桌面图标快捷键（默认关闭，不打扰老用户）
                s.ReflectOn = false;
                s.ReflectStrength = 24;
                s.ReflectHeight = 45;
                s.ReflectBlur = 3;
                s.ReflectFade = 70;

                s.Version = 7;
            }
            if (s.Version < 8)
            {
                // v7 -> v8：设置窗口可拖动改变大小，记住上次尺寸（0 = 自动）
                s.UiWidth = 0;
                s.UiHeight = 0;
                s.Version = 8;
            }
            if (s.Version < 9)
            {
                // v8 -> v9：界面语言（默认中文，不打扰老用户）
                s.Language = 0;
                s.Version = 9;
            }
            Save(s);
        }

        public static void Save(DockSettings s)
        {
            try
            {
                Directory.CreateDirectory(AppDir);
                var ser = new JavaScriptSerializer();
                ser.MaxJsonLength = 4 * 1024 * 1024;
                string json = ser.Serialize(s);

                // 原子写：先写 .tmp，再替换正式文件。
                // 直接 WriteAllText 一旦在写入过程中被杀进程，用户配置就变成半截 JSON、下次启动全丢。
                string tmp = SettingsPath + ".tmp";
                File.WriteAllText(tmp, json, System.Text.Encoding.UTF8);
                if (File.Exists(SettingsPath))
                {
                    try { File.Replace(tmp, SettingsPath, null); }
                    catch
                    {
                        File.Delete(SettingsPath);
                        File.Move(tmp, SettingsPath);
                    }
                }
                else File.Move(tmp, SettingsPath);
            }
            catch (Exception ex)
            {
                Log("Save settings failed: " + ex);
            }
        }

        public static void Log(string msg)
        {
            try
            {
                Directory.CreateDirectory(AppDir);
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine,
                    System.Text.Encoding.UTF8);
            }
            catch { }
        }
    }
}
