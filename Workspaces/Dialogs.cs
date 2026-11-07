using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Workspaces
{
    public abstract class DialogBase : Window
    {
        protected DialogBase(Window owner, string title, double width)
        {
            if (owner != null && owner.IsVisible) Owner = owner;
            Title = title;
            Icon = AppIcon.Get();
            Width = width;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = Owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            Background = UI.B(Theme.Bg);
            FontFamily = new FontFamily("Segoe UI");
            SourceInitialized += (s, e) =>
            {
                int on = Theme.Dark ? 1 : 0;
                Native.DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, 4);
            };
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { e.Handled = true; DialogResult = false; }
            };
        }

        protected StackPanel ButtonRow(Action ok, params FrameworkElement[] extra)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0)
            };
            foreach (var x in extra) { x.Margin = new Thickness(0, 0, 8, 0); row.Children.Add(x); }
            var okB = Ctl.Primary("OK", ok);
            var cancel = Ctl.Secondary(L.T("Отмена", "Cancel"), () => { DialogResult = false; });
            cancel.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(okB);
            row.Children.Add(cancel);
            return row;
        }

        protected TextBlock Label(string text)
        {
            return new TextBlock
            {
                Text = text, Foreground = UI.B(Theme.Muted), FontSize = 13, Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
        }
    }

    public class InputDialog : DialogBase
    {
        readonly TextBox box;
        public string Result;

        public InputDialog(Window owner, string title, string prompt, string initial)
            : base(owner, title, 400)
        {
            var sp = new StackPanel { Margin = new Thickness(22) };
            sp.Children.Add(Label(prompt));
            box = Ctl.Tb(initial, 356);
            box.FontSize = 15;
            sp.Children.Add(box);
            Action ok = () => { Result = box.Text; DialogResult = true; };
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) ok(); };
            sp.Children.Add(ButtonRow(ok));
            Content = sp;
            Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
        }

        public static string Ask(Window owner, string title, string prompt, string initial = "")
        {
            var d = new InputDialog(owner, title, prompt, initial);
            return d.ShowDialog() == true ? d.Result : null;
        }
    }

    // Многострочный список: файлы и ссылки, по одному в строке.
    public class TextListDialog : DialogBase
    {
        readonly TextBox box;
        public string Result;

        public TextListDialog(Window owner, string title, string prompt, string initial)
            : base(owner, title, 520)
        {
            var sp = new StackPanel { Margin = new Thickness(22) };
            sp.Children.Add(Label(prompt));
            box = Ctl.Tb(initial, 476);
            box.AcceptsReturn = true;
            box.TextWrapping = TextWrapping.NoWrap;
            box.Height = 160;
            box.VerticalContentAlignment = VerticalAlignment.Top;
            box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            box.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            sp.Children.Add(box);
            var addFile = Ctl.Secondary(L.T("Добавить файл…", "Add file…"), () =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog();
                if (dlg.ShowDialog(this) == true)
                    box.Text = (box.Text.Length > 0 && !box.Text.EndsWith("\n") ? box.Text + "\r\n" : box.Text) + dlg.FileName;
            });
            sp.Children.Add(ButtonRow(() => { Result = box.Text; DialogResult = true; }, addFile));
            Content = sp;
            Loaded += (s, e) => box.Focus();
        }
    }

    // Захват сочетания клавиш.
    public class HotkeyDialog : DialogBase
    {
        readonly TextBlock shown;
        string captured;
        public string Result = "";

        public HotkeyDialog(Window owner, string current)
            : base(owner, L.T("Горячая клавиша", "Hotkey"), 400)
        {
            captured = current ?? "";
            var sp = new StackPanel { Margin = new Thickness(22) };
            sp.Children.Add(Label(L.T("Нажмите сочетание клавиш с Ctrl, Alt, Shift или Win.",
                                      "Press a key combination with Ctrl, Alt, Shift or Win.")));
            shown = new TextBlock
            {
                Text = captured.Length > 0 ? captured : "—", FontSize = 26, FontWeight = FontWeights.Bold,
                Foreground = UI.B(Theme.Accent), HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 6)
            };
            sp.Children.Add(shown);
            var clear = Ctl.Secondary(L.T("Очистить", "Clear"), () => { Result = ""; DialogResult = true; });
            sp.Children.Add(ButtonRow(() => { Result = captured; DialogResult = true; }, clear));
            Content = sp;

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) return;
                Key k = e.Key == Key.System ? e.SystemKey : e.Key;
                if (k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt ||
                    k == Key.LeftShift || k == Key.RightShift || k == Key.LWin || k == Key.RWin ||
                    k == Key.None || k == Key.DeadCharProcessed)
                {
                    e.Handled = true;
                    return;
                }
                e.Handled = true;
                if (Keyboard.Modifiers == ModifierKeys.None)
                {
                    shown.Text = L.T("Добавьте Ctrl, Alt или Shift", "Add Ctrl, Alt or Shift");
                    return;
                }
                captured = HotkeyManager.Format(Keyboard.Modifiers, k);
                shown.Text = captured;
            };
        }
    }

    // Значок (эмодзи) и цвет пространства.
    public class LookDialog : DialogBase
    {
        static readonly string[] Emojis =
        {
            "💼", "💻", "🎮", "🎧", "📚", "🎓", "🎬", "🎨", "🛠️", "📈", "✉️", "🧪",
            "🏠", "🔥", "⭐", "🚀", "🎵", "📷", "🧠", "☕", "📝", "🌐", "🛒", "⚙️"
        };

        public string Icon;
        public string ColorHex;
        readonly List<Border> cells = new List<Border>();
        readonly List<string> cellIcons = new List<string>();
        readonly List<Border> swatches = new List<Border>();
        readonly List<string> swatchColors = new List<string>();
        TextBox iconBox;
        TextBox hexBox;

        public LookDialog(Window owner, Workspace ws)
            : base(owner, L.T("Значок и цвет", "Icon and color"), 470)
        {
            Icon = ws.Icon ?? "";
            ColorHex = ws.Color;
            var sp = new StackPanel { Margin = new Thickness(22) };

            sp.Children.Add(Label(L.T("Значок", "Icon")));
            var grid = new UniformGrid { Columns = 8 };
            AddCell(grid, "", "—");
            foreach (var e in Emojis) AddCell(grid, e, e);
            sp.Children.Add(grid);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            row.Children.Add(new TextBlock
            {
                Text = L.T("Свой значок:", "Custom:"), Foreground = UI.B(Theme.Muted), FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
            });
            iconBox = Ctl.Tb(Icon, 70);
            iconBox.MaxLength = 6;
            iconBox.FontFamily = new FontFamily("Segoe UI Emoji");
            iconBox.TextChanged += (s, e) => { Icon = iconBox.Text.Trim(); Paint(); };
            row.Children.Add(iconBox);
            sp.Children.Add(row);

            var colorLabel = Label(L.T("Цвет", "Color"));
            colorLabel.Margin = new Thickness(0, 16, 0, 8);
            sp.Children.Add(colorLabel);
            var sw = new WrapPanel();
            foreach (var c in UI.Palette)
            {
                string col = c;
                var b = new Border
                {
                    Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = UI.B(col),
                    Margin = new Thickness(0, 0, 8, 6), BorderThickness = new Thickness(2), Cursor = Cursors.Hand
                };
                b.MouseLeftButtonUp += (s, e) => { ColorHex = col; hexBox.Text = col; Paint(); };
                swatches.Add(b);
                swatchColors.Add(col);
                sw.Children.Add(b);
            }
            sp.Children.Add(sw);
            hexBox = Ctl.Tb(ColorHex, 90);
            hexBox.MaxLength = 7;
            hexBox.HorizontalAlignment = HorizontalAlignment.Left;
            hexBox.TextChanged += (s, e) =>
            {
                string t = hexBox.Text.Trim();
                if (!t.StartsWith("#")) t = "#" + t;
                if (Theme.IsHex(t)) { ColorHex = t.ToUpper(); Paint(); }
            };
            sp.Children.Add(hexBox);

            sp.Children.Add(ButtonRow(() => DialogResult = true));
            Content = sp;
            Paint();
        }

        void AddCell(UniformGrid grid, string icon, string shown)
        {
            var b = new Border
            {
                Height = 40, Margin = new Thickness(2), CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = shown, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 20,
                    Foreground = UI.B(Theme.Fg), HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            b.MouseLeftButtonUp += (s, e) => { Icon = icon; if (iconBox != null) iconBox.Text = icon; Paint(); };
            cells.Add(b);
            cellIcons.Add(icon);
            grid.Children.Add(b);
        }

        void Paint()
        {
            for (int i = 0; i < cells.Count; i++)
                cells[i].Background = UI.B(cellIcons[i] == Icon ? Theme.Mix(Theme.Accent, Theme.Bg, 0.55) : Theme.BtnSec);
            for (int i = 0; i < swatches.Count; i++)
                swatches[i].BorderBrush = string.Equals(swatchColors[i], ColorHex, StringComparison.OrdinalIgnoreCase)
                    ? UI.B(Theme.Fg) : UI.B("#40808080");
        }
    }
}

