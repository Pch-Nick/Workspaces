using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Workspaces
{
    public static class Program
    {
        static Hub hub;

        [STAThread]
        public static void Main(string[] args)
        {
            Logger.Init();
            Logger.Info("Запуск Workspaces " + UI.Version);

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Logger.Error("Необработанное исключение (завершение: " + e.IsTerminating + ")", e.ExceptionObject as Exception);
                TrySaveOnCrash();
            };
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Logger.Error("Необработанное исключение задачи", e.Exception);
                e.SetObserved();
            };

            bool created;
            using (var mutex = new Mutex(true, "Workspaces.SingleInstance.Mutex", out created))
            {
                if (!created)
                {
                    Logger.Info("Уже запущена другая копия, выход");
                    return;
                }
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += (s, e) =>
                {
                    Logger.Error("Исключение интерфейса", e.Exception);
                    TrySaveOnCrash();
                    e.Handled = true; // приложение продолжает работать, данные уже сохранены
                    try
                    {
                        if (hub != null) hub.SetStatus(L.T("Произошла ошибка. Подробности в журнале.", "An error occurred. See the log for details."));
                    }
                    catch (Exception ex) { Logger.Warn("Не удалось показать сообщение об ошибке", ex); }
                };
                app.Exit += (s, e) => { TrySaveOnCrash(); Logger.Info("Выход"); };

                hub = new Hub();
                hub.Main = new MainWindow(hub);
                hub.Island = new IslandWindow(hub);
                hub.Hotkeys = new HotkeyManager(hub);
                hub.Tray = new TrayIcon(hub);

                bool minimized = args.Contains("--minimized"); // автозапуск: только трей и остров
                if (!minimized) hub.Main.Show();
                hub.StartBackground();
                if (!minimized)
                    app.Dispatcher.BeginInvoke(new Action(() => hub.Main.ShowStartupMessages()),
                        System.Windows.Threading.DispatcherPriority.ContextIdle);
                app.Run();
            }
        }

        static void TrySaveOnCrash()
        {
            try { if (hub != null) hub.FlushSave(); }
            catch (Exception ex) { Logger.Error("Не удалось сохранить данные при сбое", ex); }
        }
    }
}
