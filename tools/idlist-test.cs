// Standalone test for the "Shell IDList Array" parser used by the dock's drop handler.
// Builds a real CIDA from a shell path and runs the exact parsing logic on it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

static class IdListTest
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHParseDisplayName(string name, IntPtr bindCtx, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
    [DllImport("shell32.dll")]
    static extern uint ILGetSize(IntPtr pidl);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder pszPath);
    [DllImport("ole32.dll")]
    static extern void CoTaskMemFree(IntPtr p);

    // ---- 与 DockWindow.GetShellIdListPaths 完全一致的解析逻辑 ----
    static string[] ParseIdList(byte[] buffer)
    {
        var result = new List<string>();
        if (buffer == null || buffer.Length < 8) return result.ToArray();

        IntPtr p = Marshal.AllocHGlobal(buffer.Length);
        try
        {
            Marshal.Copy(buffer, 0, p, buffer.Length);

            int count = Marshal.ReadInt32(p, 0);
            if (count <= 0 || count > 256) return result.ToArray();

            for (int i = 0; i < count; i++)
            {
                int offset = Marshal.ReadInt32(p, 4 + 4 * i);
                if (offset <= 0 || offset >= buffer.Length) continue;

                var sb = new StringBuilder(1024);
                if (SHGetPathFromIDList(new IntPtr(p.ToInt64() + offset), sb))
                {
                    string path = sb.ToString();
                    if (path.Length > 0 && !result.Contains(path)) result.Add(path);
                }
            }
        }
        finally { Marshal.FreeHGlobal(p); }
        return result.ToArray();
    }

    static byte[] BuildCida(string path)
    {
        IntPtr pidl; uint eaten;
        int hr = SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out eaten);
        if (hr != 0 || pidl == IntPtr.Zero) return null;

        try
        {
            int size = (int)ILGetSize(pidl);
            if (size <= 0) return null;

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(1);                 // cidl
                bw.Write(4 + 4 * 1);         // aoffset[0] -> 紧跟头部
                var bytes = new byte[size];
                Marshal.Copy(pidl, bytes, 0, size);
                bw.Write(bytes);
                bw.Flush();
                return ms.ToArray();
            }
        }
        finally { CoTaskMemFree(pidl); }
    }

    static void Main()
    {
        string[] targets =
        {
            @"C:\Users\chenz\Desktop\Razer Synapse.lnk",
            @"C:\Windows\System32\notepad.exe",
            @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Excel.lnk"
        };

        int ok = 0, fail = 0;
        foreach (string t in targets)
        {
            byte[] cida = BuildCida(t);
            if (cida == null) { Console.WriteLine("BUILD FAILED: " + t); fail++; continue; }

            string[] got = ParseIdList(cida);
            bool match = got.Length == 1 &&
                         string.Equals(got[0], t, StringComparison.OrdinalIgnoreCase);

            Console.WriteLine((match ? "OK   " : "FAIL ") + t);
            Console.WriteLine("        parsed -> [" + string.Join(" | ", got) + "]  (" + cida.Length + " bytes)");
            if (match) ok++; else fail++;
        }

        // 垃圾数据不应该崩
        Console.WriteLine("--- garbage input ---");
        Console.WriteLine("empty  -> " + ParseIdList(new byte[0]).Length + " item(s)");
        Console.WriteLine("junk   -> " + ParseIdList(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9, 9 }).Length + " item(s)");
        Console.WriteLine("huge n -> " + ParseIdList(new byte[] { 0xFF, 0xFF, 0, 0, 8, 0, 0, 0, 0, 0, 0, 0 }).Length + " item(s)");

        Console.WriteLine();
        Console.WriteLine("RESULT: ok=" + ok + " fail=" + fail);
    }
}
