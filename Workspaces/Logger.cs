using System;
using System.IO;
using System.Text;

namespace Workspaces
{
    // Простой потокобезопасный файловый лог. Сам логгер не должен ронять приложение.
    public static class Logger
    {
        static readonly object Gate = new object();
        static string dir;

        public static string LogDir
        {
            get
            {
                if (dir == null)
                    dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Workspaces", "logs");
                return dir;
            }
        }

        public static void Init(string customDir = null)
        {
            if (customDir != null) dir = customDir;
            try
            {
                Directory.CreateDirectory(LogDir);
                foreach (var f in Directory.GetFiles(LogDir, "app-*.log"))
                    if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-14)) File.Delete(f);
            }
            catch (Exception) { /* журнал недоступен: работаем без него */ }
        }

        public static void Info(string msg) { Write("INFO", msg, null); }
        public static void Warn(string msg, Exception ex = null) { Write("WARN", msg, ex); }
        public static void Error(string msg, Exception ex = null) { Write("ERROR", msg, ex); }

        static void Write(string level, string msg, Exception ex)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(LogDir);
                    string path = Path.Combine(LogDir, "app-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                    var sb = new StringBuilder();
                    sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(" [").Append(level).Append("] ").Append(msg);
                    if (ex != null) sb.AppendLine().Append(ex);
                    sb.AppendLine();
                    File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
                }
            }
            catch (Exception) { /* логгер не имеет права бросать исключения */ }
        }
    }
}