namespace Workspaces
{
    // Добавление и удаление приложений пространства.
    public class AppsDialog : DialogBase
    {
        readonly List<WinItem> items;
        readonly ListBox list;
        public List<WinItem> Result;

        public AppsDialog(Window owner, Workspace ws)
            : base(owner, L.T("Приложения: ", "Apps: ") + ws.Name, 560)
        {
            items = ws.Items.ToList();
            var sp = new StackPanel { Margin = new Thickness(22) };
            sp.Children.Add(Label(L.T("Эти программы запускаются и расставляются при переключении на пространство. "
                                      + "Раскладку окон можно обновить из меню карточки.",
                                      "These programs are launched and arranged when you switch to the workspace. "
                                      + "You can update the window layout from the card menu.")));
            list = new ListBox
            {
                Height = 220, Background = UI.B(Theme.Field), Foreground = UI.B(Theme.Fg),
                BorderBrush = UI.B(Theme.FieldBorder), FontSize = 13
            };
            sp.Children.Add(list);
            Refill();

            var add = Ctl.Secondary(L.T("Добавить программу…", "Add program…"), () =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Programs|*.exe|All files|*.*" };
                if (dlg.ShowDialog(this) != true) return;
                items.Add(new WinItem { Exe = dlg.FileName, Title = "", ShowCmd = 1 });
                Refill();
            });
            var remove = Ctl.Secondary(L.T("Удалить выбранное", "Remove selected"), () =>
            {
                int i = list.SelectedIndex;
                if (i < 0 || i >= items.Count) return;
                items.RemoveAt(i);
                Refill();
            });
            sp.Children.Add(ButtonRow(() => { Result = items; DialogResult = true; }, add, remove));
            Content = sp;
        }

