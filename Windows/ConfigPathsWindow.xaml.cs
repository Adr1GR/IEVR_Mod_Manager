using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using IEVRModManager.Models;
using System.Threading.Tasks;

namespace IEVRModManager.Windows
{
    public partial class ConfigPathsWindow : Window
    {
        private AppConfig _config;
        private System.Action _saveCallback;
        private readonly Func<Task> _createBackupAction;
        private readonly Func<Task> _restoreBackupAction;

        public ConfigPathsWindow(Window parent, AppConfig config, System.Action saveCallback, Func<Task> createBackupAction, Func<Task> restoreBackupAction)
        {
            InitializeComponent();
            Owner = parent;
            
            _config = config;
            _saveCallback = saveCallback;
            _createBackupAction = createBackupAction;
            _restoreBackupAction = restoreBackupAction;
            
            DataContext = _config;
            
            // Ensure initial values are displayed correctly
            GamePathTextBox.Text = _config.GamePath ?? string.Empty;
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

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // Save current values before closing
            _config.GamePath = GamePathTextBox.Text;
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

        private static void TryOpenFolder(string path)
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
                System.Windows.MessageBox.Show($"Could not open folder: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CreateBackup_Click(object sender, RoutedEventArgs e)
        {
            if (_createBackupAction == null)
            {
                return;
            }

            try
            {
                await _createBackupAction.Invoke();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error creating backup: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            if (_restoreBackupAction == null)
            {
                return;
            }

            try
            {
                await _restoreBackupAction.Invoke();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error restoring backup: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

