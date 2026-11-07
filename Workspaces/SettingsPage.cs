using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Workspaces
{
    // Страницы «Настройки», «О программе», баннер обновления и вспомогательные действия.
    public partial class MainWindow
    {
        // ---------------------------------------------------------------- баннер обновления
        Border BuildUpdateBanner()
        {
            var r = hub.UpdateResult;
            if (r == null || r.State != UpdateState.Available || r.Info == null)
                return new Border { Visibility = Visibility.Collapsed };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = L.T("Доступна новая версия ", "A new version is available: ") + r.Info.VersionText,
                Foreground = UI.B(Theme.OnAccent), FontWeight = FontWeights.SemiBold, FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0)
            });
            var more = new Border
            {
                Background = UI.B(Theme.Mix(Theme.Accent, "#000000", 0.2)), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 5, 12, 5), Cursor = Cursors.Hand,
                Child = new TextBlock { Text = L.T("Подробнее", "Details"), Foreground = UI.B(Theme.OnAccent), FontSize = 12, FontWeight = FontWeights.SemiBold }
            };
            more.MouseLeftButtonUp += (s, e) => ShowPage("settings", false);
            row.Children.Add(more);
            return new Border
            {
                Background = UI.B(Theme.Accent), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 8, 14, 8),
                Margin = new Thickness(0, 0, 0, 10), HorizontalAlignment = HorizontalAlignment.Left, Child = row
            };
        }

        // ---------------------------------------------------------------- настройки
        FrameworkElement BuildSettings()
        {
            var d = hub.Data;
            int dn = Eff.Density(d);
            bool pers = Entitlements.Has(Feature.Personalization);
            var sp = new StackPanel { MaxWidth = 800, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 14, 20) };
            sp.Children.Add(PageTitle(L.T("Настройки", "Settings")));

            // ---------- внешний вид
            sp.Children.Add(Sec(L.T("Внешний вид", "Appearance")));
            if (pers)
            {
                var tiles = new WrapPanel();
                foreach (var p in Theme.Presets) tiles.Children.Add(PresetTile(p));
                sp.Children.Add(Ctl.Block(L.T("Готовые темы", "Presets"),
                    L.T("Один клик меняет акцент, тему и цвет острова.", "One click sets accent, theme and island color."), tiles, dn));
            }
            if (Entitlements.Has(Feature.ThemeChoice))
            {
                string[] themes = { "dark", "light", "system" };
                sp.Children.Add(Ctl.Row(L.T("Тема", "Theme"), L.T("Оформление окна приложения.", "App window look."),
                    Ctl.Segment(new[] { L.T("Тёмная", "Dark"), L.T("Светлая", "Light"), L.T("Как в Windows", "System") },
                        Math.Max(0, Array.IndexOf(themes, d.Theme)),
                        i => { d.Theme = themes[i]; hub.ApplyAppearance(); }), dn));
            }
            if (pers)
            {
                sp.Children.Add(Ctl.ColorRow(L.T("Акцентный цвет", "Accent color"),
                    L.T("Кнопки, переключатели и выделение. Можно ввести свой HEX.", "Buttons, toggles and highlights. You can type your own HEX."),
                    d.Accent, UI.AccentPalette, c => { d.Accent = c; hub.ApplyAppearance(); }, dn));
                sp.Children.Add(Ctl.SliderRow(L.T("Скругление карточек", "Card roundness"), "", 0, 28, d.CardRadius, 1, " px",
                    v => { d.CardRadius = v; hub.Refresh(); }, dn));
                sp.Children.Add(Ctl.SliderRow(L.T("Размер карточек", "Card size"), "", 80, 130, d.CardSize, 5, " %",
                    v => { d.CardSize = v; hub.Refresh(); }, dn));
                sp.Children.Add(Ctl.Row(L.T("Плотность интерфейса", "Interface density"),
                    L.T("Отступы между карточками и блоками настроек.", "Spacing between cards and setting blocks."),
                    Ctl.Segment(new[] { L.T("Компактно", "Compact"), L.T("Обычно", "Normal"), L.T("Свободно", "Cozy") }, d.Density,
                        i => { d.Density = i; hub.ApplyAppearance(); }), dn));
            }
            sp.Children.Add(Ctl.Row(L.T("Язык", "Language"), L.T("Выбор сохраняется между запусками.", "Your choice is remembered."),
                Ctl.Segment(new[] { "Русский", "English" }, d.Lang == "en" ? 1 : 0,
                    i => { d.Lang = i == 1 ? "en" : "ru"; hub.ApplyAppearance(); }), dn));

            // ---------- остров
            sp.Children.Add(Sec("Virtual Notch"));
            sp.Children.Add(Ctl.Row(L.T("Показывать остров", "Show the island"),
                L.T("Панель выезжает, когда мышь у верхнего края экрана.", "The panel slides out when the mouse is at the top edge."),
                Ctl.Toggle(d.Island, v => { d.Island = v; hub.SaveSettings(); }), dn));
            sp.Children.Add(Ctl.Row(L.T("Предпросмотр", "Preview"),
                L.T("Показать остров на 3 секунды.", "Show the island for 3 seconds."),
                Ctl.Secondary(L.T("Показать", "Show"), () => hub.Island.Preview()), dn));
            if (pers)
            {
                sp.Children.Add(Ctl.ColorRow(L.T("Цвет острова", "Island color"),
                    L.T("Фон панели. Текст подбирается автоматически.", "Panel background. Text color adapts automatically."),
                    d.IslandBg, UI.IslandPalette, c => { d.IslandBg = c; hub.ApplyAppearance(); }, dn));
                sp.Children.Add(Ctl.SliderRow(L.T("Размер шрифта", "Font size"), "", 11, 18, d.IslandFont, 1, " px",
                    v => { d.IslandFont = v; hub.Refresh(); }, dn));
                sp.Children.Add(Ctl.Row(L.T("Содержимое блоков", "Block content"),
                    L.T("Показывать названия или только значки.", "Show names or icons only."),
                    Ctl.Segment(new[] { L.T("Значок и название", "Icon and name"), L.T("Только значок", "Icon only") },
                        d.IslandNames ? 0 : 1, i => { d.IslandNames = i == 0; hub.Refresh(); }), dn));
                sp.Children.Add(Ctl.Row(L.T("Положение", "Position"), L.T("Где остров прижат к верхнему краю.", "Where the island sits on the top edge."),
                    Ctl.Segment(new[] { L.T("По центру", "Center"), L.T("Слева", "Left"), L.T("Справа", "Right") },
                        d.IslandPos, i => { d.IslandPos = i; hub.Refresh(); }), dn));
            }
            if (Entitlements.Has(Feature.IslandOpacity))
                sp.Children.Add(Ctl.SliderRow(L.T("Прозрачность", "Opacity"), "", 30, 100, d.IslandOpacity, 1, " %",
                    v => { d.IslandOpacity = v; hub.Refresh(); }, dn));
            if (Entitlements.Has(Feature.IslandSize))
                sp.Children.Add(Ctl.SliderRow(L.T("Высота", "Height"), "", 40, 80, d.IslandHeight, 1, " px",
                    v => { d.IslandHeight = v; hub.Refresh(); }, dn));
            if (Entitlements.Has(Feature.MultiMonitor))
            {
                int screens = UI.Screens().Length;
                var mon = new string[screens];
                for (int i = 0; i < screens; i++)
                    mon[i] = i == 0 ? L.T("Основной", "Primary") : L.T("Монитор ", "Monitor ") + (i + 1);
                sp.Children.Add(Ctl.Row(L.T("Монитор", "Monitor"), L.T("На каком экране показывать остров.", "Which screen shows the island."),
                    Ctl.Segment(mon, Math.Min(d.Monitor, screens - 1), i => { d.Monitor = i; hub.Refresh(); }), dn));
            }
            if (Entitlements.Has(Feature.AdvancedAnimations))
            {
                sp.Children.Add(Ctl.Row(L.T("Анимация", "Animation"), L.T("Как остров выезжает и прячется.", "How the island slides in and out."),
                    Ctl.Segment(new[] { L.T("Пружина", "Spring"), L.T("Плавно", "Smooth"), L.T("Без анимации", "None") },
                        d.AnimType, i => { d.AnimType = i; hub.Refresh(); }), dn));
                sp.Children.Add(Ctl.SliderRow(L.T("Скорость анимации", "Animation speed"), "", 50, 200, d.AnimSpeed, 10, " %",
                    v => { d.AnimSpeed = v; hub.Refresh(); }, dn));
            }
            sp.Children.Add(Ctl.SliderRow(L.T("Ширина зоны срабатывания", "Trigger width"),
                L.T("Область у верхнего края, где остров реагирует на мышь.", "Area on the top edge where the island reacts to the mouse."),
                80, 800, d.TriggerW, 10, " px", v => { d.TriggerW = v; hub.Refresh(); }, dn));
            sp.Children.Add(Ctl.SliderRow(L.T("Высота зоны срабатывания", "Trigger height"), "", 1, 20, d.TriggerH, 1, " px",
                v => { d.TriggerH = v; hub.Refresh(); }, dn));
            sp.Children.Add(Ctl.SliderRow(L.T("Задержка скрытия", "Hide delay"),
                L.T("Через сколько остров прячется после ухода мыши.", "How long before the island hides after the mouse leaves."),
                150, 1500, d.HideDelay, 50, L.T(" мс", " ms"), v => { d.HideDelay = v; hub.SaveSettings(); }, dn));

            // ---------- переключение
            sp.Children.Add(Sec(L.T("Переключение", "Switching")));
            sp.Children.Add(Ctl.Row(L.T("Сворачивать остальные окна", "Minimize other windows"),
                L.T("При переключении скрывать окна, которые не входят в пространство.", "Hide windows that are not part of the workspace."),
                Ctl.Toggle(d.MinimizeOthers, v => { d.MinimizeOthers = v; hub.SaveSettings(); }), dn));
            sp.Children.Add(Ctl.Row(L.T("Файлы и ссылки: один раз за сеанс", "Files and links: once per session"),
                L.T("Иначе они откроются при каждом переключении (в браузере появятся дубли вкладок).",
                    "Otherwise they open on every switch (browsers will get duplicate tabs)."),
                Ctl.Toggle(d.LaunchOnce, v => { d.LaunchOnce = v; hub.SaveSettings(); }), dn));

            // ---------- система
            sp.Children.Add(Sec(L.T("Система", "System")));
            sp.Children.Add(Ctl.Row(L.T("Запускать вместе с Windows", "Start with Windows"),
                L.T("Приложение стартует свёрнутым: остров и трей, без главного окна.", "Starts minimized: island and tray, no main window."),
                Ctl.Toggle(Autostart.IsEnabled(), v =>
                {
                    try { Autostart.Set(v); }
                    catch (Exception ex)
                    {
                        Logger.Error("Не удалось изменить автозапуск", ex);
                        hub.SetStatus(L.T("Не удалось изменить автозапуск.", "Could not change autostart."));
                    }
                }), dn));
            sp.Children.Add(Ctl.Row(L.T("Закрытие окна сворачивает в трей", "Close button hides to tray"),
                L.T("Выход: значок в трее или остров.", "Exit via the tray icon or the island."),
                Ctl.Toggle(d.CloseToTray, v => { d.CloseToTray = v; hub.SaveSettings(); }), dn));

            // ---------- обновления
            sp.Children.Add(Sec(L.T("Обновления", "Updates")));
            sp.Children.Add(Ctl.Row(L.T("Текущая версия", "Current version"), "",
                new TextBlock { Text = UI.Version, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = UI.B(Theme.Fg) }, dn));
            sp.Children.Add(Ctl.Row(L.T("Проверять при запуске", "Check on startup"),
                L.T("Проверка идёт в фоне и не мешает работе.", "The check runs in the background."),
                Ctl.Toggle(d.AutoUpdateCheck, v => { d.AutoUpdateCheck = v; hub.SaveSettings(); }), dn));
            bool checking = hub.UpdateResult.State == UpdateState.Checking;
            sp.Children.Add(Ctl.Row(L.T("Проверка обновлений", "Check for updates"), UpdateStatusText(),
                Ctl.Secondary(checking ? L.T("Проверяю…", "Checking…") : L.T("Проверить", "Check now"),
                    async () => { if (!checking) await hub.CheckUpdatesAsync(true); }), dn));
            if (hub.UpdateResult.State == UpdateState.Available && hub.UpdateResult.Info != null)
                sp.Children.Add(BuildUpdateDetails(hub.UpdateResult.Info, dn));

            // ---------- данные и резервные копии
            sp.Children.Add(Sec(L.T("Данные", "Data")));
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            var export = Ctl.Secondary(L.T("Экспорт", "Export"), ExportData);
            var import = Ctl.Secondary(L.T("Импорт", "Import"), ImportData);
            var folder = Ctl.Secondary(L.T("Папка", "Folder"), OpenFolder);
            import.Margin = new Thickness(8, 0, 8, 0);
            btns.Children.Add(export);
            btns.Children.Add(import);
            btns.Children.Add(folder);
            sp.Children.Add(Ctl.Row(L.T("Экспорт и импорт", "Export and import"),
                L.T("Сохраните пространства в файл или загрузите их из файла.", "Save workspaces to a file or load them from a file."), btns, dn));

            var bk = new StackPanel { Orientation = Orientation.Horizontal };
            var bkCreate = Ctl.Secondary(L.T("Создать", "Create"), CreateBackupNow);
            var bkRestore = Ctl.Secondary(L.T("Восстановить…", "Restore…"), RestoreBackup);
            bkRestore.Margin = new Thickness(8, 0, 0, 0);
            bk.Children.Add(bkCreate);
            bk.Children.Add(bkRestore);
            sp.Children.Add(Ctl.Row(L.T("Резервные копии", "Backups"),
                L.T("Создаются автоматически при запуске и перед удалением, импортом и восстановлением. Хранятся последние "
                    + DataStore.MaxBackups + ".",
                    "Created automatically on startup and before delete, import and restore. The latest "
                    + DataStore.MaxBackups + " are kept."), bk, dn));
            sp.Children.Add(Ctl.Row(L.T("Журнал ошибок", "Error log"), Logger.LogDir,
                Ctl.Secondary(L.T("Открыть", "Open"), () => OpenPath(Logger.LogDir)), dn));

            // ---------- лицензия
            sp.Children.Add(Sec(L.T("Лицензия", "License")));
            sp.Children.Add(BuildLicenseRow(dn));

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };
            bool ready = false;
            scroll.Loaded += (s, e) => { scroll.ScrollToVerticalOffset(settingsOffset); ready = true; };
            scroll.ScrollChanged += (s, e) => { if (ready) settingsOffset = scroll.VerticalOffset; };
            return scroll;
        }

        string UpdateStatusText()
        {
            var r = hub.UpdateResult;
            string last = "";
            DateTime t;
            if (!string.IsNullOrEmpty(hub.Data.LastUpdateCheck) && DateTime.TryParse(hub.Data.LastUpdateCheck, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out t))
                last = "  " + L.T("Последняя проверка: ", "Last check: ") + t.ToLocalTime().ToString("g");
            switch (r.State)
            {
                case UpdateState.Checking: return L.T("Проверяю…", "Checking…");
                case UpdateState.Available: return L.T("Доступна версия ", "Version available: ") + r.Info.VersionText + last;
                case UpdateState.UpToDate: return r.Message + last;
                case UpdateState.NotChecked: return L.T("Ещё не проверялось.", "Not checked yet.") + last;
                default: return r.Message;
            }
        }

        FrameworkElement BuildUpdateDetails(UpdateInfo info, int dn)
        {
            var body = new StackPanel();
            if (info.Notes.Count > 0)
            {
                body.Children.Add(new TextBlock
                {
                    Text = L.T("Что нового:", "What's new:"), FontSize = 12, FontWeight = FontWeights.SemiBold,
                    Foreground = UI.B(Theme.Fg), Margin = new Thickness(0, 0, 0, 4)
                });
                foreach (var n in info.Notes)
                    body.Children.Add(new TextBlock
                    {
                        Text = "•  " + n, FontSize = 12, Foreground = UI.B(Theme.Muted), TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 2)
                    });
            }
            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            if (!string.IsNullOrEmpty(info.PageUrl))
                btns.Children.Add(Ctl.Secondary(L.T("Страница загрузки", "Download page"), () => OpenPath(info.PageUrl)));
            if (hub.Updates.CanVerifySignature && !string.IsNullOrEmpty(info.DownloadUrl))
            {
                var dl = Ctl.Primary(L.T("Скачать и проверить", "Download and verify"), async () =>
                {
                    string path = await hub.DownloadUpdateAsync();
                    if (path == null) return;
                    if (MessageBox.Show(this, L.T("Файл загружен и прошёл проверку подлинности:\n", "The file was downloaded and verified:\n")
                            + path + "\n\n" + L.T("Запустить установщик сейчас?", "Run the installer now?"),
                            Entitlements.Edition, MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        OpenPath(path);
                });
                dl.Margin = new Thickness(8, 0, 0, 0);
                btns.Children.Add(dl);
            }
            body.Children.Add(btns);
            return Ctl.Block(L.T("Версия ", "Version ") + info.VersionText, "", body, dn);
        }

        FrameworkElement BuildLicenseRow(int dn)
        {
            var s = hub.Licenses.Current;
            string title;
            switch (s.State)
            {
                case LicenseState.Pro: title = "PRO"; break;
                case LicenseState.Invalid: title = L.T("Ключ недействителен", "Invalid key"); break;
                case LicenseState.Expired: title = L.T("Срок лицензии истёк", "License expired"); break;
                case LicenseState.Unverified: title = L.T("Не удалось проверить", "Not verified"); break;
                default: title = L.T("Free: бесплатная версия", "Free version"); break;
            }
            string desc = L.T("Бесплатные функции работают без лицензии и без интернета. ", "Free features work without a license or internet. ")
                          + L.T("Максимум пространств: ", "Workspace limit: ")
                          + (Entitlements.MaxWorkspaces == int.MaxValue ? "∞" : Entitlements.MaxWorkspaces.ToString()) + ".";
            if (!string.IsNullOrEmpty(s.Message)) desc += " " + s.Message;
            if (!hub.Licenses.ProviderConfigured)
                desc += " " + L.T("Активация PRO-ключа пока недоступна.", "PRO key activation is not available yet.");

            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(new TextBlock
            {
                Text = title, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = UI.B(Theme.Accent),
                VerticalAlignment = VerticalAlignment.Center
            });
            if (hub.Licenses.ProviderConfigured || hub.Licenses.HasLocalLicense)
            {
                var re = Ctl.Secondary(L.T("Проверить снова", "Recheck"), async () => await hub.CheckLicenseAsync());
                re.Margin = new Thickness(12, 0, 0, 0);
                right.Children.Add(re);
            }
            return Ctl.Row(L.T("Статус лицензии", "License status"), desc, right, dn);
        }

        Border PresetTile(Preset p)
        {
            var d = hub.Data;
            bool match = d.Theme == p.Theme && string.Equals(d.Accent, p.Accent, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(d.IslandBg, p.IslandBg, StringComparison.OrdinalIgnoreCase);
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new Border
            {
                Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = UI.B(p.Accent),
                Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock
            {
                Text = L.T(p.Name, p.NameEn), FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = UI.B(Theme.Contrast(p.IslandBg)), VerticalAlignment = VerticalAlignment.Center
            });
            var tile = new Border
            {
                Width = 132, Height = 46, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 8, 8),
                Background = UI.B(p.IslandBg), Padding = new Thickness(14, 0, 8, 0), Cursor = Cursors.Hand,
                BorderThickness = new Thickness(2), BorderBrush = match ? UI.B(Theme.Accent) : UI.B("#40808080"),
                Child = row
            };
            tile.MouseLeftButtonUp += (s, e) =>
            {
                d.Theme = p.Theme; d.Accent = p.Accent; d.IslandBg = p.IslandBg; d.IslandOpacity = p.Opacity;
                hub.ApplyAppearance();
            };
            return tile;
        }

        // ---------------------------------------------------------------- о программе
        FrameworkElement BuildAbout()
        {
            var sp = new StackPanel();
            sp.Children.Add(PageTitle(L.T("О программе", "About")));
            sp.Children.Add(new TextBlock
            {
                Text = Entitlements.Edition + "  v" + UI.Version, FontSize = 18, FontWeight = FontWeights.SemiBold,
                Foreground = UI.B(Theme.Fg), Margin = new Thickness(0, 22, 0, 6)
            });
            sp.Children.Add(new TextBlock
            {
                Text = L.T("Запоминает раскладку окон и переключает рабочие пространства одним кликом: "
                           + "лишнее сворачивается, нужные программы запускаются и встают на свои места.",
                           "Remembers your window layout and switches workspaces in one click: "
                           + "extra windows are minimized, needed apps are launched and put in place."),
                FontSize = 14, Foreground = UI.B(Theme.Fg), TextWrapping = TextWrapping.Wrap, MaxWidth = 560
            });
            sp.Children.Add(Sec(L.T("Лицензия", "License")));
            sp.Children.Add(new TextBlock
            {
                Text = LicenseSummary(),
                FontSize = 13, Foreground = UI.B(Theme.Muted)
            });
            sp.Children.Add(Sec(L.T("Как пользоваться островом", "Using the island")));
            sp.Children.Add(new TextBlock
            {
                Text = L.T("Подведите мышь к верхнему краю экрана, и остров выедет. Нажмите на блок пространства, "
                           + "чтобы переключиться. Уберите мышь, и остров спрячется.",
                           "Move the mouse to the top edge and the island slides out. Click a workspace to switch. "
                           + "Move the mouse away and it hides."),
                FontSize = 13, Foreground = UI.B(Theme.Muted), TextWrapping = TextWrapping.Wrap, MaxWidth = 560
            });
            sp.Children.Add(Sec(L.T("Справка", "Help")));
            sp.Children.Add(LinkLine(L.T("Предложения новых функций и вопросы: Telegram ", "Feature requests and questions: Telegram "),
                "@Pharaohhh", "https://t.me/Pharaohhh"));
            sp.Children.Add(LinkLine(L.T("Обновления берутся из: ", "Updates come from: "),
                "github.com/Pch-Nick/Workspaces", "https://github.com/Pch-Nick/Workspaces"));
            sp.Children.Add(Sec(L.T("Данные", "Data")));
            sp.Children.Add(new TextBlock { Text = Store.Dir, FontSize = 12, Foreground = UI.B(Theme.Muted), TextWrapping = TextWrapping.Wrap });
            return sp;
        }

        string LicenseSummary()
        {
            if (hub.Licenses.Current.State == LicenseState.Pro) return "PRO";
            if (Entitlements.MaxWorkspaces == int.MaxValue)
                return L.T("Бесплатная версия: все функции доступны, без ограничений.", "Free version: all features are available, no limits.");
            return L.T("Free: бесплатная версия, до ", "Free version, up to ") + Entitlements.FreeMaxWorkspaces + L.T(" пространств", " workspaces");
        }

        static string LimitNote(bool ru)
        {
            if (Entitlements.MaxWorkspaces == int.MaxValue) return "";
            return ru ? "В бесплатной версии доступно до " + Entitlements.FreeMaxWorkspaces + " пространств."
                      : "The Free version supports up to " + Entitlements.FreeMaxWorkspaces + " workspaces.";
        }

        FrameworkElement LinkLine(string prefix, string linkText, string url)
        {
            var tb = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = UI.B(Theme.Muted), Margin = new Thickness(0, 0, 0, 4) };
            tb.Inlines.Add(new Run(prefix));
            var link = new Hyperlink(new Run(linkText)) { Foreground = UI.B(Theme.Accent), Cursor = Cursors.Hand };
            link.Click += (s, e) => OpenPath(url);
            tb.Inlines.Add(link);
            return tb;
        }

        // ---------------------------------------------------------------- действия
        void OpenPath(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                Logger.Warn("Не удалось открыть " + target, ex);
                Say(L.T("Не удалось открыть: ", "Could not open: ") + target);
            }
        }

        void EditApps(string name)
        {
            var w = hub.Get(name);
            if (w == null) return;
            var dlg = new AppsDialog(this, w);
            if (dlg.ShowDialog() != true) return;
            w.Items = dlg.Result;
            hub.Commit();
        }

        void CreateBackupNow()
        {
            hub.FlushSave();
            string p = Store.Instance.CreateBackup("manual");
            Say(p != null ? L.T("Резервная копия создана.", "Backup created.")
                          : L.T("Не удалось создать резервную копию. Подробности в журнале.", "Could not create a backup. See the log."));
        }

        void RestoreBackup()
        {
            var list = Store.Instance.ListBackups();
            if (list.Count == 0)
            {
                MessageBox.Show(this, L.T("Резервных копий пока нет.", "There are no backups yet."), Entitlements.Edition);
                return;
            }
            var dlg = new BackupsDialog(this, list);
            if (dlg.ShowDialog() != true || dlg.SelectedPath == null) return;
            var data = Store.Instance.TryRead(dlg.SelectedPath);
            if (data == null)
            {
                MessageBox.Show(this, L.T("Эта копия повреждена и не может быть восстановлена.", "This backup is damaged and cannot be restored."), Entitlements.Edition);
                return;
            }
            if (MessageBox.Show(this, L.T("Заменить текущие пространства (", "Replace current workspaces (") + hub.Data.Workspaces.Count
                    + L.T(") данными из копии (", ") with the backup (") + data.Workspaces.Count + ")?",
                    Entitlements.Edition, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            int skipped;
            int n = hub.ReplaceWorkspaces(data.Workspaces, out skipped);
            Say(L.T("Восстановлено пространств: ", "Workspaces restored: ") + n
                + (skipped > 0 ? L.T(". Пропущено из-за лимита Free: ", ". Skipped because of the Free limit: ") + skipped : ""));
        }

        // Сообщения после запуска: восстановление данных и приветствие при первом запуске.
        public void ShowStartupMessages()
        {
            try
            {
                if (!string.IsNullOrEmpty(hub.StartupNotice))
                {
                    MessageBox.Show(this, hub.StartupNotice, Entitlements.Edition, MessageBoxButton.OK, MessageBoxImage.Warning);
                    hub.StartupNotice = null;
                }
                if (!hub.Data.FirstRunDone)
                {
                    hub.Data.FirstRunDone = true;
                    hub.Commit();
                    MessageBox.Show(this,
                        L.T("Добро пожаловать в Workspaces Free!\n\n1. Откройте нужные программы и расставьте окна.\n"
                            + "2. Нажмите «Запомнить пространство» и введите название.\n"
                            + "3. Переключайтесь кликом по карточке или через остров: подведите мышь к верху экрана.\n\n"
                            + LimitNote(true),
                            "Welcome to Workspaces Free!\n\n1. Open your apps and arrange the windows.\n"
                            + "2. Press “Save workspace” and name it.\n"
                            + "3. Switch by clicking a card or via the island: move the mouse to the top of the screen.\n\n"
                            + LimitNote(false)),
                        Entitlements.Edition);
                }
            }
            catch (Exception ex) { Logger.Error("Ошибка сообщений при запуске", ex); }
        }
    }
}
