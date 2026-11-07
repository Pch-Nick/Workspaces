using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Workspaces
{
    public static class WindowManager
    {
        public class Win { public IntPtr Hwnd; public string Title; public string Exe; }

        static readonly HashSet<string> Ignore = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer.exe", "applicationframehost.exe", "textinputhost.exe", "searchhost.exe",
            "startmenuexperiencehost.exe", "shellexperiencehost.exe", "systemsettings.exe"
        };

        static string ExePath(uint pid)
        {
            IntPtr h = Native.OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return Native.QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { Native.CloseHandle(h); }
        }

        public static List<Win> List()
        {
            var res = new List<Win>();
            uint me = (uint)Environment.ProcessId;
            Native.EnumWindows((h, lp) =>
            {
                if (!Native.IsWindowVisible(h)) return true;
                int len = Native.GetWindowTextLength(h);
                if (len == 0) return true;
                var sb = new StringBuilder(len + 1);
                Native.GetWindowText(h, sb, sb.Capacity);
                string title = sb.ToString();
                if (string.IsNullOrWhiteSpace(title)) return true;
                if (Native.DwmGetWindowAttribute(h, 14, out int cloaked, 4) == 0 && cloaked != 0) return true;
                if ((Native.GetWindowLong(h, -20) & 0x80) != 0) return true; // WS_EX_TOOLWINDOW
                Native.GetWindowThreadProcessId(h, out uint pid);
                if (pid == me) return true;
                string exe = ExePath(pid);
                if (exe == null || Ignore.Contains(Path.GetFileName(exe))) return true;
                res.Add(new Win { Hwnd = h, Title = title, Exe = exe });
                return true;
            }, IntPtr.Zero);
            return res;
        }

        // Снимок окон (свёрнутые не берём), сверху вниз по z-порядку.
        public static List<WinItem> Capture()
        {
            var items = new List<WinItem>();
            foreach (var w in List())
            {
                if (Native.IsIconic(w.Hwnd)) continue;
                var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
                if (!Native.GetWindowPlacement(w.Hwnd, ref wp)) continue;
                var r = wp.rcNormalPosition;
                items.Add(new WinItem
                {
                    Exe = w.Exe, Title = w.Title, ShowCmd = wp.showCmd,
                    Rect = new[] { r.Left, r.Top, r.Right, r.Bottom }
                });
            }
            return items;
        }

        static IntPtr FindFree(WinItem item, HashSet<IntPtr> used)
        {
            var cands = List().Where(w => !used.Contains(w.Hwnd) &&
                string.Equals(w.Exe, item.Exe, StringComparison.OrdinalIgnoreCase)).ToList();
            var exact = cands.FirstOrDefault(w => w.Title == item.Title);
            if (exact != null) return exact.Hwnd;
            return cands.Count > 0 ? cands[0].Hwnd : IntPtr.Zero;
        }

        static void Place(IntPtr h, WinItem item)
        {
            int cmd = item.ShowCmd == 2 ? 1 : item.ShowCmd; // свёрнутое -> обычное
            if (item.Rect == null || item.Rect.Length < 4 ||
                item.Rect[2] - item.Rect[0] <= 0 || item.Rect[3] - item.Rect[1] <= 0)
            {
                // приложение добавлено вручную и раскладки у него нет: просто показываем окно
                Native.ShowWindow(h, cmd == 3 ? 3 : 9);
                return;
            }
            var wp = new WINDOWPLACEMENT
            {
                length = Marshal.SizeOf<WINDOWPLACEMENT>(),
                showCmd = cmd,
                ptMinPosition = new POINT { X = -1, Y = -1 },
                ptMaxPosition = new POINT { X = -1, Y = -1 },
                rcNormalPosition = new RECT { Left = item.Rect[0], Top = item.Rect[1], Right = item.Rect[2], Bottom = item.Rect[3] }
            };
            Native.SetWindowPlacement(h, ref wp);
        }

        static void ForceForeground(IntPtr h)
        {
            Native.keybd_event(0x12, 0, 0, UIntPtr.Zero); // Alt: разрешает смену фокуса
            Native.SetForegroundWindow(h);
            Native.keybd_event(0x12, 0, 2, UIntPtr.Zero);
        }

        // Запустить недостающее, свернуть всё лишнее, расставить окна пространства.
        public static void Activate(List<WinItem> items, Action<string> status, bool minimizeOthers = true)
        {
            var used = new HashSet<IntPtr>();
            var matched = new List<(int idx, IntPtr h, WinItem item)>();
            var pending = new List<(int idx, WinItem item)>();

            for (int i = 0; i < items.Count; i++)
            {
                var h = FindFree(items[i], used);
                if (h != IntPtr.Zero) { used.Add(h); matched.Add((i, h, items[i])); }
                else pending.Add((i, items[i]));
            }

            var started = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var launched = new List<(int idx, WinItem item)>();
            var extras = new List<(int idx, WinItem item)>();
            foreach (var p in pending)
            {
                if (started.Contains(p.item.Exe) || !File.Exists(p.item.Exe)) { extras.Add(p); continue; }
                try
                {
                    status(L.T("Запускаю ", "Launching ") + Path.GetFileName(p.item.Exe));
                    Process.Start(new ProcessStartInfo(p.item.Exe)
                    {
                        UseShellExecute = false,
                        WorkingDirectory = Path.GetDirectoryName(p.item.Exe)
                    });
                    started.Add(p.item.Exe);
                    launched.Add(p);
                }
                catch (Exception ex) { Logger.Warn("Не удалось запустить " + p.item.Exe, ex); }
            }

            var waiting = launched.ToList();
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (waiting.Count > 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(400);
                foreach (var p in waiting.ToList())
                {
                    var h = FindFree(p.item, used);
                    if (h != IntPtr.Zero) { used.Add(h); matched.Add((p.idx, h, p.item)); waiting.Remove(p); }
                }
            }
            foreach (var p in extras)
            {
                var h = FindFree(p.item, used);
                if (h != IntPtr.Zero) { used.Add(h); matched.Add((p.idx, h, p.item)); }
            }

            if (minimizeOthers)
            {
                status(L.T("Сворачиваю лишнее…", "Minimizing others…"));
                var keep = new HashSet<IntPtr>(matched.Select(m => m.h));
                foreach (var w in List())
                    if (!keep.Contains(w.Hwnd)) Native.ShowWindow(w.Hwnd, 6); // SW_MINIMIZE
            }

            status(L.T("Расставляю окна…", "Arranging windows…"));
            foreach (var m in matched.OrderByDescending(m => m.idx))
            {
                Place(m.h, m.item);
                ForceForeground(m.h);
            }
            status(L.T("Готово", "Done"));
        }
    }
}
