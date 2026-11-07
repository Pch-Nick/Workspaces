using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Workspaces
{
    public static class Ctl
    {
        public static Border Btn(string text, Action click, string bg, string fg, string hover)
        {
            var b = new Border
            {
                Background = UI.B(bg), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18, 10, 18, 10), Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = text, Foreground = UI.B(fg), FontWeight = FontWeights.SemiBold, FontSize = 14
                }
            };
            b.MouseEnter += (s, e) => b.Background = UI.B(hover);
            b.MouseLeave += (s, e) => b.Background = UI.B(bg);
            b.MouseLeftButtonUp += (s, e) => click();
            return b;
        }

        public static Border Primary(string text, Action click)
        {
            return Btn(text, click, Theme.Accent, Theme.OnAccent, Theme.AccentHover);
        }

        public static Border Secondary(string text, Action click)
        {
            return Btn(text, click, Theme.BtnSec, Theme.Fg, Theme.BtnSecHover);
        }

        public static TextBox Tb(string text, double width)
        {
            return new TextBox
            {
                Text = text, Width = width, FontSize = 13, Padding = new Thickness(7, 5, 7, 5),
                Background = UI.B(Theme.Field), Foreground = UI.B(Theme.Fg), CaretBrush = UI.B(Theme.Fg),
                BorderBrush = UI.B(Theme.FieldBorder), SelectionBrush = UI.B(Theme.Accent),
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        public static FrameworkElement Toggle(bool on, Action<bool> changed)
        {
            var thumb = new Ellipse
            {
                Width = 18, Height = 18, Fill = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
            };
            var tt = new TranslateTransform(on ? 25 : 3, 0);
            thumb.RenderTransform = tt;
            var track = new Border
            {
                Width = 46, Height = 24, CornerRadius = new CornerRadius(12),
                Background = UI.B(on ? Theme.Accent : Theme.ToggleOff), Child = thumb, Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            track.MouseLeftButtonUp += (s, e) =>
            {
                on = !on;
                tt.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(on ? 25 : 3, TimeSpan.FromMilliseconds(170))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                track.Background = UI.B(on ? Theme.Accent : Theme.ToggleOff);
                changed(on);
            };
            return track;
        }

        // Переключатель из нескольких вариантов.
        public static FrameworkElement Segment(string[] opts, int selected, Action<int> changed)
        {
            var host = new Border
            {
                Background = UI.B(Theme.BtnSec), CornerRadius = new CornerRadius(10), Padding = new Thickness(3)
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            host.Child = sp;
            var boxes = new List<Border>();
            var texts = new List<TextBlock>();
            int cur = selected;
            Action paint = () =>
            {
                for (int i = 0; i < boxes.Count; i++)
                {
                    bool sel = i == cur;
                    boxes[i].Background = sel ? UI.B(Theme.Accent) : Brushes.Transparent;
                    texts[i].Foreground = UI.B(sel ? Theme.OnAccent : Theme.Fg);
                }
            };
            for (int i = 0; i < opts.Length; i++)
            {
                int idx = i;
                var tb = new TextBlock
                {
                    Text = opts[i], FontSize = 12, FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var b = new Border
                {
                    CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 6, 12, 6),
                    Cursor = Cursors.Hand, Child = tb, Background = Brushes.Transparent
                };
                b.MouseLeftButtonUp += (s, e) => { cur = idx; paint(); changed(idx); };
                boxes.Add(b);
                texts.Add(tb);
                sp.Children.Add(b);
            }
            paint();
            return host;
        }

        // Карточка настройки: заголовок + описание слева, элемент управления справа.
        public static Border Row(string title, string desc, FrameworkElement control, int density = 1)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(Title(title));
            if (!string.IsNullOrEmpty(desc)) left.Children.Add(Desc(desc));
            g.Children.Add(left);
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return Card(g, density);
        }

        // Блок на всю ширину: заголовок и описание сверху, содержимое снизу.
        public static Border Block(string title, string desc, FrameworkElement body, int density = 1)
        {
            var sp = new StackPanel();
            sp.Children.Add(Title(title));
            if (!string.IsNullOrEmpty(desc)) sp.Children.Add(Desc(desc));
            body.Margin = new Thickness(0, 12, 0, 0);
            sp.Children.Add(body);
            return Card(sp, density);
        }

        static TextBlock Title(string t)
        {
            return new TextBlock
            {
                Text = t, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = UI.B(Theme.Fg)
            };
        }

        static TextBlock Desc(string t)
        {
            return new TextBlock
            {
                Text = t, FontSize = 12, Foreground = UI.B(Theme.Muted),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0)
            };
        }

        static Border Card(UIElement child, int density)
        {
            int pad = density == 0 ? 9 : density == 2 ? 19 : 14;
            int gap = density == 0 ? 5 : density == 2 ? 12 : 8;
            return new Border
            {
                Background = UI.B(Theme.Panel), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18, pad, 18, pad), Margin = new Thickness(0, 0, 0, gap), Child = child
            };
        }

        public static Border SliderRow(string title, string desc, double min, double max, double value,
            double step, string unit, Action<int> changed, int density = 1)
        {
            var label = new TextBlock
            {
                Text = ((int)value) + unit, Foreground = UI.B(Theme.Muted), FontSize = 12, Width = 62,
                VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right
            };
            var sl = new Slider
            {
                Minimum = min, Maximum = max, Value = value, Width = 170,
                TickFrequency = step, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center
            };
            sl.ValueChanged += (s, e) =>
            {
                label.Text = ((int)sl.Value) + unit;
                changed((int)sl.Value);
            };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(sl);
            sp.Children.Add(label);
            return Row(title, desc, sp, density);
        }

        // Палитра цветов + ввод HEX.
        public static Border ColorRow(string title, string desc, string current, string[] palette,
            Action<string> apply, int density = 1)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var c in palette)
            {
                string col = c;
                bool sel = string.Equals(c, current, StringComparison.OrdinalIgnoreCase);
                var sw = new Border
                {
                    Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = UI.B(col),
                    Margin = new Thickness(0, 0, 6, 0), BorderThickness = new Thickness(2), Cursor = Cursors.Hand,
                    BorderBrush = sel ? UI.B(Theme.Fg) : UI.B("#40808080")
                };
                sw.MouseLeftButtonUp += (s, e) => apply(col);
                sp.Children.Add(sw);
            }
            var tb = Tb(current, 88);
            tb.MaxLength = 7;
            tb.Margin = new Thickness(6, 0, 0, 0);
            bool applied = false;
            Action commit = () =>
            {
                if (applied) return;
                string t = tb.Text.Trim();
                if (!t.StartsWith("#")) t = "#" + t;
                if (Theme.IsHex(t) && !string.Equals(t, current, StringComparison.OrdinalIgnoreCase))
                {
                    applied = true;
                    apply(t.ToUpper());
                }
            };
            tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) commit(); };
            tb.LostKeyboardFocus += (s, e) => commit();
            sp.Children.Add(tb);
            return Row(title, desc, sp, density);
        }
    }
}
