using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Workspaces
{
    public class WinItem
    {
        [JsonPropertyName("exe")] public string Exe { get; set; } = "";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("show_cmd")] public int ShowCmd { get; set; } = 1;
        [JsonPropertyName("rect")] public int[] Rect { get; set; } = new int[4];
    }

    public class Workspace
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("color")] public string Color { get; set; } = "#6C8CFF";
        [JsonPropertyName("icon")] public string Icon { get; set; } = "";
        [JsonPropertyName("hotkey")] public string Hotkey { get; set; } = "";
        [JsonPropertyName("open")] public List<string> Launch { get; set; } = new List<string>();
        [JsonPropertyName("items")] public List<WinItem> Items { get; set; } = new List<WinItem>();
    }

    public class AppData
    {
        // 0 = файл старого формата (до версии 1.3); актуальную версию выставляет DataStore при сохранении.
        [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 0;
        [JsonPropertyName("workspaces")] public List<Workspace> Workspaces { get; set; } = new List<Workspace>();

        // общие
        [JsonPropertyName("lang")] public string Lang { get; set; } = "ru";
        [JsonPropertyName("theme")] public string Theme { get; set; } = "dark";      // dark | light | system (PRO)
        [JsonPropertyName("accent")] public string Accent { get; set; } = "#6C8CFF";
        [JsonPropertyName("card_radius")] public int CardRadius { get; set; } = 14;
        [JsonPropertyName("card_size")] public int CardSize { get; set; } = 100;      // %
        [JsonPropertyName("density")] public int Density { get; set; } = 1;
        [JsonPropertyName("close_to_tray")] public bool CloseToTray { get; set; } = true;
        [JsonPropertyName("minimize_others")] public bool MinimizeOthers { get; set; } = true;
        [JsonPropertyName("launch_once")] public bool LaunchOnce { get; set; } = true;
        [JsonPropertyName("auto_update_check")] public bool AutoUpdateCheck { get; set; } = true;
        [JsonPropertyName("last_update_check")] public string LastUpdateCheck { get; set; } = "";
        [JsonPropertyName("first_run_done")] public bool FirstRunDone { get; set; } = false;

        // остров
        [JsonPropertyName("island")] public bool Island { get; set; } = true;
        [JsonPropertyName("island_bg")] public string IslandBg { get; set; } = "#0B0B0D";
        [JsonPropertyName("island_opacity")] public int IslandOpacity { get; set; } = 100;
        [JsonPropertyName("island_height")] public int IslandHeight { get; set; } = 58;
        [JsonPropertyName("island_font")] public int IslandFont { get; set; } = 13;
        [JsonPropertyName("island_names")] public bool IslandNames { get; set; } = true;
        [JsonPropertyName("island_pos")] public int IslandPos { get; set; } = 0;
        [JsonPropertyName("monitor")] public int Monitor { get; set; } = 0;
        [JsonPropertyName("anim_type")] public int AnimType { get; set; } = 0;
        [JsonPropertyName("anim_speed")] public int AnimSpeed { get; set; } = 100;
        [JsonPropertyName("trigger_w")] public int TriggerW { get; set; } = 300;
        [JsonPropertyName("trigger_h")] public int TriggerH { get; set; } = 3;
        [JsonPropertyName("hide_delay")] public int HideDelay { get; set; } = 350;
    }
}
