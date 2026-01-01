using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using IEVRModManager.Helpers;

namespace IEVRModManager
{
    /// <summary>
    /// Interaction logic for App.xaml. Provides application-level initialization and exception handling.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="App"/> class and sets up exception handlers.
        /// </summary>
        public App()
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        /// <summary>
        /// Called when the application starts. Initializes base directory, applies language and theme settings.
        /// </summary>
        /// <param name="e">The startup event arguments.</param>
        protected override void OnStartup(StartupEventArgs e)
        {
            EnsureBaseDirectory();
            
            // Initialize logger
            var logger = Helpers.Logger.Instance;
            logger.SetMinimumLevel(LogLevel.Info);
            logger.SetFileLogging(true);
#if DEBUG
            logger.SetDebugLogging(true);
#endif
            
            ApplyLanguage();
            ApplyTheme();
            logger.Info("App starting.", true);
            base.OnStartup(e);
        }

        private void ApplyLanguage()
        {
            try
            {
                var configManager = new Managers.ConfigManager();
                var config = configManager.Load();
                string language = string.IsNullOrWhiteSpace(config.Language) ? "System" : config.Language;
                Helpers.Logger.Instance.Info($"Applying language: {language}", true);
                Helpers.LocalizationHelper.SetLanguage(language);
                
                var testString = Helpers.LocalizationHelper.GetString("AppTitle");
                Helpers.Logger.Instance.Debug($"Test string 'AppTitle' = '{testString}'", true);
            }
            catch (Exceptions.ConfigurationException ex)
            {
                Helpers.Logger.Instance.Error("Configuration error while applying language", true);
                Helpers.Logger.Instance.Log(LogLevel.Error, "Configuration error details", true, ex);
            }
            catch (Exception ex)
            {
                Helpers.Logger.Instance.Error("Error applying language", true);
                Helpers.Logger.Instance.Log(LogLevel.Error, "Error details", true, ex);
            }
        }

        private void ApplyTheme()
        {
            try
            {
                var configManager = new Managers.ConfigManager();
                var config = configManager.Load();
                string theme = string.IsNullOrWhiteSpace(config.Theme) ? "System" : config.Theme;

                string themeToUse = theme == "System" 
                    ? (IsSystemThemeDark() ? "Dark" : "Light")
                    : theme;

                ApplyThemeResource(themeToUse);
            }
            catch (Exception ex)
            {
                Helpers.Logger.Instance.Error("Error applying theme", true);
                Helpers.Logger.Instance.Log(LogLevel.Error, "Error details", true, ex);
                ApplyThemeResource("Dark");
            }
        }

        private void ApplyThemeResource(string themeName)
        {
            var resources = Current.Resources;
            resources.MergedDictionaries.Clear();

            var themeDict = new ResourceDictionary();
            var themePath = themeName switch
            {
                "Light" => "Themes/LightTheme.xaml",
                "Dark" => "Themes/DarkTheme.xaml",
                "Christmas" => "Themes/ChristmasTheme.xaml",
                "Red" => "Themes/RedTheme.xaml",
                "Blue" => "Themes/BlueTheme.xaml",
                "Purple" => "Themes/PurpleTheme.xaml",
                "Pastel" => "Themes/PastelTheme.xaml",
                "Neon" => "Themes/NeonTheme.xaml",
                _ => "Themes/DarkTheme.xaml"
            };
            themeDict.Source = new Uri(themePath, UriKind.Relative);
            resources.MergedDictionaries.Add(themeDict);
        }

        private bool IsSystemThemeDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        var appsUseLightTheme = key.GetValue("AppsUseLightTheme");
                        if (appsUseLightTheme != null && appsUseLightTheme is int value)
                        {
                            return value == 0;
                        }
                    }
                }
            }
            catch
            {
            }
            return true;
        }


        private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Helpers.Logger.Instance.Log(LogLevel.Error, "Dispatcher exception", true, e.Exception);
            MessageBox.Show($"Unhandled error:\n{e.Exception.Message}", "IEVR Mod Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Helpers.Logger.Instance.Log(LogLevel.Error, "Domain unhandled exception", true, e.ExceptionObject as Exception);
            MessageBox.Show("Unhandled error. See app.log in AppData.", "IEVR Mod Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private static void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
        {
            Helpers.Logger.Instance.Log(LogLevel.Error, "Task unobserved exception", true, e.Exception);
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
                Helpers.Logger.Instance.Log(LogLevel.Error, "Failed to ensure base directory", true, ex);
            }
        }

        /// <summary>
        /// Called when the application is shutting down. Flushes and disposes the logger and HttpClient instances.
        /// </summary>
        /// <param name="e">The exit event arguments.</param>
        protected override void OnExit(ExitEventArgs e)
        {
            // Dispose HttpClient instances to prevent socket exhaustion
            // Use fully qualified names to avoid conflicts with WPF Window types
            IEVRModManager.MainWindow.DisposeHttpClient();
            IEVRModManager.Managers.AppUpdateManager.DisposeHttpClient();
            IEVRModManager.Windows.GameBananaBrowserWindow.DisposeHttpClient();
            
            Helpers.Logger.Instance.Flush();
            Helpers.Logger.Instance.Dispose();
            base.OnExit(e);
        }
    }
}

