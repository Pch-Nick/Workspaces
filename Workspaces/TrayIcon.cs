using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Workspaces
{
    public class TrayIcon : IDisposable
    {
        readonly Hub hub;
        readonly NotifyIcon icon;
        readonly ContextMenuStrip menu;

        public TrayIcon(Hub hub)
        {
            this.hub = hub;
            menu = new ContextMenuStrip();
            menu.Opening += (s, e) => Rebuild();
            icon = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "Workspaces",
                Visible = true,
                ContextMenuStrip = menu
            };
            icon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) hub.ShowMain(); };
            icon.BalloonTipClicked += (s, e) => hub.ShowMain();
        }

        void Rebuild()
        {
            menu.Items.Clear();
            var list = hub.Data.Workspaces;
            foreach (var w in list)
            {
                string name = w.Name;
                var it = new ToolStripMenuItem((name == hub.Active ? "●  " : "     ") + (string.IsNullOrEmpty(w.Icon) ? "" : w.Icon + " ") + name);
                it.Click += (s, e) => hub.Activate(name);
                menu.Items.Add(it);
            }
            if (list.Count > 0) menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("Открыть окно", "Open window"), null, (s, e) => hub.ShowMain());
            menu.Items.Add(L.T("Выход", "Exit"), null, (s, e) => hub.Quit());
        }

        public void Notify(string title, string text)
        {
            try { icon.ShowBalloonTip(6000, title, text, ToolTipIcon.Info); }
            catch (Exception ex) { Logger.Warn("Не удалось показать уведомление", ex); }
        }

        static Icon LoadIcon()
        {
            try
            {
                var ic = Icon.ExtractAssociatedIcon(Environment.ProcessPath);
                if (ic != null) return ic;
            }
            catch (Exception ex) { Logger.Warn("Не удалось взять иконку из exe", ex); }
            return MakeIcon();
        }

        static Icon MakeIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var path = RoundRect(1, 1, 30, 30, 8))
                using (var br = new SolidBrush(Color.FromArgb(0x6C, 0x8C, 0xFF)))
                    g.FillPath(br, path);
                using (var dark = new SolidBrush(Color.FromArgb(0x0B, 0x0C, 0x10)))
                {
                    foreach (var r in new[] { new Rectangle(7, 7, 8, 8), new Rectangle(17, 7, 8, 8),
                                              new Rectangle(7, 17, 8, 8), new Rectangle(17, 17, 8, 8) })
                        g.FillRectangle(dark, r);
                }
                IntPtr h = bmp.GetHicon();
                try { using (var tmp = Icon.FromHandle(h)) return (Icon)tmp.Clone(); }
                finally { Native.DestroyIcon(h); }
            }
        }

        static GraphicsPath RoundRect(int x, int y, int w, int h, int r)
        {
            var p = new GraphicsPath();
            int d = r * 2;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
            menu.Dispose();
        }
    }
}
