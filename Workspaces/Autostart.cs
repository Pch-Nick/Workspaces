using System;
using Microsoft.Win32;

namespace Workspaces
{
    public static class Autostart
    {
        const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "Workspaces";

        public static bool IsEnabled()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(KeyPath))
                    return k != null && k.GetValue(ValueName) != null;
            }
            catch { return false; }
        }

        public static void Set(bool on)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(KeyPath, true))
            {
                if (k == null) return;
                if (on) k.SetValue(ValueName, "\"" + Environment.ProcessPath + "\" --minimized");
                else k.DeleteValue(ValueName, false);
            }
        }
    }
}
