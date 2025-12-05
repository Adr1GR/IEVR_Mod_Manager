using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        private readonly ObservableCollection<CpkOption> _availableCpkFiles = new();
        private static readonly HttpClient _httpClient = new();
        private AppConfig _config = null!;
        private bool _isApplying;

        public MainWindow()
        {
            InitializeComponent();
            
            _configManager = new ConfigManager();
            _modManager = new ModManager();
            _lastInstallManager = new LastInstallManager();
            _viola = new ViolaIntegration(message => Log(message, "info"));
            _modEntries = new ObservableCollection<ModEntryViewModel>();
            
            ModsListView.ItemsSource = _modEntries;
            CpkSelector.ItemsSource = _availableCpkFiles;
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("IEVRModManager/1.0");
            
            LoadConfig();
            EnsureStorageStructure();
            RefreshCpkOptions();
            ScanMods();
            CleanupTempDir();
        }

        private void LoadConfig()
        {
            _config = _configManager.Load();
        }

        private static void EnsureStorageStructure()
        {
            _ = Config.SharedStorageDir;
            _ = Config.SharedStorageCpkDir;
            _ = Config.SharedStorageViolaDir;
        }

        private void RefreshCpkOptions()
        {
            try
            {
                var cpkDir = Config.SharedStorageCpkDir;
                var files = Directory.Exists(cpkDir)
                    ? Directory.GetFiles(cpkDir, "*.bin", SearchOption.TopDirectoryOnly)
                        .Concat(Directory.GetFiles(cpkDir, "*.cfg.bin", SearchOption.TopDirectoryOnly))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(Path.GetFileName)
                        .ToList()
                    : new List<string>();

                _availableCpkFiles.Clear();
                foreach (var file in files)
                {
                    _availableCpkFiles.Add(new CpkOption { FileName = Path.GetFileName(file) });
                }

                var preferred = _config.SelectedCpkName;
                if (string.IsNullOrWhiteSpace(preferred) && !string.IsNullOrWhiteSpace(_config.CfgBinPath))
                {
                    preferred = Path.GetFileName(_config.CfgBinPath);
                }

                if (!string.IsNullOrWhiteSpace(preferred) && _availableCpkFiles.Any(o => o.FileName == preferred))
                {
                    CpkSelector.SelectedValue = preferred;
                    _config.SelectedCpkName = preferred;
                    _config.CfgBinPath = Path.Combine(cpkDir, preferred);
                }
                else if (_availableCpkFiles.Count > 0)
                {
                    var first = _availableCpkFiles[0];
                    CpkSelector.SelectedValue = first.FileName;
                    _config.SelectedCpkName = first.FileName;
                    _config.CfgBinPath = Path.Combine(cpkDir, first.FileName);
                }
                else
                {
                    CpkSelector.SelectedValue = null;
                    _config.SelectedCpkName = string.Empty;
                    _config.CfgBinPath = string.Empty;
                }
            }
            catch (Exception ex)
            {
                Log($"Error refreshing cpk list: {ex.Message}", "error");
            }
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
                _config.SelectedCpkName,
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

        private string? ResolveSelectedCpkPath()
        {
            var cpkDir = Config.SharedStorageCpkDir;
            if (!Directory.Exists(cpkDir))
            {
                Directory.CreateDirectory(cpkDir);
            }

            if (string.IsNullOrWhiteSpace(_config.SelectedCpkName))
            {
                Log("No cpk_list.cfg.bin selected.", "error");
                return null;
            }

            var selectedPath = Path.Combine(cpkDir, _config.SelectedCpkName);
            if (!File.Exists(selectedPath))
            {
                Log($"Could not find {_config.SelectedCpkName} in the cpk folder.", "error");
                return null;
            }

            _config.CfgBinPath = selectedPath;
            return selectedPath;
        }

        private string? ResolveViolaCliPath()
        {
            var violaDir = Config.SharedStorageViolaDir;
            if (!Directory.Exists(violaDir))
            {
                Directory.CreateDirectory(violaDir);
            }

            var executables = Directory.GetFiles(violaDir, "*.exe", SearchOption.TopDirectoryOnly);

            if (executables.Length == 0)
            {
                MessageBox.Show("No Viola executable found in the shared folder.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }

            if (executables.Length > 1)
            {
                MessageBox.Show("There is more than one executable in the Viola folder. Keep only one.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }

            _config.ViolaCliPath = executables[0];
            return executables[0];
        }

        private async Task<int> DownloadCpkFilesAsync()
        {
            const string apiUrl = "https://api.github.com/repos/Adr1GR/IEVR_Mod_Manager/contents/cpk_list";
            var targetDir = Config.SharedStorageCpkDir;
            Directory.CreateDirectory(targetDir);

            using var response = await _httpClient.GetAsync(apiUrl);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();

            var items = JsonSerializer.Deserialize<List<GithubContent>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<GithubContent>();

            var candidates = items
                .Where(i => i.Type == "file" && (i.Name.EndsWith(".cfg.bin", StringComparison.OrdinalIgnoreCase) || i.Name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var downloaded = 0;
            foreach (var item in candidates)
            {
                var targetPath = Path.Combine(targetDir, item.Name);
                if (File.Exists(targetPath))
                {
                    continue;
                }

                var downloadUrl = item.DownloadUrl;
                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    downloadUrl = $"https://raw.githubusercontent.com/Adr1GR/IEVR_Mod_Manager/main/cpk_list/{item.Name}";
                }

                var bytes = await _httpClient.GetByteArrayAsync(downloadUrl);
                await File.WriteAllBytesAsync(targetPath, bytes);
                downloaded++;
            }

            return downloaded;
        }

        private void SetApplyButtonEnabled(bool isEnabled)
        {
            ApplyButton.IsEnabled = isEnabled;
            PlayButton.IsEnabled = isEnabled;
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

        private void CpkSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CpkSelector.SelectedValue is string selectedName)
            {
                _config.SelectedCpkName = selectedName;
                _config.CfgBinPath = Path.Combine(Config.SharedStorageCpkDir, selectedName);
                SaveConfig();
            }
        }

        private async void DownloadCpkLists_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button)
            {
                button.IsEnabled = false;
            }

            try
            {
                Log("Fetching available cpk_list files from GitHub...", "info");
                var downloaded = await DownloadCpkFilesAsync();
                if (downloaded > 0)
                {
                    Log($"Downloaded {downloaded} cpk_list file(s).", "success");
                    RefreshCpkOptions();
                }
                else
                {
                    Log("No new cpk_list files downloaded (they may already exist).", "info");
                }
            }
            catch (Exception ex)
            {
                Log($"Error downloading cpk_list files: {ex.Message}", "error");
            }
            finally
            {
                if (sender is System.Windows.Controls.Button b)
                {
                    b.IsEnabled = true;
                }
            }
        }

        private void OpenCpkStorage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folder = Config.SharedStorageCpkDir;
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log($"Could not open cpk folder: {ex.Message}", "error");
            }
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

            if (_isApplying)
            {
                MessageBox.Show("A process is already running.", "Info", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _isApplying = true;
            SetApplyButtonEnabled(false);

            try
            {
                RefreshCpkOptions();

                // Validate paths
                if (string.IsNullOrWhiteSpace(_config.GamePath) || !Directory.Exists(_config.GamePath))
                {
                    MessageBox.Show("Invalid game path.", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var selectedCpkPath = ResolveSelectedCpkPath();
                if (selectedCpkPath == null)
                {
                    MessageBox.Show("No cpk_list.cfg.bin selected or it was not found in the shared folder.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var violaExePath = ResolveViolaCliPath();
                if (violaExePath == null)
                {
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
                        File.Copy(selectedCpkPath, targetCpk, true);
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
                    await RunMergeAndCopy(violaExePath, selectedCpkPath, 
                        enabledMods, tmpRoot, _config.GamePath, lastInstall);
                });
            }
            finally
            {
                _isApplying = false;
                SetApplyButtonEnabled(true);
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isApplying || _viola.IsRunning)
            {
                MessageBox.Show("Please wait until mod application finishes.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(_config.GamePath) || !Directory.Exists(_config.GamePath))
            {
                MessageBox.Show("Select the game path before using Play.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var exePath = Path.Combine(_config.GamePath, "nie.exe");
            if (!File.Exists(exePath))
            {
                MessageBox.Show("nie.exe was not found in the game path.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = _config.GamePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not start the game: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                SelectedCpkName = _config.SelectedCpkName,
                ViolaCliPath = _config.ViolaCliPath,
                TmpDir = _config.TmpDir,
                Mods = _config.Mods
            };
            
            var window = new ConfigPathsWindow(this, configCopy, () =>
            {
                // Save when something changes
                _config.GamePath = configCopy.GamePath;
                _config.CfgBinPath = configCopy.CfgBinPath;
                _config.SelectedCpkName = configCopy.SelectedCpkName;
                _config.ViolaCliPath = configCopy.ViolaCliPath;
                SaveConfig();
            });
            
            window.ShowDialog();
            
            // Ensure final values are saved
            _config.GamePath = configCopy.GamePath;
            _config.CfgBinPath = configCopy.CfgBinPath;
            _config.SelectedCpkName = configCopy.SelectedCpkName;
            _config.ViolaCliPath = configCopy.ViolaCliPath;
            RefreshCpkOptions();
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

    public class CpkOption
    {
        public string FileName { get; set; } = string.Empty;
        public string DisplayName
        {
            get
            {
                var name = Path.GetFileName(FileName);
                if (name.EndsWith(".cfg.bin", StringComparison.OrdinalIgnoreCase))
                {
                    return name[..^8];
                }
                return Path.GetFileNameWithoutExtension(name);
            }
        }
    }

    public class GithubContent
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        [JsonPropertyName("download_url")]
        public string DownloadUrl { get; set; } = string.Empty;
    }
}
