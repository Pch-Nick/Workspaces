using System;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Microsoft.Win32;

namespace Workspaces
{
    public static class L
    {
        public static string Lang = "ru";
        public static string T(string ru, string en) { return Lang == "en" ? en : ru; }
    }

    public class Preset
    {
        public string Name, NameEn, Accent, Theme, IslandBg;
        public int Opacity;
    }

    public static class Theme
    {
        public static bool Dark = true;
        public static string Accent = "#6C8CFF";

        public static string Bg { get { return Dark ? "#15161A" : "#F1F2F6"; } }
        public static string Sidebar { get { return Dark ? "#0F1013" : "#E6E8EE"; } }
        public static string Card { get { return Dark ? "#202228" : "#FFFFFF"; } }
        public static string CardHover { get { return Dark ? "#292C34" : "#EEF0F6"; } }
        public static string Panel { get { return Dark ? "#1B1D23" : "#FFFFFF"; } }
        public static string Fg { get { return Dark ? "#F2F3F5" : "#16171B"; } }
        public static string Muted { get { return Dark ? "#8B8F9A" : "#6A6F7B"; } }
        public static string Dim { get { return Dark ? "#5C606B" : "#8F94A0"; } }
        public static string NavHover { get { return Dark ? "#1A1C22" : "#DADDE5"; } }
        public static string NavSel { get { return Dark ? "#202228" : "#FFFFFF"; } }
        public static string BtnSec { get { return Dark ? "#2A2D36" : "#DADDE5"; } }
        public static string BtnSecHover { get { return Dark ? "#363A46" : "#CBCFDA"; } }
        public static string ToggleOff { get { return Dark ? "#3A3D48" : "#C3C7D1"; } }
        public static string Field { get { return Dark ? "#26272D" : "#FFFFFF"; } }
        public static string FieldBorder { get { return Dark ? "#3D4050" : "#C3C7D1"; } }
        public static string OnAccent { get { return Contrast(Accent); } }
        public static string AccentHover { get { return Mix(Accent, "#FFFFFF", 0.18); } }

        public static readonly Preset[] Presets =
        {
            new Preset { Name = "Классика", NameEn = "Classic", Accent = "#6C8CFF", Theme = "dark", IslandBg = "#0B0B0D", Opacity = 100 },
            new Preset { Name = "Океан", NameEn = "Ocean", Accent = "#22D3EE", Theme = "dark", IslandBg = "#071A20", Opacity = 100 },
            new Preset { Name = "Закат", NameEn = "Sunset", Accent = "#FB923C", Theme = "dark", IslandBg = "#1C0F08", Opacity = 100 },
            new Preset { Name = "Лаванда", NameEn = "Lavender", Accent = "#C084FC", Theme = "dark", IslandBg = "#150D1F", Opacity = 100 },
            new Preset { Name = "Мята", NameEn = "Mint", Accent = "#4ADE80", Theme = "dark", IslandBg = "#08170F", Opacity = 100 },
            new Preset { Name = "Роза", NameEn = "Rose", Accent = "#FF7AA8", Theme = "dark", IslandBg = "#1F0C14", Opacity = 100 },
            new Preset { Name = "Светлая", NameEn = "Light", Accent = "#3B6BFF", Theme = "light", IslandBg = "#FFFFFF", Opacity = 96 },
            new Preset { Name = "Стекло", NameEn = "Glass", Accent = "#6C8CFF", Theme = "dark", IslandBg = "#000000", Opacity = 60 }
        };

        public static void Apply(AppData d)
        {
            string mode = Eff.ThemeMode(d);
            Dark = mode == "dark" || (mode == "system" && SystemIsDark());
            Accent = Eff.Accent(d);
        }

        static bool SystemIsDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch (Exception ex) { Logger.Warn("Не удалось определить системную тему", ex); return true; }
        }

        public static bool IsHex(string s)
        {
            return s != null && Regex.IsMatch(s, "^#[0-9A-Fa-f]{6}$");
        }

        public static Color Parse(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        public static double Lum(string hex)
        {
            var c = Parse(hex);
            return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        }

        public static string Contrast(string hex)
        {
            return Lum(hex) > 0.58 ? "#0B0C10" : "#FFFFFF";
        }

        public static string Mix(string a, string b, double t)
        {
            var ca = Parse(a); var cb = Parse(b);
            int r = (int)(ca.R + (cb.R - ca.R) * t);
            int g = (int)(ca.G + (cb.G - ca.G) * t);
            int bl = (int)(ca.B + (cb.B - ca.B) * t);
            return "#" + r.ToString("X2") + g.ToString("X2") + bl.ToString("X2");
        }
    }
}
