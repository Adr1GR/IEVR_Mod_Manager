using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using IEVRModManager.Models;
using IEVRModManager.Helpers;
using System.Threading.Tasks;

namespace IEVRModManager.Windows
{
    /// <summary>
    /// Interaction logic for ConfigPathsWindow.xaml. Allows users to configure application paths and settings.
    /// </summary>
    public partial class ConfigPathsWindow : Window
    {
        private AppConfig _config;
        private System.Action _saveCallback;
        private readonly Func<Task> _createBackupAction;
        private readonly Func<Task> _restoreBackupAction;
        private readonly Func<Task> _checkForUpdatesAction;
        private readonly System.Action _openDownloadsAction;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigPathsWindow"/> class.
        /// </summary>
        /// <param name="parent">The parent window.</param>
        /// <param name="config">The application configuration to edit.</param>
        /// <param name="saveCallback">Callback to execute when configuration is saved.</param>
        /// <param name="createBackupAction">Action to create a backup.</param>
        /// <param name="restoreBackupAction">Action to restore from a backup.</param>
        /// <param name="checkForUpdatesAction">Action to check for updates.</param>
        /// <param name="openDownloadsAction">Action to open downloads window.</param>
        public ConfigPathsWindow(Window parent, AppConfig config, System.Action saveCallback, Func<Task> createBackupAction, Func<Task> restoreBackupAction, Func<Task> checkForUpdatesAction, System.Action openDownloadsAction)
        {
            InitializeComponent();
            Owner = parent;
            
            _config = config;
            _saveCallback = saveCallback;
            _createBackupAction = createBackupAction;
            _restoreBackupAction = restoreBackupAction;
            _checkForUpdatesAction = checkForUpdatesAction;
            _openDownloadsAction = openDownloadsAction;
            
            DataContext = _config;
            
            // Update localized texts
            UpdateLocalizedTexts();
            
            // Try to detect game path from Steam if not configured
            if (string.IsNullOrWhiteSpace(_config.GamePath) || !Directory.Exists(_config.GamePath))
            {
                TryDetectGamePathFromSteam();
            }
            
            // Ensure initial values are displayed correctly
            GamePathTextBox.Text = _config.GamePath ?? string.Empty;
            
            // Set up technical logs toggle
            if (ShowTechnicalLogsToggle != null)
            {
                ShowTechnicalLogsToggle.IsChecked = _config.ShowTechnicalLogs;
            }
            
            // Set up language combo box
            if (LanguageComboBox != null)
            {
                string language = string.IsNullOrWhiteSpace(_config.Language) ? "System" : _config.Language;
                foreach (System.Windows.Controls.ComboBoxItem item in LanguageComboBox.Items)
                {
                    if (item.Tag?.ToString() == language)
                    {
                        LanguageComboBox.SelectedItem = item;
                        break;
                    }
                }
            }

            // Set up theme combo box
            if (ThemeComboBox != null)
            {
                string theme = string.IsNullOrWhiteSpace(_config.Theme) ? "System" : _config.Theme;
                foreach (System.Windows.Controls.ComboBoxItem item in ThemeComboBox.Items)
                {
                    if (item.Tag?.ToString() == theme)
                    {
                        ThemeComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private void LanguageComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.ComboBox comboBox && comboBox.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
            {
                string language = selectedItem.Tag?.ToString() ?? "System";
                string previousLanguage = _config.Language ?? "System";
                
                // Only show message if language actually changed
                if (language != previousLanguage)
                {
                    _config.Language = language;
                    _saveCallback?.Invoke();
                    
                    // Show modal with restart option (same as theme change)
                    string languageName = language == "System" ? LocalizationHelper.GetString("System") + " (follows OS language)" : 
                                         language == "en-US" ? LocalizationHelper.GetString("English") : 
                                         language == "fr-FR" ? LocalizationHelper.GetString("French") :
                                         language == "es-ES" ? LocalizationHelper.GetString("Espanol") : language;
                    var themeWindow = new ThemeChangeWindow(this, string.Format(LocalizationHelper.GetString("LanguageChangedMessage"), languageName));
                    var result = themeWindow.ShowDialog();
                    
                    if (result == true && themeWindow.UserChoseRestart)
                    {
                        // Restart the application
                        var appPath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                        if (string.IsNullOrEmpty(appPath))
                        {
                            // Fallback: use the executable name from the current process
                            appPath = System.IO.Path.Combine(AppContext.BaseDirectory, System.Diagnostics.Process.GetCurrentProcess().ProcessName + ".exe");
                        }
                        
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = appPath,
                            UseShellExecute = true
                        });
                        System.Windows.Application.Current.Shutdown();
                    }
                }
            }
        }

        private void ThemeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.ComboBox comboBox && comboBox.SelectedItem is System.Windows.Controls.ComboBoxItem selectedItem)
            {
                string theme = selectedItem.Tag?.ToString() ?? "System";
                string previousTheme = _config.Theme ?? "System";
                
                // Only show message if theme actually changed
                if (theme != previousTheme)
                {
                    _config.Theme = theme;
                    _saveCallback?.Invoke();
                    
                    // Show modal with restart option
                    string themeName = theme == "System" ? LocalizationHelper.GetString("System") + " (follows OS theme)" :
                                       theme == "Light" ? LocalizationHelper.GetString("Light") :
                                       theme == "Dark" ? LocalizationHelper.GetString("Dark") :
                                       theme == "Christmas" ? LocalizationHelper.GetString("Christmas") :
                                       theme == "Red" ? LocalizationHelper.GetString("Red") :
                                       theme;
                    var themeWindow = new ThemeChangeWindow(this, themeName);
                    var result = themeWindow.ShowDialog();
                    
                    if (result == true && themeWindow.UserChoseRestart)
                    {
                        // Restart the application
                        // Use Environment.ProcessPath for single-file apps, fallback to executable name
                        var appPath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                        if (string.IsNullOrEmpty(appPath))
                        {
                            // Fallback: use the executable name from the current process
                            appPath = System.IO.Path.Combine(AppContext.BaseDirectory, System.Diagnostics.Process.GetCurrentProcess().ProcessName + ".exe");
                        }
                        
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = appPath,
                            UseShellExecute = true
                        });
                        System.Windows.Application.Current.Shutdown();
                    }
                }
            }
        }

        private void BrowseGame_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the game root folder"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _config.GamePath = Path.GetFullPath(dialog.SelectedPath);
                _saveCallback?.Invoke();
            }
        }

        private void GamePathTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                _config.GamePath = textBox.Text;
                _saveCallback?.Invoke();
            }
        }

        private void UpdateLocalizedTexts()
        {
            Title = LocalizationHelper.GetString("ConfigTitle");
            ConfigTitleLabel.Content = LocalizationHelper.GetString("ConfigTitle");
            ConfigInfoLabel.Content = LocalizationHelper.GetString("ConfigSavedAutomatically");
            GamePathLabel.Content = LocalizationHelper.GetString("GamePath");
            BrowseButton.Content = LocalizationHelper.GetString("Browse");
            CpkStorageLabel.Content = LocalizationHelper.GetString("CpkStorageFolder");
            CpkStorageDescription.Text = LocalizationHelper.GetString("CpkStorageDescription");
            OpenCpkStorageButton.Content = LocalizationHelper.GetString("OpenFolder");
            ViolaCLILabel.Content = LocalizationHelper.GetString("ViolaCLIFolder");
            ViolaCLIDescription.Text = LocalizationHelper.GetString("ViolaCLIDescription");
            OpenViolaStorageButton.Content = LocalizationHelper.GetString("OpenFolder");
            LanguageLabel.Content = LocalizationHelper.GetString("LanguageLabel");
            ThemeLabel.Content = LocalizationHelper.GetString("ThemeLabel");
            if (ShowTechnicalLogsLabel != null)
            {
                ShowTechnicalLogsLabel.Content = LocalizationHelper.GetString("ShowTechnicalLogs");
            }
            if (ShowTechnicalLogsDescription != null)
            {
                ShowTechnicalLogsDescription.Text = LocalizationHelper.GetString("ShowTechnicalLogsDescription");
            }
            CreateBackupButton.Content = LocalizationHelper.GetString("CreateBackup");
            RestoreBackupButton.Content = LocalizationHelper.GetString("RestoreBackup");
            if (CheckForUpdatesButtonText != null)
                CheckForUpdatesButtonText.Text = LocalizationHelper.GetString("CheckForUpdates");
            if (LinksButton != null)
                LinksButton.Content = LocalizationHelper.GetString("Links");
            CloseButton.Content = LocalizationHelper.GetString("Close");
            
            // Update combo box items
            if (LanguageComboBox != null)
            {
                foreach (System.Windows.Controls.ComboBoxItem item in LanguageComboBox.Items)
                {
                    if (item.Tag?.ToString() == "System")
                        item.Content = LocalizationHelper.GetString("System");
                    else if (item.Tag?.ToString() == "en-US")
                        item.Content = LocalizationHelper.GetString("English");
                    else if (item.Tag?.ToString() == "fr-FR")
                        item.Content = LocalizationHelper.GetString("French");
                    else if (item.Tag?.ToString() == "es-ES")
                        item.Content = LocalizationHelper.GetString("Espanol");
                }
            }
            
            if (ThemeComboBox != null)
            {
                foreach (System.Windows.Controls.ComboBoxItem item in ThemeComboBox.Items)
                {
                    if (item.Tag?.ToString() == "System")
                        item.Content = LocalizationHelper.GetString("System");
                    else if (item.Tag?.ToString() == "Light")
                        item.Content = LocalizationHelper.GetString("Light");
                    else if (item.Tag?.ToString() == "Dark")
                        item.Content = LocalizationHelper.GetString("Dark");
                    else if (item.Tag?.ToString() == "Christmas")
                        item.Content = LocalizationHelper.GetString("Christmas");
                    else if (item.Tag?.ToString() == "Red")
                        item.Content = LocalizationHelper.GetString("Red");
                }
            }
        }

        private void ShowTechnicalLogsToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (ShowTechnicalLogsToggle != null)
            {
                _config.ShowTechnicalLogs = ShowTechnicalLogsToggle.IsChecked ?? false;
                _saveCallback?.Invoke();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // Save current values before closing
            _config.GamePath = GamePathTextBox.Text;
            if (ShowTechnicalLogsToggle != null)
            {
                _config.ShowTechnicalLogs = ShowTechnicalLogsToggle.IsChecked ?? false;
            }
            _saveCallback?.Invoke();
            Close();
        }

        private void OpenCpkStorage_Click(object sender, RoutedEventArgs e)
        {
            TryOpenFolder(Config.SharedStorageCpkDir);
        }

        private void OpenViolaStorage_Click(object sender, RoutedEventArgs e)
        {
            TryOpenFolder(Config.SharedStorageViolaDir);
        }

        private void TryOpenFolder(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (System.Exception ex)
            {
                var errorWindow = new MessageWindow(this, 
                    LocalizationHelper.GetString("ErrorTitle"), 
                    string.Format(LocalizationHelper.GetString("CouldNotOpenFolder"), ex.Message), 
                    MessageType.Error);
                errorWindow.ShowDialog();
            }
        }

        private async void CreateBackup_Click(object sender, RoutedEventArgs e)
        {
            if (_createBackupAction == null)
            {
                return;
            }

            // The action already shows confirmation dialog, so just invoke it
            try
            {
                await _createBackupAction.Invoke();
            }
            catch (Exception ex)
            {
                var errorWindow = new MessageWindow(this, 
                    LocalizationHelper.GetString("ErrorTitle"), 
                    string.Format(LocalizationHelper.GetString("ErrorCreatingBackup"), ex.Message), 
                    MessageType.Error);
                errorWindow.ShowDialog();
            }
        }

        private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            if (_restoreBackupAction == null)
            {
                return;
            }

            // The action already shows confirmation dialog, so just invoke it
            try
            {
                await _restoreBackupAction.Invoke();
            }
            catch (Exception ex)
            {
                var errorWindow = new MessageWindow(this, 
                    LocalizationHelper.GetString("ErrorTitle"), 
                    string.Format(LocalizationHelper.GetString("ErrorRestoringBackup"), ex.Message), 
                    MessageType.Error);
                errorWindow.ShowDialog();
            }
        }

        private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            if (_checkForUpdatesAction == null)
            {
                return;
            }

            try
            {
                await _checkForUpdatesAction.Invoke();
            }
            catch (Exception ex)
            {
                var errorWindow = new MessageWindow(this, 
                    LocalizationHelper.GetString("ErrorTitle"), 
                    string.Format(LocalizationHelper.GetString("UpdateCheckFailed"), ex.Message), 
                    MessageType.Error);
                errorWindow.ShowDialog();
            }
        }

        private void Links_Click(object sender, RoutedEventArgs e)
        {
            _openDownloadsAction?.Invoke();
        }

        private void TryDetectGamePathFromSteam()
        {
            try
            {
                var detectedPath = Helpers.SteamHelper.DetectGamePath();
                if (!string.IsNullOrWhiteSpace(detectedPath) && Directory.Exists(detectedPath))
                {
                    _config.GamePath = detectedPath;
                    GamePathTextBox.Text = detectedPath;
                    _saveCallback?.Invoke();
                    
                    var message = string.Format(LocalizationHelper.GetString("GamePathDetectedFromSteam"), detectedPath);
                    var messageWindow = new MessageWindow(this, 
                        LocalizationHelper.GetString("InfoTitle"), 
                        message, 
                        MessageType.Info);
                    messageWindow.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                // Silently fail - user can still manually set the path
                System.Diagnostics.Debug.WriteLine($"Error detecting game path from Steam: {ex.Message}");
            }
        }
    }
}

