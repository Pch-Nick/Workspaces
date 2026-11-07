using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Workspaces
{
    public partial class MainWindow : Window
    {
        class NavRef { public Border Box; public Border Bar; public TextBlock Icon; public TextBlock Text; }

        readonly Hub hub;
        Border pageHost;
        readonly Dictionary<string, NavRef> nav = new Dictionary<string, NavRef>();
        WrapPanel cards;
        TextBlock status;
        FrameworkElement wsPage;
        string current = "";
        string lastStatus = "";
        int lastCount = -1;
        double settingsOffset = 0;

        public MainWindow(Hub hub)
        {
            this.hub = hub;
            Title = "Workspaces";
            Width = 1100; Height = 740; MinWidth = 820; MinHeight = 500;
            FontFamily = new FontFamily("Segoe UI");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            hub.Changed += () => Render();
            hub.AppearanceChanged += () => BuildUI();
            hub.UpdateChanged += () => BuildUI();
            Icon = AppIcon.Get();
            hub.StatusChanged += s => { lastStatus = s; if (status != null) status.Text = s; };
            SourceInitialized += (s, e) => ApplyTitleBar();
            Closing += (s, e) =>
            {
                if (hub.Data.CloseToTray) { e.Cancel = true; Hide(); }
                else hub.Quit();
            };
            BuildUI();
        }

        void ApplyTitleBar()
        {
            IntPtr h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return;
            int on = Theme.Dark ? 1 : 0;
            Native.DwmSetWindowAttribute(h, 20, ref on, 4);
        }

        // Полная перерисовка интерфейса (при смене темы, акцента, языка).
        void BuildUI()
        {
            Title = Entitlements.Edition;
            Background = UI.B(Theme.Bg);
            nav.Clear();
            cards = new WrapPanel();
            status = new TextBlock { Text = lastStatus };
            pageHost = new Border { Margin = new Thickness(32, 26, 32, 14) };

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var side = BuildSidebar();
            Grid.SetColumn(side, 0);
            root.Children.Add(side);
            Grid.SetColumn(pageHost, 1);
            root.Children.Add(pageHost);
            Content = root;

            wsPage = BuildWorkspacesPage();
            lastCount = -1;
            Render();
            ShowPage(string.IsNullOrEmpty(current) ? "ws" : current, true);
            ApplyTitleBar();
        }

        // ---------------------------------------------------------------- боковое меню
        FrameworkElement BuildSidebar()
        {
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var logo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 28, 16, 24) };
            logo.Children.Add(new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = UI.B(Theme.Accent),
                Child = new TextBlock
                {
                    Text = "\uE71D", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14,
                    Foreground = UI.B(Theme.OnAccent), HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
            logo.Children.Add(new TextBlock
            {
                Text = "Workspaces", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = UI.B(Theme.Fg),
                Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            });
            g.Children.Add(logo);

            var items = new StackPanel();
            items.Children.Add(NavItem("ws", "\uE71D", L.T("Пространства", "Workspaces")));
            items.Children.Add(NavItem("settings", "\uE713", L.T("Настройки", "Settings")));
            items.Children.Add(NavItem("about", "\uE946", L.T("О программе", "About")));
            Grid.SetRow(items, 1);
            g.Children.Add(items);

            var ver = new TextBlock
            {
                Text = "v" + UI.Version + "  ·  " + (Entitlements.IsPro ? "PRO" : "Free"), FontSize = 12, Foreground = UI.B(Theme.Dim), Margin = new Thickness(24, 0, 0, 18)
            };
            Grid.SetRow(ver, 2);
            g.Children.Add(ver);

            return new Border { Background = UI.B(Theme.Sidebar), Child = g };
        }

        Border NavItem(string key, string glyph, string text)
        {
            var bar = new Border
            {
                Width = 3, Height = 18, CornerRadius = new CornerRadius(2), Background = UI.B(Theme.Accent),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            var icon = new TextBlock
            {
                Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16, Width = 24,
                VerticalAlignment = VerticalAlignment.Center
            };
            var label = new TextBlock
            {
                Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0) };
            row.Children.Add(icon);
            row.Children.Add(label);
            var inner = new Grid();
            inner.Children.Add(bar);
            inner.Children.Add(row);
            var box = new Border
            {
                Height = 42, CornerRadius = new CornerRadius(10), Margin = new Thickness(10, 2, 10, 2),
                Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = inner
            };
            box.MouseEnter += (s, e) => { if (current != key) box.Background = UI.B(Theme.NavHover); };
            box.MouseLeave += (s, e) => { if (current != key) box.Background = Brushes.Transparent; };
            box.MouseLeftButtonUp += (s, e) => ShowPage(key, false);
            nav[key] = new NavRef { Box = box, Bar = bar, Icon = icon, Text = label };
            return box;
        }

        void ShowPage(string key, bool keepScroll)
        {
            if (key == "settings" && !keepScroll) settingsOffset = 0;
            current = key;
            foreach (var kv in nav)
            {
                bool sel = kv.Key == key;
                kv.Value.Box.Background = sel ? UI.B(Theme.NavSel) : Brushes.Transparent;
                kv.Value.Bar.Visibility = sel ? Visibility.Visible : Visibility.Collapsed;
                kv.Value.Icon.Foreground = UI.B(sel ? Theme.Accent : Theme.Muted);
                kv.Value.Text.Foreground = UI.B(sel ? Theme.Fg : Theme.Muted);
            }
            pageHost.Child = key == "settings" ? BuildSettings() : key == "about" ? BuildAbout() : wsPage;

            var slide = new TranslateTransform(0, 10);
            pageHost.RenderTransform = slide;
            pageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            slide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        // ---------------------------------------------------------------- страницы
        TextBlock PageTitle(string text)
        {
            return new TextBlock
            {
                Text = text, FontSize = 28, FontWeight = FontWeights.Bold, Foreground = UI.B(Theme.Fg),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        TextBlock Sec(string text)
        {
            return new TextBlock
            {
                Text = text.ToUpper(), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = UI.B(Theme.Accent),
                Margin = new Thickness(2, 20, 0, 8)
            };
        }

        FrameworkElement BuildWorkspacesPage()
        {
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(PageTitle(L.T("Рабочие пространства", "Workspaces")));
            var add = Ctl.Primary(L.T("＋  Запомнить пространство", "＋  Save workspace"), Remember);
            Grid.SetColumn(add, 1);
            head.Children.Add(add);
            g.Children.Add(head);

            var hint = new TextBlock
            {
                Text = L.T("Откройте нужные программы, расставьте окна и нажмите кнопку. Клик по карточке переключает пространство, "
                           + "перетаскивание меняет порядок, правый клик открывает меню.",
                           "Open your apps, arrange the windows and press the button. Click a card to switch, "
                           + "drag to reorder, right-click for the menu."),
                Foreground = UI.B(Theme.Muted), FontSize = 13, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 10)
            };
            var hintBox = new StackPanel();
            hintBox.Children.Add(hint);
            hintBox.Children.Add(BuildUpdateBanner());
            Grid.SetRow(hintBox, 1);
            g.Children.Add(hintBox);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = cards };
            Grid.SetRow(scroll, 2);
            g.Children.Add(scroll);

            status.Foreground = UI.B(Theme.Muted);
            status.FontSize = 12;
            status.Margin = new Thickness(0, 8, 0, 0);
            Grid.SetRow(status, 3);
            g.Children.Add(status);
            return g;
        }

        // ---------------------------------------------------------------- карточки
        void Render()
        {
            if (cards == null) return;
            bool animate = hub.Data.Workspaces.Count != lastCount;
            lastCount = hub.Data.Workspaces.Count;
            cards.Children.Clear();
            if (lastCount == 0)
            {
                cards.Children.Add(new TextBlock
                {
                    Text = L.T("Пока нет ни одного пространства.\nОткройте программы, расставьте окна и нажмите «Запомнить».",
                               "No workspaces yet.\nOpen your apps, arrange the windows and press “Save workspace”."),
                    Foreground = UI.B(Theme.Muted), FontSize = 15, Margin = new Thickness(8, 40, 0, 0)
                });
            }
            for (int i = 0; i < lastCount; i++) cards.Children.Add(MakeCard(hub.Data.Workspaces[i], i, animate));
        }

        string CountText(int n)
        {
            if (L.Lang == "en") return n + (n == 1 ? " window" : " windows");
            string w = (n % 10 == 1 && n % 100 != 11) ? "окно"
                : (n % 10 >= 2 && n % 10 <= 4 && !(n % 100 >= 12 && n % 100 <= 14)) ? "окна" : "окон";
            return n + " " + w;
        }

        Border MakeCard(Workspace w, int index, bool animate)
        {
            var d = hub.Data;
            bool active = w.Name == hub.Active;
            double scaleK = Math.Max(80, Math.Min(130, Eff.CardSize(d))) / 100.0;
            int radius = Math.Max(0, Math.Min(28, Eff.CardRadius(d)));
            int dens = Eff.Density(d);
            double margin = dens == 0 ? 4 : dens == 2 ? 14 : 8;

            var names = new List<string>();
            foreach (var it in w.Items)
            {
                string n = Path.GetFileNameWithoutExtension(it.Exe);
                if (n.Length > 0) n = char.ToUpper(n[0]) + n.Substring(1);
                if (!names.Contains(n)) names.Add(n);
            }
            string apps = string.Join(", ", names.Take(4)) + (names.Count > 4 ? " …" : "");

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            if (!string.IsNullOrEmpty(w.Icon))
                titleRow.Children.Add(new TextBlock
                {
                    Text = w.Icon, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 20,
                    Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                    Foreground = UI.B(Theme.Fg)
                });
            titleRow.Children.Add(new TextBlock
            {
                Text = w.Name, FontSize = 19, FontWeight = FontWeights.Bold, Foreground = UI.B(Theme.Fg),
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 250 * scaleK - 90,
                VerticalAlignment = VerticalAlignment.Center
            });

            string sub = CountText(w.Items.Count);
            if (!string.IsNullOrEmpty(w.Hotkey)) sub += "  ·  " + w.Hotkey;
            if (w.Launch.Count > 0) sub += "  ·  +" + w.Launch.Count;

            var body = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
            body.Children.Add(titleRow);
            body.Children.Add(new TextBlock
            {
                Text = sub, FontSize = 12, Foreground = UI.B(Theme.Muted), Margin = new Thickness(0, 4, 0, 10),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            body.Children.Add(new TextBlock
            {
                Text = apps, FontSize = 12, Foreground = UI.B(Theme.Muted), TextWrapping = TextWrapping.Wrap
            });

            var inner = new StackPanel();
            inner.Children.Add(new Border
            {
                Height = 5, Background = UI.B(w.Color),
                CornerRadius = new CornerRadius(Math.Max(0, radius - 2), Math.Max(0, radius - 2), 0, 0)
            });
            inner.Children.Add(body);

            var scale = new ScaleTransform(1, 1);
            var move = new TranslateTransform(0, animate ? 14 : 0);
            var tg = new TransformGroup();
            tg.Children.Add(scale);
            tg.Children.Add(move);

            var card = new Border
            {
                Width = 250 * scaleK, Height = 160 * scaleK, Margin = new Thickness(margin),
                CornerRadius = new CornerRadius(radius), ClipToBounds = true,
                Background = UI.B(Theme.Card), BorderThickness = new Thickness(2),
                BorderBrush = UI.B(active ? w.Color : Theme.Card),
                Child = inner, Cursor = Cursors.Hand, AllowDrop = true,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = tg
            };

            card.MouseEnter += (s, e) =>
            {
                var du = TimeSpan.FromMilliseconds(150);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.03, du));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.03, du));
                card.Background = UI.B(Theme.CardHover);
            };
            card.MouseLeave += (s, e) =>
            {
                var du = TimeSpan.FromMilliseconds(150);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, du));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, du));
                card.Background = UI.B(Theme.Card);
            };

            string name = w.Name;

            // клик переключает, перетаскивание меняет порядок
            Point? dragStart = null;
            bool didDrag = false;
            card.PreviewMouseLeftButtonDown += (s, e) => { dragStart = e.GetPosition(null); didDrag = false; };
            card.PreviewMouseMove += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed && dragStart.HasValue)
                {
                    Point pnt = e.GetPosition(null);
                    if (Math.Abs(pnt.X - dragStart.Value.X) > 8 || Math.Abs(pnt.Y - dragStart.Value.Y) > 8)
                    {
                        dragStart = null;
                        didDrag = true;
                        DragDrop.DoDragDrop(card, new DataObject("ws", name), DragDropEffects.Move);
                    }
                }
            };
            card.MouseLeftButtonUp += (s, e) =>
            {
                dragStart = null;
                if (didDrag) { didDrag = false; return; }
                hub.Activate(name);
            };
            card.DragOver += (s, e) =>
            {
                e.Effects = e.Data.GetDataPresent("ws") ? DragDropEffects.Move : DragDropEffects.None;
                e.Handled = true;
            };
            card.Drop += (s, e) =>
            {
                if (e.Data.GetDataPresent("ws"))
                {
                    hub.MoveBefore((string)e.Data.GetData("ws"), name);
                    e.Handled = true;
                }
            };

            var menu = new ContextMenu();
            AddMenu(menu, L.T("Приложения…", "Apps…"), () => EditApps(name));
            AddMenu(menu, L.T("Значок и цвет…", "Icon and color…"), () => EditLook(name));
            AddMenu(menu, L.T("Горячая клавиша…", "Hotkey…"), () => EditHotkey(name));
            AddMenu(menu, L.T("Файлы и ссылки…", "Files and links…"), () => EditLaunch(name));
            menu.Items.Add(new Separator());
            AddMenu(menu, L.T("Обновить раскладку по текущим окнам", "Update layout from current windows"), () => UpdateLayout(name));
            AddMenu(menu, L.T("Переименовать", "Rename"), () => RenameWs(name));
            menu.Items.Add(new Separator());
            AddMenu(menu, L.T("Удалить", "Delete"), () => DeleteWs(name));
            card.ContextMenu = menu;

            if (animate)
            {
                card.Opacity = 0;
                var delay = TimeSpan.FromMilliseconds(45 * index);
                card.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { BeginTime = delay });
                move.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(380))
                    { BeginTime = delay, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            return card;
        }

        static void AddMenu(ContextMenu menu, string header, Action act)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (s, e) => act();
            menu.Items.Add(mi);
        }

        // ---------------------------------------------------------------- действия
        void Say(string text)
        {
            hub.SetStatus(text);
        }

        void Remember()
        {
            if (!hub.CanCreateWorkspace())
            {
                MessageBox.Show(this,
                    L.T("Достигнут лимит бесплатной версии: ", "You have reached the Free version limit: ") + Entitlements.FreeMaxWorkspaces
                    + L.T(" пространства. В будущем появится версия PRO без этого ограничения. "
                          + "Уже созданные пространства продолжают работать, их можно переименовывать, обновлять и удалять.",
                          " workspaces. A PRO version without this limit is planned for the future. "
                          + "Existing workspaces keep working; you can rename, update and delete them."),
                    Entitlements.Edition);
                return;
            }
            var items = WindowManager.Capture();
            if (items.Count == 0)
            {
                MessageBox.Show(this, L.T("Не нашёл открытых окон. Откройте нужные программы и попробуйте снова.",
                    "No open windows found. Open your apps and try again."), "Workspaces");
                return;
            }
            string name = InputDialog.Ask(this, L.T("Новое пространство", "New workspace"),
                L.T("Название (окон: ", "Name (windows: ") + items.Count + "):");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            var ex = hub.Get(name);
            if (ex != null)
            {
                if (MessageBox.Show(this, "«" + name + "» " + L.T("уже есть. Перезаписать?", "already exists. Overwrite?"),
                        "Workspaces", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                ex.Items = items;
            }
            else
            {
                hub.Data.Workspaces.Add(new Workspace
                {
                    Name = name,
                    Color = UI.Palette[hub.Data.Workspaces.Count % UI.Palette.Length],
                    Items = items
                });
            }
            hub.Active = name;
            hub.Commit();
            Say(L.T("Сохранено: ", "Saved: ") + name);
        }

        void EditLook(string name)
        {
            var w = hub.Get(name);
            if (w == null) return;
            var dlg = new LookDialog(this, w);
            if (dlg.ShowDialog() != true) return;
            w.Icon = dlg.Icon ?? "";
            w.Color = dlg.ColorHex;
            hub.Commit();
        }

        void EditHotkey(string name)
        {
            var w = hub.Get(name);
            if (w == null) return;
            var dlg = new HotkeyDialog(this, w.Hotkey);
            if (dlg.ShowDialog() != true) return;
            if (!string.IsNullOrEmpty(dlg.Result) && hub.Data.Workspaces.Any(x => x.Name != name && x.Hotkey == dlg.Result))
            {
                MessageBox.Show(this, L.T("Это сочетание уже назначено другому пространству.",
                    "This hotkey is already used by another workspace."), "Workspaces");
                return;
            }
            w.Hotkey = dlg.Result;
            hub.Commit();
        }

        void EditLaunch(string name)
        {
            var w = hub.Get(name);
            if (w == null) return;
            var dlg = new TextListDialog(this, L.T("Файлы и ссылки", "Files and links"),
                L.T("По одному в строке: путь к файлу, папке или ссылка. Откроются при переключении на это пространство.",
                    "One per line: a file, folder or URL. They open when you switch to this workspace."),
                string.Join("\r\n", w.Launch));
            if (dlg.ShowDialog() != true) return;
            w.Launch = (dlg.Result ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            hub.Commit();
        }

        void UpdateLayout(string name)
        {
            var items = WindowManager.Capture();
            if (items.Count == 0) return;
            if (MessageBox.Show(this, L.T("Заменить раскладку «", "Replace the layout of “") + name
                    + L.T("» текущими окнами (", "” with current windows (") + items.Count + ")?",
                    "Workspaces", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            hub.Get(name).Items = items;
            hub.Commit();
            Say(L.T("Обновлено: ", "Updated: ") + name);
        }

        void RenameWs(string name)
        {
            string n = InputDialog.Ask(this, L.T("Переименовать", "Rename"), L.T("Новое название:", "New name:"), name);
            if (string.IsNullOrWhiteSpace(n)) return;
            n = n.Trim();
            if (n != name && hub.Get(n) != null) return;
            hub.Get(name).Name = n;
            if (hub.Active == name) hub.Active = n;
            hub.Commit();
        }

        void DeleteWs(string name)
        {
            if (MessageBox.Show(this, L.T("Удалить «", "Delete “") + name + L.T("»?", "”?"), "Workspaces",
                    MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            Store.Instance.CreateBackup("before-delete");
            hub.Data.Workspaces.RemoveAll(w => w.Name == name);
            if (hub.Active == name) hub.Active = null;
            hub.Commit();
        }

        void ExportData()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "JSON|*.json", FileName = "workspaces-backup.json" };
            if (dlg.ShowDialog(this) != true) return;
            try { Store.ExportTo(hub.Data, dlg.FileName); Say(L.T("Экспортировано: ", "Exported: ") + dlg.FileName); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Export"); }
        }

        void ImportData()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "JSON|*.json" };
            if (dlg.ShowDialog(this) != true) return;
            var imported = Store.ImportFrom(dlg.FileName);
            if (imported == null || imported.Workspaces == null)
            {
                MessageBox.Show(this, L.T("Не удалось прочитать файл.", "Could not read the file."), "Import");
                return;
            }
            if (MessageBox.Show(this, L.T("Заменить текущие пространства (", "Replace current workspaces (")
                    + hub.Data.Workspaces.Count + L.T(") на файл (", ") with the file (")
                    + imported.Workspaces.Count + ")?", "Import", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            int skipped;
            int n = hub.ReplaceWorkspaces(imported.Workspaces, out skipped);
            string msg = L.T("Импортировано пространств: ", "Workspaces imported: ") + n;
            if (skipped > 0)
                msg += L.T(". Не импортировано из-за лимита Free: ", ". Skipped because of the Free limit: ") + skipped
                       + L.T(" (файл не изменён).", " (the file is untouched).");
            Say(msg);
        }

        void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(Store.Dir);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Store.Dir + "\"") { UseShellExecute = true });
            }
            catch { }
        }
    }
}
