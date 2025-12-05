using System;
using System.IO;
using System.Windows;

namespace IEVRModManager
{
    public partial class App : Application
    {
        public App()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            EnsureBaseDirectory();
            StartupLog.Log("App starting.");
            base.OnStartup(e);
        }

        private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            StartupLog.Log("Dispatcher exception", e.Exception);
            MessageBox.Show($"Unhandled error:\n{e.Exception.Message}", "IEVR Mod Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            StartupLog.Log("Domain unhandled exception", e.ExceptionObject as Exception);
            MessageBox.Show("Unhandled error. See app.log in AppData.", "IEVR Mod Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private static void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
        {
            StartupLog.Log("Task unobserved exception", e.Exception);
            e.SetObserved();
        }

        private static void EnsureBaseDirectory()
        {
            try
            {
                Directory.CreateDirectory(Config.BaseDir);
                Directory.SetCurrentDirectory(Config.BaseDir);
            }
            catch (Exception ex)
            {
                StartupLog.Log("Failed to ensure base directory", ex);
            }
        }
    }

    internal static class StartupLog
    {
        private static readonly string LogPath = Path.Combine(Config.BaseDir, "app.log");

        public static void Log(string message, Exception? ex = null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}";
                if (ex != null)
                {
                    text += $"{Environment.NewLine}{ex}";
                }
                File.AppendAllText(LogPath, text + Environment.NewLine + Environment.NewLine);
            }
            catch
            {
                // ignore logging failures
            }
        }
    }
}

