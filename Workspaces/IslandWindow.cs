using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Workspaces
{
    // Virtual Notch: «остров», приклеенный к верхнему краю экрана. Скрыт, пока мышь не подведена
    // к зоне срабатывания; выезжает с анимацией и прячется при уходе мыши. Не забирает фокус,
    // не срабатывает поверх полноэкранных окон, геометрия экрана кэшируется (низкая нагрузка).
    public class IslandWindow : Window
    {
        enum S { Hidden, Expanding, Expanded, Collapsing }

        const double CollapsedW = 120;
        const double Pad = 30; // запас окна по бокам и снизу под тень и перелёт анимации

        readonly Hub hub;
        readonly Border pill;
        readonly StackPanel chips;
        readonly DispatcherTimer timer;
        readonly EventHandler displayHandler;
        S state = S.Hidden;
        double pillW = 320;
        DateTime? leaveSince;
        DateTime holdUntil = DateTime.MinValue;
        Color fgColor = Colors.White;
        SolidColorBrush fgBrush = new SolidColorBrush(Colors.White);

        // кэш геометрии выбранного экрана (в пикселях)
        int gL, gT, gW = 1920, gH = 1080;
        double gK = 1.0;
        DateTime gUntil = DateTime.MinValue;

        double PillH { get { return Eff.IslandHeight(hub.Data); } }

        public IslandWindow(Hub hub)
        {
            this.hub = hub;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;

            chips = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2),
                Opacity = 0
            };
            pill = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Width = CollapsedW, Height = 0, ClipToBounds = true,
                Child = chips,
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.5 }
            };
            var g = new Grid();
            g.Children.Add(pill);
            Content = g;
            ApplyLook();

            SourceInitialized += (s, e) =>
            {
                // не забирает фокус и не попадает в Alt+Tab
                IntPtr h = new WindowInteropHelper(this).Handle;
                Native.SetWindowLong(h, -20, Native.GetWindowLong(h, -20) | 0x08000000 | 0x80);
            };
            DpiChanged += (s, e) => { InvalidateGeo(); if (IsVisible) PlaceWindow(); };

            displayHandler = (s, e) => Dispatcher.BeginInvoke(new Action(() =>
            {
                InvalidateGeo();
                if (IsVisible) PlaceWindow();
            }));
            SystemEvents.DisplaySettingsChanged += displayHandler;
            Closed += (s, e) => SystemEvents.DisplaySettingsChanged -= displayHandler;

            hub.Changed += OnChanged;
            hub.AppearanceChanged += OnChanged;
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += Tick;
            timer.Start();
        }

        void SetRate(int ms)
        {
            if (timer != null && (int)timer.Interval.TotalMilliseconds != ms) timer.Interval = TimeSpan.FromMilliseconds(ms);
        }

        // ---------- вид из настроек
        void ApplyLook()
        {
            string bg = Eff.IslandBg(hub.Data);
            Color c = Theme.Parse(bg);
            int op = Math.Max(30, Math.Min(100, Eff.IslandOpacity(hub.Data)));
            pill.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * op / 100.0), c.R, c.G, c.B));
            double r = PillH / 2;
            pill.CornerRadius = new CornerRadius(0, 0, r, r); // плоский верх: «прикреплён» к экрану
            fgColor = Theme.Lum(bg) > 0.6 ? Color.FromRgb(0x11, 0x11, 0x14) : Colors.White;
            fgBrush = new SolidColorBrush(fgColor);
        }

        Brush Ov(int alpha)
        {
            return new SolidColorBrush(Color.FromArgb((byte)alpha, fgColor.R, fgColor.G, fgColor.B));
        }

        // ---------- экран, DPI и позиция
        System.Windows.Forms.Screen GetScreen()
        {
            var all = UI.Screens();
            int i = Eff.Monitor(hub.Data);
            if (i < 0 || i >= all.Length) i = 0;
            return all[i];
        }

        static double Scale(int left, int top, int width)
        {
            try
            {
                var pt = new POINT { X = left + width / 2, Y = top + 10 };
                IntPtr mon = Native.MonitorFromPoint(pt, 2);
                uint dx, dy;
                if (Native.GetDpiForMonitor(mon, 0, out dx, out dy) == 0 && dx > 0) return dx / 96.0;
            }
            catch (Exception ex) { Logger.Warn("Не удалось определить DPI монитора", ex); }
            return 1.0;
        }

        void RefreshGeo()
        {
            var b = GetScreen().Bounds;
            gL = b.Left; gT = b.Top; gW = b.Width; gH = b.Height;
            gK = Scale(gL, gT, gW);
            gUntil = DateTime.UtcNow.AddSeconds(2);
        }

        void EnsureGeo() { if (DateTime.UtcNow > gUntil) RefreshGeo(); }
        void InvalidateGeo() { gUntil = DateTime.MinValue; }

        // Горизонтальные границы острова на экране, в пикселях.
        void PillExtents(out double pl, out double pr)
        {
            double pillPx = pillW * gK;
            double margin = 14 * gK;
            int pos = Eff.IslandPos(hub.Data);
            if (pos == 1) { pl = gL + margin; pr = pl + pillPx; }
            else if (pos == 2) { pr = gL + gW - margin; pl = pr - pillPx; }
            else { pl = gL + (gW - pillPx) / 2; pr = pl + pillPx; }
        }

        void PlaceWindow()
        {
            RefreshGeo();
            Width = pillW + Pad * 2;
            Height = PillH + Pad;
            double pl, pr;
            PillExtents(out pl, out pr);
            IntPtr h = new WindowInteropHelper(this).EnsureHandle();
            Native.SetWindowPos(h, IntPtr.Zero, (int)Math.Round(pl - Pad * gK), gT, 0, 0, 0x0001 | 0x0004 | 0x0010);
        }

        // Полноэкранное окно без заголовка (игра, видео): остров не мешаем.
        bool ForegroundFullscreen()
        {
            try
            {
                IntPtr h = Native.GetForegroundWindow();
                if (h == IntPtr.Zero) return false;
                var sb = new StringBuilder(64);
                Native.GetClassName(h, sb, 64);
                string cls = sb.ToString();
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd") return false;
                if ((Native.GetWindowLong(h, -16) & 0xC00000) != 0) return false; // есть заголовок
                RECT r;
                if (!Native.GetWindowRect(h, out r)) return false;
                return r.Left <= gL && r.Top <= gT && r.Right >= gL + gW && r.Bottom >= gT + gH;
            }
            catch (Exception ex)
            {
                Logger.Warn("Не удалось проверить полноэкранное окно", ex);
                return false;
            }
        }

        // ---------- слежение за мышью
        void Tick(object sender, EventArgs e)
        {
            if (!hub.Data.Island)
            {
                SetRate(500);
                if (state != S.Hidden) HideNow();
                return;
            }
            SetRate(state == S.Hidden ? 50 : 30);
            POINT p;
            if (!Native.GetCursorPos(out p)) return;
            EnsureGeo();
            double ry = p.Y - gT;
            bool onScreen = p.X >= gL && p.X < gL + gW && ry >= 0 && ry < gH;

            double tw = hub.Data.TriggerW * gK, th = hub.Data.TriggerH * gK;
            double tl, tr;
            int pos = Eff.IslandPos(hub.Data);
            if (pos == 1) { tl = gL; tr = gL + tw; }
            else if (pos == 2) { tr = gL + gW; tl = tr - tw; }
            else { double c = gL + gW / 2.0; tl = c - tw / 2; tr = c + tw / 2; }

            double pl, pr;
            PillExtents(out pl, out pr);

            bool nearTrigger = onScreen && ry <= th && p.X >= tl && p.X <= tr;
            bool keep = (onScreen && ry <= (PillH + 22) * gK && p.X >= pl - 28 * gK && p.X <= pr + 28 * gK)
                        || DateTime.UtcNow < holdUntil;

            switch (state)
            {
                case S.Hidden:
                    if (nearTrigger && (Native.GetAsyncKeyState(0x01) & 0x8000) == 0 && !ForegroundFullscreen()) Expand();
                    break;
                case S.Collapsing:
                    if (nearTrigger || keep) Expand();
                    break;
                default:
                    if (keep) leaveSince = null;
                    else if (leaveSince == null) leaveSince = DateTime.UtcNow;
                    else if ((DateTime.UtcNow - leaveSince.Value).TotalMilliseconds > hub.Data.HideDelay) Collapse();
                    break;
            }
        }

        public void Preview()
        {
            if (!hub.Data.Island)
            {
                hub.SetStatus(L.T("Сначала включите остров", "Enable the island first"));
                return;
            }
            holdUntil = DateTime.UtcNow.AddSeconds(3);
            if (state == S.Hidden || state == S.Collapsing) Expand();
        }

        // ---------- анимации
        TimeSpan Dur(int ms)
        {
            if (Eff.AnimType(hub.Data) == 2) return TimeSpan.FromMilliseconds(1);
            return TimeSpan.FromMilliseconds(Math.Max(1, ms * 100.0 / Math.Max(25, Eff.AnimSpeed(hub.Data))));
        }

        IEasingFunction ShowEase()
        {
            int t = Eff.AnimType(hub.Data);
            if (t == 0) return new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
            if (t == 1) return new CubicEase { EasingMode = EasingMode.EaseOut };
            return null;
        }

        IEasingFunction HideEase()
        {
            return Eff.AnimType(hub.Data) == 2 ? null : new CubicEase { EasingMode = EasingMode.EaseIn };
        }

        void Expand()
        {
            state = S.Expanding;
            leaveSince = null;
            SetRate(30);
            Rebuild();
            PlaceWindow();
            if (!IsVisible)
            {
                pill.BeginAnimation(WidthProperty, null);
                pill.BeginAnimation(HeightProperty, null);
                chips.BeginAnimation(OpacityProperty, null);
                pill.Width = CollapsedW;
                pill.Height = 0;
                chips.Opacity = 0;
                Show();
                PlaceWindow();
            }
            Topmost = false; Topmost = true;

            var d = Dur(460);
            var ease = ShowEase();
            var ha = new DoubleAnimation(PillH, d) { EasingFunction = ease };
            ha.Completed += (s, e) => { if (state == S.Expanding) state = S.Expanded; };
            pill.BeginAnimation(WidthProperty, new DoubleAnimation(pillW, d) { EasingFunction = ease });
            pill.BeginAnimation(HeightProperty, ha);
            chips.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, Dur(250)) { BeginTime = Eff.AnimType(hub.Data) == 2 ? TimeSpan.Zero : Dur(130) });
        }

        void Collapse()
        {
            state = S.Collapsing;
            var d = Dur(260);
            var ease = HideEase();
            var ha = new DoubleAnimation(0, d) { EasingFunction = ease };
            ha.Completed += (s, e) => { if (state == S.Collapsing) HideNow(); };
            pill.BeginAnimation(HeightProperty, ha);
            pill.BeginAnimation(WidthProperty, new DoubleAnimation(CollapsedW, d) { EasingFunction = ease });
            chips.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Dur(120)));
        }

        void HideNow()
        {
            state = S.Hidden;
            pill.BeginAnimation(WidthProperty, null);
            pill.BeginAnimation(HeightProperty, null);
            chips.BeginAnimation(OpacityProperty, null);
            pill.Width = CollapsedW;
            pill.Height = 0;
            chips.Opacity = 0;
            if (IsVisible) Hide();
            SetRate(50);
        }

        void OnChanged()
        {
            ApplyLook();
            InvalidateGeo();
            if (state == S.Expanded || state == S.Expanding)
            {
                Rebuild();
                PlaceWindow();
                pill.BeginAnimation(WidthProperty, new DoubleAnimation(pillW, Dur(250))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                pill.BeginAnimation(HeightProperty, new DoubleAnimation(PillH, Dur(250))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        }

        // ---------- содержимое
        void Rebuild()
        {
            ApplyLook();
            RefreshGeo();
            chips.Children.Clear();
            var list = hub.Data.Workspaces;
            if (list.Count == 0)
            {
                chips.Children.Add(new TextBlock
                {
                    Text = L.T("Пока нет пространств", "No workspaces yet"), Foreground = Ov(0x99),
                    FontSize = Eff.IslandFont(hub.Data), VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 10, 0)
                });
            }
            foreach (var w in list) chips.Children.Add(MakeChip(w));
            chips.Children.Add(IconChip("\uE713", () => hub.ShowMain())); // шестерёнка
            chips.Children.Add(IconChip("\uE711", () => hub.Quit()));     // закрыть
            chips.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            pillW = Math.Min(chips.DesiredSize.Width + 44, gW / gK - 80);
        }

        double ChipH { get { return Math.Max(26, PillH - 24); } }

        Border MakeChip(Workspace w)
        {
            double fs = Eff.IslandFont(hub.Data);
            bool names = Eff.IslandNames(hub.Data);
            double ch = ChipH;
            int baseA = w.Name == hub.Active ? 0x4D : 0x1F;

            var sp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
            };
            if (!string.IsNullOrEmpty(w.Icon))
                sp.Children.Add(new TextBlock
                {
                    Text = w.Icon, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = fs + 3,
                    Foreground = fgBrush, VerticalAlignment = VerticalAlignment.Center
                });
            else
                sp.Children.Add(new Ellipse
                {
                    Width = Math.Max(6, fs - 4), Height = Math.Max(6, fs - 4), Fill = UI.B(w.Color),
                    VerticalAlignment = VerticalAlignment.Center
                });
            if (names)
                sp.Children.Add(new TextBlock
                {
                    Text = w.Name.Length > 16 ? w.Name.Substring(0, 16) + "…" : w.Name,
                    Foreground = fgBrush, FontSize = fs, FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
                });

            var b = new Border
            {
                Height = ch, CornerRadius = new CornerRadius(ch / 2), Background = Ov(baseA),
                Margin = new Thickness(3, 0, 3, 0), Child = sp, Cursor = Cursors.Hand, ToolTip = w.Name
            };
            if (names) b.Padding = new Thickness(fs, 0, fs + 1, 0);
            else b.Width = ch;
            b.MouseEnter += (s, e) => b.Background = Ov(0x4D);
            b.MouseLeave += (s, e) => b.Background = Ov(baseA);
            string name = w.Name;
            b.MouseLeftButtonUp += (s, e) => hub.Activate(name);
            return b;
        }

        Border IconChip(string glyph, Action click)
        {
            double ch = ChipH;
            var b = new Border
            {
                Width = ch, Height = ch, CornerRadius = new CornerRadius(ch / 2),
                Background = Ov(0x1F), Margin = new Thickness(3, 0, 3, 0), Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13,
                    Foreground = fgBrush, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            b.MouseEnter += (s, e) => b.Background = Ov(0x4D);
            b.MouseLeave += (s, e) => b.Background = Ov(0x1F);
            b.MouseLeftButtonUp += (s, e) => click();
            return b;
        }
    }
}
