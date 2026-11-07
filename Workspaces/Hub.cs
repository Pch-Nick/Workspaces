using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Workspaces
{
    public static class UI
    {
        public static System.Version CurrentVersion
        {
            get { return typeof(UI).Assembly.GetName().Version ?? new System.Version(0, 0, 0, 0); }
        }
        public static string Version
        {
            get { var v = CurrentVersion; return v.Major + "." + v.Minor + "." + Math.Max(0, v.Build); }
        }

        public static readonly string[] Palette =
        {
            "#6C8CFF", "#FF7AA8", "#4ADE80", "#FBBF24", "#C084FC", "#22D3EE",
            "#FB923C", "#F87171", "#A3E635", "#2DD4BF", "#818CF8", "#E879F9"
        };
        public static readonly string[] AccentPalette =
            { "#6C8CFF", "#FF7AA8", "#4ADE80", "#FBBF24", "#C084FC", "#22D3EE", "#FB923C", "#F87171" };
        public static readonly string[] IslandPalette =
            { "#0B0B0D", "#1A1B22", "#071A20", "#1C0F08", "#150D1F", "#08170F", "#000000", "#FFFFFF" };

        public static SolidColorBrush B(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        public static System.Windows.Forms.Screen[] Screens()
        {
            return System.Windows.Forms.Screen.AllScreens.OrderByDescending(s => s.Primary).ToArray();
        }
    }

    // Общее состояние: данные, активное пространство, сервисы и события для окон.
    public class Hub
    {
        public AppData Data;
        public string Active;
        public string StartupNotice;
        public MainWindow Main;
        public IslandWindow Island;
        public TrayIcon Tray;
        public HotkeyManager Hotkeys;

        public AppConfig Config;
        public UpdateService Updates;
        public LicenseService Licenses;
        public UpdateCheckResult UpdateResult = new UpdateCheckResult();

        public event Action Changed;              // данные или лёгкие настройки
        public event Action AppearanceChanged;    // тема, язык, лицензия: полная перерисовка
        public event Action UpdateChanged;
        public event Action<string> StatusChanged;

        bool busy;
        bool dirty;
        DispatcherTimer saveTimer;
        readonly HashSet<string> openedOnce = new HashSet<string>();

        public Hub()
        {
            Data = Store.Load();
            StartupNotice = Store.LoadNotice;
            Config = AppConfig.Load();
            Licenses = new LicenseService(new NotConfiguredLicenseProvider(), new DpapiLicenseStorage(Store.Dir), Config.LicenseGraceDays);
            Entitlements.StatusProvider = () => Licenses.Current;
            Updates = new UpdateService(Config, UI.CurrentVersion);
            L.Lang = Data.Lang;
            Theme.Apply(Data);
            Licenses.Changed += () =>
            {
                Theme.Apply(Data);
                var h = AppearanceChanged;
                if (h != null) h();
            };
        }

        public Workspace Get(string name)
        {
            return Data.Workspaces.FirstOrDefault(w => w.Name == name);
        }

        public bool CanCreateWorkspace() { return Entitlements.CanCreateWorkspace(Data.Workspaces.Count); }

        // ---------- сохранение
        // Изменение данных: пишем сразу (атомарно).
        public void Commit()
        {
            SaveNow();
            var h = Changed;
            if (h != null) h();
        }

        public void Refresh() { SaveSoon(); var h = Changed; if (h != null) h(); }

        public void SaveSettings() { SaveSoon(); }

        void SaveNow()
        {
            dirty = false;
            if (!Store.Save(Data))
                SetStatus(L.T("Не удалось сохранить данные. Подробности в журнале.", "Could not save data. See the log for details."));
        }

        // Частые изменения (ползунки): откладываем запись на 400 мс.
        public void SaveSoon()
        {
            dirty = true;
            if (saveTimer == null)
            {
                saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                saveTimer.Tick += (s, e) => { saveTimer.Stop(); FlushSave(); };
            }
            saveTimer.Stop();
            saveTimer.Start();
        }

        public void FlushSave()
        {
            if (dirty) SaveNow();
        }

        public void ApplyAppearance()
        {
            L.Lang = Data.Lang;
            Theme.Apply(Data);
            SaveNow();
            var h = AppearanceChanged;
            if (h != null) h();
        }

        public void SetStatus(string s)
        {
            var h = StatusChanged;
            if (h != null) h(s);
        }

        public void MoveBefore(string src, string target)
        {
            if (src == target) return;
            var w = Get(src);
            if (w == null || Get(target) == null) return;
            Data.Workspaces.Remove(w);
            int idx = Data.Workspaces.FindIndex(x => x.Name == target);
            Data.Workspaces.Insert(idx < 0 ? Data.Workspaces.Count : idx, w);
            Commit();
        }

        // Заменяет пространства импортированными. В Free импортируется не больше лимита, остальное не трогаем в файле.
        public int ReplaceWorkspaces(List<Workspace> list, out int skipped)
        {
            int max = Entitlements.MaxWorkspaces;
            var take = list.Take(max == int.MaxValue ? list.Count : max).ToList();
            skipped = list.Count - take.Count;
            Store.Instance.CreateBackup("before-import");
            Data.Workspaces = take;
            Active = null;
            Commit();
            return take.Count;
        }

        // ---------- переключение
        public async void Activate(string name)
        {
            var ws = Get(name);
            if (ws == null || busy) return;
            busy = true;
            try
            {
                Active = name;
                var h = Changed;
                if (h != null) h();
                SetStatus(L.T("Переключаюсь: ", "Switching: ") + name + "…");
                if (Main != null && Main.IsVisible && Main.WindowState == WindowState.Normal)
                    Main.WindowState = WindowState.Minimized;

                bool minimize = Data.MinimizeOthers;
                var launch = ws.Launch.ToList();
                bool doLaunch = launch.Count > 0 && (!Data.LaunchOnce || openedOnce.Add(name));
                var disp = Application.Current.Dispatcher;
                await Task.Run(() =>
                {
                    WindowManager.Activate(ws.Items,
                        s => disp.BeginInvoke(new Action(() => SetStatus(s))), minimize);
                    if (doLaunch)
                    {
                        foreach (var entry in launch)
                        {
                            try { Process.Start(new ProcessStartInfo(entry) { UseShellExecute = true }); }
                            catch (Exception ex) { Logger.Warn("Не удалось открыть " + entry, ex); }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка переключения пространства " + name, ex);
                SetStatus(L.T("Ошибка при переключении: ", "Switching failed: ") + ex.Message);
            }
            finally { busy = false; }
        }

        public void ShowMain()
        {
            Main.Show();
            if (Main.WindowState == WindowState.Minimized) Main.WindowState = WindowState.Normal;
            Main.Activate();
        }

        // ---------- обновления и лицензия
        public async Task CheckUpdatesAsync(bool manual)
        {
            if (UpdateResult.State == UpdateState.Checking) return;
            if (!Updates.IsConfigured)
            {
                UpdateResult = new UpdateCheckResult
                {
                    State = UpdateState.NotConfigured,
                    Message = L.T("Источник обновлений не настроен.", "The update source is not configured.")
                };
                RaiseUpdate();
                return;
            }
            UpdateResult = new UpdateCheckResult { State = UpdateState.Checking };
            RaiseUpdate();
            try
            {
                UpdateResult = await Updates.CheckAsync();
                Data.LastUpdateCheck = DateTime.UtcNow.ToString("o");
                SaveSoon();
            }
            catch (Exception ex)
            {
                Logger.Error("Сбой проверки обновлений", ex);
                UpdateResult = new UpdateCheckResult { State = UpdateState.Error, Message = ex.Message };
            }
            RaiseUpdate();
            if (!manual && UpdateResult.State == UpdateState.Available && Tray != null)
                Tray.Notify(L.T("Доступно обновление", "Update available"),
                            L.T("Версия ", "Version ") + UpdateResult.Info.VersionText);
        }

        void RaiseUpdate() { var h = UpdateChanged; if (h != null) h(); }

        public async Task<string> DownloadUpdateAsync()
        {
            var info = UpdateResult.Info;
            var prog = new Progress<double>(p => SetStatus(L.T("Загрузка обновления: ", "Downloading update: ") + (int)(p * 100) + "%"));
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Workspaces", "updates");
            var r = await Updates.DownloadAsync(info, dir, prog);
            SetStatus(r.Message);
            return r.Ok ? r.Path : null;
        }

        public async Task CheckLicenseAsync()
        {
            try { await Licenses.CheckAsync(); }
            catch (Exception ex) { Logger.Error("Сбой проверки лицензии", ex); }
        }

        // Фоновые задачи после запуска. Ничего из этого не блокирует работу приложения.
        public async void StartBackground()
        {
            try
            {
                Store.Instance.AutoBackupIfDue(TimeSpan.FromHours(12));
                await CheckLicenseAsync();
                if (Data.AutoUpdateCheck && Updates.IsConfigured)
                {
                    await Task.Delay(3000);
                    await CheckUpdatesAsync(false);
                }
            }
            catch (Exception ex) { Logger.Error("Сбой фоновых задач запуска", ex); }
        }

        public void Quit()
        {
            try { FlushSave(); }
            catch (Exception ex) { Logger.Error("Не удалось сохранить при выходе", ex); }
            if (Tray != null) Tray.Dispose();
            Application.Current.Shutdown();
        }
    }
}
