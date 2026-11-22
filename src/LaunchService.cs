using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Fudock
{
    /// <summary>启动应用 / 打开协议 / 定位文件。</summary>
    internal static class LaunchService
    {
        public static bool Launch(string target, string args)
        {
            if (string.IsNullOrEmpty(target)) return false;
            try
            {
                // UWP / 协议 / shell 目标
                if (IsShellTarget(target))
                {
                    Process.Start(new ProcessStartInfo(target)
                    {
                        UseShellExecute = true,
                        Arguments = args ?? ""
                    });
                    return true;
                }

                string exe = target;
                if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    // 快捷方式交给 shell，保留工作目录/参数
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                    return true;
                }

                if (!File.Exists(exe)) return false;

                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
                };
                if (!string.IsNullOrEmpty(args)) psi.Arguments = args;
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                SettingsStore.Log("Launch failed: " + target + " -> " + ex.Message);
                return false;
            }
        }

        private static bool IsShellTarget(string s)
        {
            if (s.IndexOf('\\') >= 0 || s.IndexOf('/') >= 0) return !File.Exists(s);
            int colon = s.IndexOf(':');
            return colon > 0; // 形如 ms-settings: / shell:AppsFolder\...
        }

        public static void ShowInExplorer(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                if (File.Exists(path))
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else if (Directory.Exists(path))
                    Process.Start("explorer.exe", "\"" + path + "\"");
                else
                    Process.Start("explorer.exe");
            }
            catch (Exception ex) { SettingsStore.Log("ShowInExplorer: " + ex.Message); }
        }

        /// <summary>首次运行的默认固定项：只收录真实存在的常用应用。</summary>
        public static List<PinnedEntry> BuildDefaults()
        {
            var list = new List<PinnedEntry>();

            // 1) 系统必备项
            AddIfExists(list, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "文件资源管理器");
            AddIfExists(list, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe"), "记事本");
            AddIfExists(list, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "cmd.exe"), "命令提示符");

            // 2) 开始菜单里能匹配到的常用应用
            string[] whitelist =
            {
                "Microsoft Edge", "Google Chrome", "Firefox",
                "Word", "Excel", "PowerPoint", "OneNote", "Outlook",
                "Visual Studio Code", "Visual Studio", "微信", "WeChat", "QQ",
                "网易云音乐", "Spotify", "Steam", "Photoshop", "钉钉", "DingTalk",
                "WPS Office", "Typora", "Notion", "Obsidian", "Telegram", "Discord",
                "PotPlayer", "Sublime Text", "PyCharm", "IDEA", "IntelliJ IDEA", "HBuilderX"
            };

            var found = new List<PinnedEntry>();
            foreach (string root in StartMenuRoots())
            {
                if (!Directory.Exists(root)) continue;
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories); }
                catch { continue; }

                foreach (string lnk in files)
                {
                    string name = Path.GetFileNameWithoutExtension(lnk);
                    bool hit = false;
                    foreach (string w in whitelist)
                    {
                        if (name.Equals(w, StringComparison.OrdinalIgnoreCase) ||
                            name.StartsWith(w + " ", StringComparison.OrdinalIgnoreCase))
                        { hit = true; break; }
                    }
                    if (!hit) continue;
                    foreach (var e in list) if (e.Name == name) { hit = false; break; }
                    foreach (var e in found) if (e.Name == name) { hit = false; break; }
                    if (!hit) continue;
                    found.Add(new PinnedEntry { Path = lnk, Name = name, Args = "" });
                }
            }

            found.Sort(delegate (PinnedEntry a, PinnedEntry b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            for (int i = 0; i < found.Count && i < 6; i++) list.Add(found[i]);

            return list;
        }

        private static IEnumerable<string> StartMenuRoots()
        {
            var roots = new List<string>();
            try
            {
                roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"));
                roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"));
            }
            catch { }
            return roots;
        }

        private static void AddIfExists(List<PinnedEntry> list, string path, string name)
        {
            try { if (File.Exists(path)) list.Add(new PinnedEntry { Path = path, Name = name, Args = "" }); }
            catch { }
        }

        /// <summary>从任意文件路径构造一个固定项（exe / lnk）。</summary>
        public static PinnedEntry FromFile(string file, string displayName = null)
        {
            if (string.IsNullOrEmpty(file)) return null;
            file = file.Trim().Trim('"');
            string name = displayName;

            if (name == null)
            {
                if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    name = Path.GetFileNameWithoutExtension(file);
                else
                    name = Path.GetFileNameWithoutExtension(file);
            }

            return new PinnedEntry { Path = file, Name = name, Args = "" };
        }

        public static string MatchKey(PinnedEntry e)
        {
            if (e == null || string.IsNullOrEmpty(e.Path)) return "";
            string p = e.Path;
            if (p.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                p = IconService.ResolveShortcut(p);
            return (p ?? "").Trim().ToLowerInvariant();
        }
    }
}
