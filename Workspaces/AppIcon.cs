using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Workspaces
{
    // Иконка приложения, вшитая в exe (Assets\app.ico), для окон WPF.
    public static class AppIcon
    {
        static ImageSource cached;

        public static ImageSource Get()
        {
            if (cached != null) return cached;
            try
            {
                using (var ic = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath))
                {
                    if (ic == null) return null;
                    var src = Imaging.CreateBitmapSourceFromHIcon(ic.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    cached = src;
                }
            }
            catch (Exception ex) { Logger.Warn("Не удалось загрузить иконку приложения", ex); }
            return cached;
        }
    }
}
