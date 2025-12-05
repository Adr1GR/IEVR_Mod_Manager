using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using IEVRModManager.Managers;
using IEVRModManager.Models;
using IEVRModManager.Windows;

namespace IEVRModManager
{
    public partial class MainWindow : Window
    {
        private readonly ConfigManager _configManager;
        private readonly ModManager _modManager;
        private readonly LastInstallManager _lastInstallManager;
        private readonly ViolaIntegration _viola;
        private ObservableCollection<ModEntryViewModel> _modEntries;
        private AppConfig _config = null!;

        public MainWindow()
        {
            InitializeComponent();
            
            _configManager = new ConfigManager();
            _modManager = new ModManager();
            _lastInstallManager = new LastInstallManager();
            _viola = new ViolaIntegration(message => Log(message, "info"));
            _modEntries = new ObservableCollection<ModEntryViewModel>();
            
            ModsListView.ItemsSource = _modEntries;
            
            LoadConfig();
            ScanMods();
            CleanupTempDir();
        }

        private void LoadConfig()
        {
            _config = _configManager.Load();
        }

        private void SaveConfig()
        {
            var modData = _modEntries.Select(me => new ModData
            {
                Name = me.Name,
                Enabled = me.Enabled
            }).ToList();

            var success = _configManager.Save(
                _config.GamePath,
                _config.CfgBinPath,
                _config.ViolaCliPath,
                _config.TmpDir,
                _modEntries.Select(me => new ModEntry
                {
                    Name = me.Name,
                    Path = Config.DefaultModsDir,
                    Enabled = me.Enabled,
                    DisplayName = me.DisplayName,
                    Author = me.Author,
                    ModVersion = me.ModVersion,
                    GameVersion = me.GameVersion
                }).ToList()
            );

            if (!success)
            {
                MessageBox.Show("Could not save configuration.", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Log("Could not save configuration.", "error");
            }
        }

        private void ScanMods()
        {
            var savedMods = _config?.Mods ?? new List<ModData>();
            var existingEntries = _modEntries.Select(me => new ModEntry
            {
                Name = me.Name,
                Path = Config.DefaultModsDir,
                Enabled = me.Enabled,
                DisplayName = me.DisplayName,
                Author = me.Author,
                ModVersion = me.ModVersion,
                GameVersion = me.GameVersion
            }).ToList();

            var scannedMods = _modManager.ScanMods(savedMods, existingEntries);
            
            _modEntries.Clear();
            foreach (var mod in scannedMods)
            {
                _modEntries.Add(new ModEntryViewModel(mod));
            }
            
            SaveConfig();
        }

        private void ScanMods_Click(object sender, RoutedEventArgs e)
        {
            ScanMods();
        }

        private void ModsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ModsListView.SelectedItem is ModEntryViewModel selected)
            {
                selected.Enabled = !selected.Enabled;
                SaveConfig();
            }
        }

        private void ModsListView_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            var selectedIndex = ModsListView.SelectedIndex;
            if (selectedIndex <= 0) return;

