using System.IO;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Forms;
using WinFormsDialog = System.Windows.Forms.OpenFileDialog;
using WpfDialog = Microsoft.Win32.OpenFileDialog;
using IEVRModManager.Models;

namespace IEVRModManager.Windows
{
    public partial class ConfigPathsWindow : Window
    {
        private AppConfig _config;
        private System.Action _saveCallback;

        public ConfigPathsWindow(Window parent, AppConfig config, System.Action saveCallback)
        {
            InitializeComponent();
            Owner = parent;
            
            _config = config;
            _saveCallback = saveCallback;
            
            DataContext = _config;
            
            // Ensure initial values are displayed correctly
            GamePathTextBox.Text = _config.GamePath ?? string.Empty;
            CfgBinPathTextBox.Text = _config.CfgBinPath ?? string.Empty;
            ViolaCliPathTextBox.Text = _config.ViolaCliPath ?? string.Empty;
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

        private void BrowseCfgBin_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WpfDialog
            {
                Title = "Select cpk_list.cfg.bin",
                Filter = "cfg.bin files (*.cfg.bin)|*.cfg.bin|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                var selectedPath = Path.GetFullPath(dialog.FileName);
                _config.CfgBinPath = selectedPath;
                CfgBinPathTextBox.Text = selectedPath;
                _saveCallback?.Invoke();
            }
        }

        private void BrowseViolaCli_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new WpfDialog
            {
                Title = "Select violacli.exe",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                var selectedPath = Path.GetFullPath(dialog.FileName);
                _config.ViolaCliPath = selectedPath;
                ViolaCliPathTextBox.Text = selectedPath;
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

        private void CfgBinPathTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                _config.CfgBinPath = textBox.Text;
                _saveCallback?.Invoke();
            }
        }

        private void ViolaCliPathTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                _config.ViolaCliPath = textBox.Text;
                _saveCallback?.Invoke();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // Save current values before closing
            _config.GamePath = GamePathTextBox.Text;
            _config.CfgBinPath = CfgBinPathTextBox.Text;
            _config.ViolaCliPath = ViolaCliPathTextBox.Text;
            _saveCallback?.Invoke();
            Close();
        }
    }
}

