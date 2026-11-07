using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workspaces
{
    // Внешняя конфигурация (appconfig.json рядом с exe). Пустые значения = функция не подключена.
    public class AppConfig
    {
        [JsonPropertyName("updateFeedUrl")] public string UpdateFeedUrl { get; set; } = "";
        [JsonPropertyName("updatePublicKeyPem")] public string UpdatePublicKeyPem { get; set; } = "";
        [JsonPropertyName("licenseServerUrl")] public string LicenseServerUrl { get; set; } = "";
        [JsonPropertyName("licenseGraceDays")] public int LicenseGraceDays { get; set; } = 7;

        public static bool IsHttps(string url)
        {
            Uri u;
            return !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out u) && u.Scheme == Uri.UriSchemeHttps;
        }

        public static AppConfig Load(string path = null)
        {
            try
            {
                if (path == null) path = Path.Combine(AppContext.BaseDirectory, "appconfig.json");
                if (!File.Exists(path)) return new AppConfig();
                var c = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path)) ?? new AppConfig();
                if (c.LicenseGraceDays < 0 || c.LicenseGraceDays > 90) c.LicenseGraceDays = 7;
                return c;
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось прочитать appconfig.json", ex);
                return new AppConfig();
            }
        }
    }
}
