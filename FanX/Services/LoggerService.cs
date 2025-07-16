using System.Runtime.CompilerServices;
using log4net;
using log4net.Appender;
using log4net.Config;
using log4net.Layout;

namespace FanX.Services
{
    public class LoggerService
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(LoggerService));

        static LoggerService()
        {
            log4net.Util.LogLog.InternalDebugging = false;

            var fileAppender = new RollingFileAppender
            {
                LockingModel = new FileAppender.MinimalLock(),
                File = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs/"),
                AppendToFile = true,
                RollingStyle = RollingFileAppender.RollingMode.Date,
                DatePattern = "yyyy/MM/yyyy-MM-dd'.log'",
                StaticLogFileName = false,
                Layout = new PatternLayout("%date [%thread] (%property{file}:%property{line}) %-5level - %message%newline"),
                Threshold = log4net.Core.Level.All,
                ImmediateFlush = true
            };
            
            fileAppender.ActivateOptions();
            BasicConfigurator.Configure(fileAppender);
        }

        private static void SetContextProperties(string file, int line)
        {
            GlobalContext.Properties["file"] = Path.GetFileName(file);
            GlobalContext.Properties["line"] = line;
        }

        public static void Debug(string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            SetContextProperties(file, line);
            Log.Debug(message);
        }

        public static void Info(string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            SetContextProperties(file, line);
            Log.Info(message);
        }

        public static void Warn(string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            SetContextProperties(file, line);
            Log.Warn(message);
        }

        public static void Error(string message, Exception? ex = null, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            SetContextProperties(file, line);
            Log.Error(ex == null ? message : $"{message}\nException: {ex}");
        }

        public static void Fatal(string message, Exception? ex = null, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            SetContextProperties(file, line);
            Log.Fatal(ex == null ? message : $"{message}\nException: {ex}");
        }
    }
} 