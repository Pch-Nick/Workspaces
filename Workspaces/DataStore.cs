using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Workspaces
{
    public class BackupInfo
    {
        public string Path;
        public DateTime Time;
        public long Size;
        public string Reason;
    }

    // Надёжное хранилище: атомарная запись (tmp -> Replace), .bak предыдущей версии, ротация
    // резервных копий, карантин повреждённых файлов, восстановление и миграция формата.
    public class DataStore
    {
        public const int CurrentSchema = 3;
        public const int MaxBackups = 20;

        public readonly string Dir;
        readonly string mainPath, bakPath, backupDir;
        readonly object gate = new object();

        static readonly JsonSerializerOptions WriteOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        static readonly JsonSerializerOptions ReadOpts = new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public DataStore(string dir)
        {
            Dir = dir;
            mainPath = Path.Combine(dir, "workspaces.json");
            bakPath = mainPath + ".bak";
            backupDir = Path.Combine(dir, "Backups");
        }

        public string MainPath { get { return mainPath; } }

        // ---------- загрузка с восстановлением
        public AppData Load(out string notice)
        {
            notice = null;
            lock (gate)
            {
                try { Directory.CreateDirectory(Dir); }
                catch (Exception ex) { Logger.Error("Не удалось создать папку данных " + Dir, ex); }

                if (File.Exists(mainPath))
                {
                    var d = TryRead(mainPath);
                    if (d != null) return d;
                    Quarantine(mainPath);
                    Logger.Error("Основной файл данных повреждён, пробую восстановить");
                }
                else if (!File.Exists(bakPath) && ListBackups().Count == 0)
                {
                    return new AppData(); // первый запуск
                }

                foreach (var cand in new[] { bakPath }.Concat(ListBackups().Select(b => b.Path)))
                {
                    if (!File.Exists(cand)) continue;
                    var d = TryRead(cand);
                    if (d == null) continue;
                    Logger.Warn("Данные восстановлены из " + cand);
                    notice = L.T("Файл данных был повреждён. Пространства восстановлены из резервной копии.",
                                 "The data file was damaged. Workspaces were restored from a backup.");
                    SaveInternal(d);
                    return d;
                }
                notice = L.T("Файл данных повреждён, и резервных копий не нашлось. Приложение начато с чистого листа; "
                             + "повреждённый файл сохранён рядом с пометкой corrupt.",
                             "The data file is damaged and no backup was found. Starting empty; "
                             + "the damaged file was kept next to the data with a “corrupt” name.");
                return new AppData();
            }
        }

        // Читает и проверяет файл; null, если файл нечитаем.
        public AppData TryRead(string path)
        {
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text)) return null;
                var d = JsonSerializer.Deserialize<AppData>(text, ReadOpts);
                if (d == null) return null;
                int original = d.SchemaVersion;
                bool hadWorkspaces = d.Workspaces != null && d.Workspaces.Count > 0;
                if (original < CurrentSchema && string.Equals(path, mainPath, StringComparison.OrdinalIgnoreCase))
                    CreateBackup("pre-migration");
                Normalize(d);
                if (original < CurrentSchema && hadWorkspaces) d.FirstRunDone = true;
                return d;
            }
            catch (Exception ex)
            {
                Logger.Warn("Не удалось прочитать " + path, ex);
                return null;
            }
        }

        void Quarantine(string path)
        {
            try
            {
                string target = Path.Combine(Dir, "workspaces.corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                File.Move(path, target);
            }
            catch (Exception ex) { Logger.Error("Не удалось отложить повреждённый файл", ex); }
        }

        // ---------- миграция и проверка значений
        public static void Normalize(AppData d)
        {
            if (d.Workspaces == null) d.Workspaces = new List<Workspace>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int n = 0;
            foreach (var w in d.Workspaces.ToList())
            {
                if (w == null) { d.Workspaces.Remove(w); continue; }
                n++;
                string name = string.IsNullOrWhiteSpace(w.Name) ? "Workspace " + n : w.Name.Trim();
                string baseName = name; int k = 2;
                while (!names.Add(name)) name = baseName + " (" + (k++) + ")";
                w.Name = name;
                if (!Theme.IsHex(w.Color)) w.Color = UI.Palette[(n - 1) % UI.Palette.Length];
                w.Icon = w.Icon ?? "";
                w.Hotkey = w.Hotkey ?? "";
                w.Launch = (w.Launch ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                w.Items = (w.Items ?? new List<WinItem>()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.Exe)).ToList();
                foreach (var it in w.Items)
                {
                    it.Title = it.Title ?? "";
                    if (it.Rect == null || it.Rect.Length < 4) it.Rect = new int[4];
                    if (it.ShowCmd < 1 || it.ShowCmd > 9) it.ShowCmd = 1;
                }
            }
            if (d.Lang != "ru" && d.Lang != "en") d.Lang = "ru";
            if (d.Theme != "dark" && d.Theme != "light" && d.Theme != "system") d.Theme = "dark";
            if (!Theme.IsHex(d.Accent)) d.Accent = "#6C8CFF";
            if (!Theme.IsHex(d.IslandBg)) d.IslandBg = "#0B0B0D";
            d.CardRadius = Clamp(d.CardRadius, 0, 28);
            d.CardSize = Clamp(d.CardSize, 80, 130);
            d.Density = Clamp(d.Density, 0, 2);
            d.IslandOpacity = Clamp(d.IslandOpacity, 30, 100);
            d.IslandHeight = Clamp(d.IslandHeight, 40, 80);
            d.IslandFont = Clamp(d.IslandFont, 11, 18);
            d.IslandPos = Clamp(d.IslandPos, 0, 2);
            d.Monitor = Clamp(d.Monitor, 0, 15);
            d.AnimType = Clamp(d.AnimType, 0, 2);
            d.AnimSpeed = Clamp(d.AnimSpeed, 50, 200);
            d.TriggerW = Clamp(d.TriggerW, 80, 800);
            d.TriggerH = Clamp(d.TriggerH, 1, 20);
            d.HideDelay = Clamp(d.HideDelay, 150, 1500);
            d.LastUpdateCheck = d.LastUpdateCheck ?? "";
            d.SchemaVersion = CurrentSchema;
        }

        static int Clamp(int v, int min, int max) { return v < min ? min : v > max ? max : v; }

        // ---------- запись
        public bool Save(AppData d)
        {
            lock (gate) { return SaveInternal(d); }
        }

        bool SaveInternal(AppData d)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                d.SchemaVersion = CurrentSchema;
                AtomicWrite(mainPath, JsonSerializer.Serialize(d, WriteOpts), bakPath);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось сохранить данные", ex);
                return false;
            }
        }

        // Сначала полностью пишем временный файл на диск, потом атомарно подменяем основной.
        public static void AtomicWrite(string path, string content, string backupPath = null)
        {
            string tmp = path + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(content);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            if (File.Exists(path)) File.Replace(tmp, path, backupPath, true);
            else File.Move(tmp, path);
        }

        public void ExportTo(AppData d, string path)
        {
            AtomicWrite(path, JsonSerializer.Serialize(d, WriteOpts));
        }

        // ---------- резервные копии
        public string CreateBackup(string reason)
        {
            lock (gate)
            {
                try
                {
                    if (!File.Exists(mainPath)) return null;
                    Directory.CreateDirectory(backupDir);
                    string safe = new string((reason ?? "manual").Where(char.IsLetterOrDigit).ToArray());
                    if (safe.Length == 0) safe = "manual";
                    string name = "workspaces-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + safe + ".json";
                    string target = Path.Combine(backupDir, name);
                    File.Copy(mainPath, target, true);
                    File.SetLastWriteTime(target, DateTime.Now); // время копии, а не исходного файла
                    Prune();
                    return target;
                }
                catch (Exception ex)
                {
                    Logger.Error("Не удалось создать резервную копию", ex);
                    return null;
                }
            }
        }

        public void AutoBackupIfDue(TimeSpan interval)
        {
            var list = ListBackups();
            if (list.Count == 0 || DateTime.Now - list[0].Time > interval) CreateBackup("auto");
        }

        public List<BackupInfo> ListBackups()
        {
            var res = new List<BackupInfo>();
            try
            {
                if (!Directory.Exists(backupDir)) return res;
                foreach (var f in Directory.GetFiles(backupDir, "workspaces-*.json"))
                {
                    var fi = new FileInfo(f);
                    string[] parts = Path.GetFileNameWithoutExtension(f).Split('-');
                    res.Add(new BackupInfo
                    {
                        Path = f, Time = fi.LastWriteTime, Size = fi.Length,
                        Reason = parts.Length >= 5 ? parts[parts.Length - 1] : ""
                    });
                }
            }
            catch (Exception ex) { Logger.Warn("Не удалось прочитать список копий", ex); }
            return res.OrderByDescending(b => b.Time).ToList();
        }

        void Prune()
        {
            var list = ListBackups();
            for (int i = MaxBackups; i < list.Count; i++)
            {
                try { File.Delete(list[i].Path); }
                catch (Exception ex) { Logger.Warn("Не удалось удалить старую копию", ex); }
            }
        }
    }

    // Статический фасад для приложения; тесты создают собственные экземпляры DataStore.
    public static class Store
    {
        public static DataStore Instance = new DataStore(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Workspaces"));
        public static string LoadNotice;

        public static string Dir { get { return Instance.Dir; } }

        public static AppData Load()
        {
            string notice;
            var d = Instance.Load(out notice);
            LoadNotice = notice;
            return d;
        }

        public static bool Save(AppData d) { return Instance.Save(d); }
        public static void ExportTo(AppData d, string path) { Instance.ExportTo(d, path); }
        public static AppData ImportFrom(string path) { return Instance.TryRead(path); }
    }
}
