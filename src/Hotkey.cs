using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace Fudock
{
    /// <summary>全局快捷键的修饰键常量与显示文本。</summary>
    internal static class Hotkey
    {
        public const int MOD_ALT = 0x0001;
        public const int MOD_CONTROL = 0x0002;
        public const int MOD_SHIFT = 0x0004;
        public const int MOD_WIN = 0x0008;

        public static bool IsModifier(Key k)
        {
            return k == Key.LeftCtrl || k == Key.RightCtrl
                || k == Key.LeftAlt || k == Key.RightAlt
                || k == Key.LeftShift || k == Key.RightShift
                || k == Key.LWin || k == Key.RWin;
        }

        public static string KeyName(int vk)
        {
            if (vk <= 0) return "";
            try
            {
                Key k = KeyInterop.KeyFromVirtualKey(vk);
                if (k >= Key.D0 && k <= Key.D9) return ((int)(k - Key.D0)).ToString();
                if (k >= Key.NumPad0 && k <= Key.NumPad9) return "小键盘" + ((int)(k - Key.NumPad0));

                switch (k)
                {
                    case Key.OemPlus: return "=";
                    case Key.OemMinus: return "-";
                    case Key.OemComma: return ",";
                    case Key.OemPeriod: return ".";
                    case Key.OemQuestion: return "/";
                    case Key.Space: return "Space";
                    case Key.Return: return "Enter";
                    case Key.Back: return "Backspace";
                    case Key.Oem3: return "`";
                    case Key.OemOpenBrackets: return "[";
                    case Key.OemCloseBrackets: return "]";
                    case Key.OemSemicolon: return ";";
                    case Key.OemQuotes: return "'";
                    case Key.OemPipe: return "\\";
                }
                return k.ToString();
            }
            catch { return "?"; }
        }

        /// <summary>把修饰键 + 主键拼成 "Ctrl + Alt + H"。</summary>
        public static string Text(int mods, int vk)
        {
            if (vk <= 0) return "未设置";

            var parts = new List<string>(5);
            if ((mods & MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((mods & MOD_ALT) != 0) parts.Add("Alt");
            if ((mods & MOD_SHIFT) != 0) parts.Add("Shift");
            if ((mods & MOD_WIN) != 0) parts.Add("Win");
            parts.Add(KeyName(vk));
            return string.Join(" + ", parts.ToArray());
        }

        public static int ModsFromKeyboard()
        {
            int mods = 0;
            ModifierKeys m = Keyboard.Modifiers;
            if ((m & ModifierKeys.Control) != 0) mods |= MOD_CONTROL;
            if ((m & ModifierKeys.Alt) != 0) mods |= MOD_ALT;
            if ((m & ModifierKeys.Shift) != 0) mods |= MOD_SHIFT;
            if ((m & ModifierKeys.Windows) != 0) mods |= MOD_WIN;
            return mods;
        }
    }
}
