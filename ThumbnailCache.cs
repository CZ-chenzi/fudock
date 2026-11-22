using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace Fudock
{
    /// <summary>
    /// 窗口缩略图缓存。最小化的窗口抓不到画面（PrintWindow 对隐藏窗口返回空），
    /// 所以在窗口可见时后台轮换抓帧，最小化后回放"最后一帧"——和任务栏预览一个思路。
    /// </summary>
    internal static class ThumbnailCache
    {
        private sealed class Entry
        {
            public ImageSource Image;
            public DateTime At;
        }

        private const int MaxWidth = 440;
        private const double FreshSeconds = 3.0;     // 超过这个时间就重抓
        private const double KeepSeconds = 120.0;    // 缓存最长保留多久

        private static readonly Dictionary<IntPtr, Entry> Cache = new Dictionary<IntPtr, Entry>();
        private static int _cursor;

        public static bool IsMinimized(IntPtr hwnd)
        {
            try { return hwnd != IntPtr.Zero && Native.IsIconic(hwnd); }
            catch { return false; }
        }

        /// <summary>取缩略图：可见就现抓，最小化/抓失败就回放最后一次快照。</summary>
        public static ImageSource Get(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return null;

            ImageSource cached = Peek(hwnd);

            if (!IsMinimized(hwnd))
            {
                ImageSource fresh = WindowCapture.Capture(hwnd, MaxWidth);
                if (fresh != null)
                {
                    Store(hwnd, fresh);
                    return fresh;
                }
            }

            return cached;
        }

        private static ImageSource Peek(IntPtr hwnd)
        {
            Entry e;
            if (!Cache.TryGetValue(hwnd, out e)) return null;
            if ((DateTime.Now - e.At).TotalSeconds > KeepSeconds) return null;
            return e.Image;
        }

        private static void Store(IntPtr hwnd, ImageSource img)
        {
            Cache[hwnd] = new Entry { Image = img, At = DateTime.Now };
        }

        /// <summary>后台预热：每次调用最多抓 2 个窗口，轮着来，避免集中开销。</summary>
        public static void Warmup(List<IntPtr> handles)
        {
            if (handles == null || handles.Count == 0) return;

            int budget = 2;
            int guard = 0;
            while (budget > 0 && guard < handles.Count)
            {
                _cursor = (_cursor + 1) % handles.Count;
                IntPtr h = handles[_cursor];
                guard++;
                if (h == IntPtr.Zero) continue;
                if (!Native.IsWindow(h)) continue;
                if (IsMinimized(h)) continue;      // 最小化的抓不到，等它恢复

                Entry e;
                if (Cache.TryGetValue(h, out e) && (DateTime.Now - e.At).TotalSeconds < FreshSeconds)
                    continue;

                ImageSource img = WindowCapture.Capture(h, MaxWidth);
                if (img != null) Store(h, img);
                budget--;
            }

            Prune();
        }

        private static int _pruneTick;
        private static void Prune()
        {
            if (++_pruneTick % 20 != 0) return;   // 偶尔清一次
            try
            {
                var dead = new List<IntPtr>();
                foreach (var kv in Cache)
                {
                    if (!Native.IsWindow(kv.Key)) dead.Add(kv.Key);
                    else if ((DateTime.Now - kv.Value.At).TotalSeconds > KeepSeconds) dead.Add(kv.Key);
                }
                foreach (var h in dead) Cache.Remove(h);
            }
            catch { }
        }

        public static void Clear()
        {
            Cache.Clear();
            _cursor = 0;
        }
    }
}
