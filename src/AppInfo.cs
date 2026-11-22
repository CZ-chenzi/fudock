namespace Fudock
{
    /// <summary>产品身份：只在这里改名字/版本，窗口标题、托盘、设置面板都跟着变。</summary>
    internal static class AppInfo
    {
        public const string ChineseName = "浮坞";
        public const string EnglishName = "Fudock";  // 程序标识 / exe 名
        public const string Version = "1.0.0";
        public const string Author = "cz_chenzi";

        /// <summary>当前语言的显示名：浮坞 / Fudock。</summary>
        public static string Name { get { return L.En ? EnglishName : ChineseName; } }

        /// <summary>一句话定位（设置面板副标题用）。</summary>
        public static string Tagline
        {
            get
            {
                return L.En
                    ? "an app dock that lives on the desktop layer"
                    : "贴在桌面层上的应用栏";
            }
        }

        /// <summary>“浮坞 1.0.0” / “Fudock 1.0.0”，用于标题栏与关于信息。</summary>
        public static string FullName { get { return Name + " " + Version; } }
    }
}