            var item = _modEntries[selectedIndex];
            _modEntries.RemoveAt(selectedIndex);
            _modEntries.Insert(selectedIndex - 1, item);
            ModsListView.SelectedIndex = selectedIndex - 1;
            SaveConfig();
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            var selectedIndex = ModsListView.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _modEntries.Count - 1) return;

            var item = _modEntries[selectedIndex];
            _modEntries.RemoveAt(selectedIndex);
            _modEntries.Insert(selectedIndex + 1, item);
            ModsListView.SelectedIndex = selectedIndex + 1;
            SaveConfig();
        }

        private void EnableAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var mod in _modEntries)
            {
                mod.Enabled = true;
            }
            SaveConfig();
        }

        private void DisableAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var mod in _modEntries)
            {
                mod.Enabled = false;
            }
            SaveConfig();
        }

        private async void ApplyMods_Click(object sender, RoutedEventArgs e)
        {
            if (_viola.IsRunning)
            {
                MessageBox.Show("A process is already running.", "Info", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Validate paths
            if (string.IsNullOrWhiteSpace(_config.GamePath) || !Directory.Exists(_config.GamePath))
            {
                MessageBox.Show("Invalid game path.", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(_config.CfgBinPath) || !File.Exists(_config.CfgBinPath))
            {
                MessageBox.Show("Invalid cpk_list.cfg.bin path.", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(_config.ViolaCliPath) || !File.Exists(_config.ViolaCliPath))
            {
                MessageBox.Show("violacli.exe not found. Please configure its path.", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var lastInstall = _lastInstallManager.Load();
            var gameDataPath = Path.Combine(_config.GamePath, "data");

            // Get enabled mods
            var modEntries = _modEntries.Select(me => new ModEntry
            {
                Name = me.Name,
                Path = Config.DefaultModsDir,
                Enabled = me.Enabled,
                DisplayName = me.DisplayName,
                Author = me.Author,
                ModVersion = me.ModVersion,
                GameVersion = me.GameVersion
            }).ToList();

            var enabledMods = _modManager.GetEnabledMods(modEntries);

            // If no mods enabled, restore original cpk_list.cfg.bin
            if (enabledMods.Count == 0)
            {
                var targetCpk = Path.Combine(gameDataPath, "cpk_list.cfg.bin");
                var removed = RemoveObsoleteFiles(lastInstall, gameDataPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                if (removed > 0)
                {
                    Log($"Removed {removed} leftover file(s) from previous install.", "info");
                }
                _lastInstallManager.Clear();

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetCpk)!);
                    File.Copy(_config.CfgBinPath, targetCpk, true);
                    Log("CHANGES APPLIED!! No mods selected.", "success");
                    
                    // Show popup when no mods are selected
                    var successWindow = new SuccessMessageWindow(this, "Original game files restored.\nNo mods were active.");
                    successWindow.ShowDialog();
                }
                catch (Exception ex)
                {
                    Log($"Error applying changes: {ex.Message}", "error");
                }
                return;
            }

            var packsModifiers = _modManager.DetectPacksModifiers(modEntries);
            if (packsModifiers.Count > 0)
            {
                var packsWarningWindow = new PacksWarningWindow(this, packsModifiers);
                var result = packsWarningWindow.ShowDialog();

                if (result != true || !packsWarningWindow.UserChoseContinue)
                {
                    Log("Operation cancelled because of data/packs warning.", "info");
                    return;
                }

                Log($"Warning: {packsModifiers.Count} mod(s) will modify data/packs. User chose to continue.", "info");
            }

            // Detect file conflicts before merging
            var conflicts = _modManager.DetectFileConflicts(modEntries);
            if (conflicts.Count > 0)
            {
                var conflictWindow = new ConflictWarningWindow(this, conflicts);
                var result = conflictWindow.ShowDialog();
                
                if (result != true || !conflictWindow.UserChoseContinue)
                {
                    // User cancelled the operation
                    Log("Operation cancelled by user due to file conflicts.", "info");
                    return;
                }
                
                Log($"Warning: {conflicts.Count} file conflict(s) detected. User chose to continue.", "info");
            }

            // Merge mods
            var tmpRoot = Path.GetFullPath(_config.TmpDir);
            Directory.CreateDirectory(tmpRoot);

            // Run merge in background
            await Task.Run(async () =>
            {
                await RunMergeAndCopy(_config.ViolaCliPath, _config.CfgBinPath, 
                    enabledMods, tmpRoot, _config.GamePath, lastInstall);
            });
        }

        private async Task RunMergeAndCopy(string violaCli, string cfgBin, 
            List<string> modPaths, string tmpRoot, string gamePath, LastInstallInfo lastInstall)
        {
            try
            {
                // Merge mods
                var success = await _viola.MergeModsAsync(violaCli, cfgBin, modPaths, tmpRoot);
                
                if (!success)
                {
                    Dispatcher.Invoke(() => Log("violacli returned error; aborting copy.", "error"));
                    return;
                }

                // Copy merged files
                var tmpData = Path.Combine(tmpRoot, "data");
                var destData = Path.Combine(gamePath, "data");

                var mergedFiles = GetRelativeFiles(tmpData);
                var mergedSet = new HashSet<string>(mergedFiles, StringComparer.OrdinalIgnoreCase);

                if (_viola.CopyMergedFiles(tmpData, destData))
                {
                    _viola.CleanupTemp(tmpRoot);
                    var removed = RemoveObsoleteFiles(lastInstall, destData, mergedSet);
                    if (removed > 0)
                    {
                        Dispatcher.Invoke(() => Log($"Removed {removed} leftover file(s) from previous install.", "info"));
                    }
                    Dispatcher.Invoke(() => Log("MODS APPLIED!!", "success"));
                    
                    // Get applied mod names in order
                    // Create a dictionary for quick lookup by folder name
                    var modNameMap = _modEntries.ToDictionary(
                        me => Path.GetFullPath(Path.Combine(Config.DefaultModsDir, me.Name)),
                        me => me.DisplayName);
                    
                    var modNames = modPaths.Select(path =>
                    {
                        var fullPath = Path.GetFullPath(path);
                        return modNameMap.TryGetValue(fullPath, out var displayName) 
                            ? displayName 
                            : Path.GetFileName(path);
                    }).ToList();
                    
                    // Show success popup
                    var modCount = modPaths.Count;
                    var message = modCount == 1 
                        ? "1 Mod Applied Successfully" 
                        : $"{modCount} Mods Applied Successfully";
                    
                    _lastInstallManager.Save(new LastInstallInfo
                    {
                        GamePath = Path.GetFullPath(gamePath),
                        Files = mergedFiles,
                        Mods = modNames,
                        AppliedAt = DateTime.UtcNow
                    });

                    Dispatcher.Invoke(() =>
                    {
                        var successWindow = new SuccessMessageWindow(this, message, modNames);
                        successWindow.ShowDialog();
                    });
                }
                else
                {
                    Dispatcher.Invoke(() => Log("Failed to copy merged files.", "error"));
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => Log($"Unexpected error: {ex.Message}", "error"));
            }
        }

        private static List<string> GetRelativeFiles(string root)
        {
            if (!Directory.Exists(root))
            {
                return new List<string>();
            }

            return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
                .ToList();
        }

        private int RemoveObsoleteFiles(LastInstallInfo previousInstall, string destData, HashSet<string> newFiles)
        {
            if (previousInstall.Files == null || previousInstall.Files.Count == 0)
            {
                return 0;
            }

            if (!PathsMatch(previousInstall.GamePath, _config.GamePath))
            {
                Dispatcher.Invoke(() => Log("Skipped cleanup: stored install points to a different game path.", "info"));
                return 0;
            }

            var destRoot = Path.GetFullPath(destData);
            var removed = 0;

            foreach (var relativePath in previousInstall.Files)
            {
                if (newFiles.Contains(relativePath))
                {
                    continue;
                }

                var targetPath = Path.GetFullPath(Path.Combine(destRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!targetPath.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                        removed++;
                        var parentDir = Path.GetDirectoryName(targetPath);
                        if (!string.IsNullOrEmpty(parentDir))
                        {
                            RemoveEmptyParents(parentDir, destRoot);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => Log($"Could not delete leftover file {relativePath}: {ex.Message}", "error"));
                }
            }

            return removed;
        }

        private static void RemoveEmptyParents(string dir, string stopAt)
        {
            var stopRoot = Path.GetFullPath(stopAt);
            var current = dir;
            while (!string.IsNullOrEmpty(current) &&
                   current.StartsWith(stopRoot, StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(current) && !Directory.EnumerateFileSystemEntries(current).Any())
                {
                    Directory.Delete(current);
                    current = Path.GetDirectoryName(current);
                }
                else
                {
                    break;
                }
            }
        }

        private static bool PathsMatch(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            {
                return false;
            }

            var a = Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var b = Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return a.Equals(b, StringComparison.OrdinalIgnoreCase);
        }

        private void OpenModsFolder_Click(object sender, RoutedEventArgs e)
        {
            var path = Path.GetFullPath(Config.DefaultModsDir);
            if (Directory.Exists(path))
            {
                System.Diagnostics.Process.Start("explorer.exe", path);
            }
            else
            {
                MessageBox.Show($"{path} does not exist", "Info", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void HelpLink_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBlock textBlock)
            {
                textBlock.TextDecorations = System.Windows.TextDecorations.Underline;
                textBlock.FontSize = 13;
            }
        }

        private void HelpLink_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBlock textBlock)
            {
                textBlock.TextDecorations = null;
                textBlock.FontSize = 12;
            }
        }

        private void HelpLink_Click(object sender, MouseButtonEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/Adr1GR/IEVR_Mod_Manager?tab=readme-ov-file#using-the-mod-manager",
                UseShellExecute = true
            });
        }

        private void Downloads_Click(object sender, RoutedEventArgs e)
        {
            var window = new DownloadsWindow(this);
            window.ShowDialog();
        }

        private void Configuration_Click(object sender, RoutedEventArgs e)
        {
            // Create a copy of the configuration for the window
            var configCopy = new AppConfig
            {
                GamePath = _config.GamePath,
                CfgBinPath = _config.CfgBinPath,
                ViolaCliPath = _config.ViolaCliPath,
                TmpDir = _config.TmpDir,
                Mods = _config.Mods
            };
            
            var window = new ConfigPathsWindow(this, configCopy, () =>
            {
                // Save when something changes
                _config.GamePath = configCopy.GamePath;
                _config.CfgBinPath = configCopy.CfgBinPath;
                _config.ViolaCliPath = configCopy.ViolaCliPath;
                SaveConfig();
            });
            
            window.ShowDialog();
            
            // Ensure final values are saved
            _config.GamePath = configCopy.GamePath;
            _config.CfgBinPath = configCopy.CfgBinPath;
            _config.ViolaCliPath = configCopy.ViolaCliPath;
            SaveConfig();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            if (_viola.IsRunning)
            {
                var result = MessageBox.Show(
                    "There is an operation in progress. Are you sure you want to exit?",
                    "Exit",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result == MessageBoxResult.No)
                    return;
                
                _viola.Stop();
            }

            SaveConfig();
            Close();
        }

        private void Log(string message, string level = "info")
        {
            Dispatcher.Invoke(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                var formattedMessage = $"[{timestamp}] {message}\n";
                
                // Limit log size to prevent memory issues
                var lines = LogTextBox.LineCount;
                if (lines > 1000)
                {
                    var startIndex = LogTextBox.GetCharacterIndexFromLineIndex(lines - 1000);
                    LogTextBox.Text = LogTextBox.Text.Substring(startIndex);
                }
                
                LogTextBox.AppendText(formattedMessage);
                LogTextBox.CaretIndex = LogTextBox.Text.Length;
                LogTextBox.ScrollToEnd();
            });
        }

        private void CleanupTempDir()
        {
            var tmpRoot = _config?.TmpDir ?? Config.DefaultTmpDir;
            if (Directory.Exists(tmpRoot))
            {
                try
                {
                    Directory.Delete(tmpRoot, true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Could not clean temporary folder {tmpRoot}: {ex.Message}");
                }
            }
            Directory.CreateDirectory(tmpRoot);
        }
    }

    public class ModEntryViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _enabled;

        public string Name { get; set; } = string.Empty;
        
        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                OnPropertyChanged(nameof(Enabled));
                OnPropertyChanged(nameof(EnabledIcon));
            }
        }
        
        public string EnabledIcon => Enabled ? "✓" : "✗";
        public string DisplayName { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string ModVersion { get; set; } = string.Empty;
        public string GameVersion { get; set; } = string.Empty;

        public ModEntryViewModel(ModEntry mod)
        {
            Name = mod.Name;
            Enabled = mod.Enabled;
            DisplayName = mod.DisplayName;
            Author = mod.Author;
            ModVersion = string.IsNullOrEmpty(mod.ModVersion) ? "—" : mod.ModVersion;
            GameVersion = string.IsNullOrEmpty(mod.GameVersion) ? "—" : mod.GameVersion;
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}