        void Refill()
        {
            list.Items.Clear();
            foreach (var it in items)
            {
                string n = System.IO.Path.GetFileNameWithoutExtension(it.Exe);
                string extra = string.IsNullOrEmpty(it.Title) ? "" : "  —  " + it.Title;
                list.Items.Add(n + extra);
            }
        }
    }

    // Список резервных копий для восстановления.
    public class BackupsDialog : DialogBase
    {
        readonly List<BackupInfo> backups;
        readonly ListBox list;
        public string SelectedPath;

        public BackupsDialog(Window owner, List<BackupInfo> backups)
            : base(owner, L.T("Резервные копии", "Backups"), 520)
        {
            this.backups = backups;
            var sp = new StackPanel { Margin = new Thickness(22) };
            sp.Children.Add(Label(L.T("Выберите копию. Перед восстановлением текущие данные тоже сохраняются в копию.",
                                      "Pick a backup. Your current data is backed up before restoring.")));
            list = new ListBox
            {
                Height = 220, Background = UI.B(Theme.Field), Foreground = UI.B(Theme.Fg),
                BorderBrush = UI.B(Theme.FieldBorder), FontSize = 13
            };
            foreach (var b in backups)
                list.Items.Add(b.Time.ToString("yyyy-MM-dd HH:mm:ss") + "   " + (b.Size / 1024 + 1) + " KB   " + b.Reason);
            if (backups.Count > 0) list.SelectedIndex = 0;
            sp.Children.Add(list);
            sp.Children.Add(ButtonRow(() =>
            {
                int i = list.SelectedIndex;
                if (i < 0) return;
                SelectedPath = backups[i].Path;
                DialogResult = true;
            }));
            Content = sp;
        }
    }
}
