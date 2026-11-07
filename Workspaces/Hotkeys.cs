using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;

namespace Workspaces
{
    // Глобальные горячие клавиши пространств (RegisterHotKey).
    public class HotkeyManager
    {
        readonly Hub hub;
        readonly HwndSource src;
        readonly Dictionary<int, string> map = new Dictionary<int, string>();
        string signature = "\u0001";

        public HotkeyManager(Hub hub)
        {
            this.hub = hub;
            var p = new HwndSourceParameters("Workspaces.Hotkeys")
            {
                ParentWindow = new IntPtr(-3), // HWND_MESSAGE
                WindowStyle = 0, Width = 0, Height = 0
            };
            src = new HwndSource(p);
            src.AddHook(Hook);
            hub.Changed += Refresh;
            Refresh();
        }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0312) // WM_HOTKEY
            {
                string name;
                if (map.TryGetValue(wParam.ToInt32(), out name))
                {
                    handled = true;
                    hub.Activate(name);
                }
            }
            return IntPtr.Zero;
        }

        public void Refresh()
        {
            string sig = string.Join("|", hub.Data.Workspaces.Select(w => w.Name + "=" + w.Hotkey));
            if (sig == signature) return;
            signature = sig;
            foreach (var id in map.Keys.ToList()) Native.UnregisterHotKey(src.Handle, id);
            map.Clear();
            int next = 1;
            var failed = new List<string>();
            foreach (var w in hub.Data.Workspaces)
            {
                if (string.IsNullOrEmpty(w.Hotkey)) continue;
                uint mod, vk;
                if (!Parse(w.Hotkey, out mod, out vk)) continue;
                if (Native.RegisterHotKey(src.Handle, next, mod | 0x4000, vk)) // MOD_NOREPEAT
                {
                    map[next] = w.Name;
                    next++;
                }
                else failed.Add(w.Hotkey);
            }
            if (failed.Count > 0)
                hub.SetStatus(L.T("Сочетание занято другой программой: ", "Hotkey is taken by another app: ") + string.Join(", ", failed));
        }

        public static string Format(ModifierKeys m, Key k)
        {
            var parts = new List<string>();
            if ((m & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((m & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((m & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((m & ModifierKeys.Windows) != 0) parts.Add("Win");
            string ks;
            if (k >= Key.D0 && k <= Key.D9) ks = ((int)(k - Key.D0)).ToString();
            else if (k >= Key.NumPad0 && k <= Key.NumPad9) ks = "Num" + ((int)(k - Key.NumPad0));
            else ks = k.ToString();
            parts.Add(ks);
            return string.Join("+", parts);
        }

        public static bool Parse(string s, out uint mod, out uint vk)
        {
            mod = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            Key key = Key.None;
            foreach (var raw in s.Split('+'))
            {
                string t = raw.Trim();
                if (t == "Ctrl") mod |= 2;
                else if (t == "Alt") mod |= 1;
                else if (t == "Shift") mod |= 4;
                else if (t == "Win") mod |= 8;
                else if (t.Length == 1 && char.IsDigit(t[0])) key = Key.D0 + (t[0] - '0');
                else if (t.Length == 4 && t.StartsWith("Num") && char.IsDigit(t[3])) key = Key.NumPad0 + (t[3] - '0');
                else if (!Enum.TryParse(t, true, out key)) return false;
            }
            if (key == Key.None) return false;
            vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            return vk != 0;
        }
    }
}
