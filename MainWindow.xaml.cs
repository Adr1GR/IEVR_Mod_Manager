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
using System.Windows.Threading;
using Microsoft.Win32;
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
        private DispatcherTimer? _playResetTimer;

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
                        .Where(f => !string.IsNullOrWhiteSpace(Path.GetFileName(f)))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(Path.GetFileName)
                        .ToList()
                    : new List<string>();

                _availableCpkFiles.Clear();
                foreach (var file in files)
                {
                    _availableCpkFiles.Add(new CpkOption { FileName = Path.GetFileName(file) });
                }

                if (_availableCpkFiles.Count == 0)
                {
                    CpkSelector.SelectedIndex = -1;
                    _config.SelectedCpkName = string.Empty;
                    _config.CfgBinPath = string.Empty;
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
                Enabled = me.Enabled,
                ModLink = me.ModLink
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
                    GameVersion = me.GameVersion,
                    ModLink = me.ModLink
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
                GameVersion = me.GameVersion,
                ModLink = me.ModLink
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

        private void ModLink_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBlock tb &&
                tb.DataContext is ModEntryViewModel vm)
            {
                var link = vm.ModLink?.Trim();
                if (string.IsNullOrWhiteSpace(link))
                {
                    return;
                }

                if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
                {
                    MessageBox.Show("Invalid mod link.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = uri.ToString(),
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open the link: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
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

        public async void DownloadCpkLists_Click(object sender, RoutedEventArgs e)
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

        private async void CreateBackup_Click(object sender, RoutedEventArgs e)
        {
            if (_viola.IsRunning || _isApplying)
            {
                MessageBox.Show("Please wait until the current operation finishes.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var gamePath = _config.GamePath;
            if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
            {
                MessageBox.Show("Invalid game path. Set it in Configuration first.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                SetApplyButtonEnabled(false);
                var created = await EnsureBackupExistsAsync(gamePath, true, true);
                if (!created)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                Log($"Error creating backup: {ex.Message}", "error");
                MessageBox.Show($"Error creating backup: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetApplyButtonEnabled(true);
            }
        }

        private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            if (_viola.IsRunning || _isApplying)
            {
                MessageBox.Show("Please wait until the current operation finishes.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var gamePath = _config.GamePath;
            if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
            {
                MessageBox.Show("Invalid game path. Set it in Configuration first.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var gameFolderName = new DirectoryInfo(gamePath).Name;
            var backupRoot = Path.Combine(Config.BackupDir, gameFolderName);
            if (!Directory.Exists(backupRoot))
            {
                MessageBox.Show("No backup folder was found in the manager data folder.", "Backup not found",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Log("No backup folder found; cannot restore.", "error");
                return;
            }

            try
            {
                SetApplyButtonEnabled(false);
                Log("Restoring backup...", "info");

                await Task.Run(() =>
                {
                    CopyTopLevelFiles(backupRoot, gamePath);
                    RestoreDataBackup(gamePath, backupRoot);
                });

                Log($"Backup restored successfully from {backupRoot}.", "success");
                MessageBox.Show("Backup restored successfully.", "Backup",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"Error restoring backup: {ex.Message}", "error");
                MessageBox.Show($"Error restoring backup: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetApplyButtonEnabled(true);
            }
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

                // Ensure backup exists before making changes
                var backupReady = await EnsureBackupExistsAsync(_config.GamePath, false, false);
                if (!backupReady)
                {
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

                var gameDataPath = Path.Combine(_config.GamePath, "data");
                var tmpRoot = Path.GetFullPath(_config.TmpDir);

                if (!Directory.Exists(gameDataPath))
                {
                    MessageBox.Show("Invalid game path: no data folder found.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Quick read/write checks so we fail fast on permission issues
                if (!CheckReadWriteAccess(gameDataPath, "game data folder"))
                {
                    MessageBox.Show("No se pudo escribir en la carpeta del juego. Revisa permisos (ej. ejecutar como administrador) o que la ruta no esté protegida.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!CheckReadWriteAccess(tmpRoot, "temporary folder"))
                {
                    MessageBox.Show("No se pudo escribir en la carpeta temporal configurada. Revisa permisos o cambia la ruta en Configuración.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var lastInstall = _lastInstallManager.Load();

                // Get enabled mods
                var modEntries = _modEntries.Select(me => new ModEntry
                {
                    Name = me.Name,
                    Path = Config.DefaultModsDir,
                    Enabled = me.Enabled,
                    DisplayName = me.DisplayName,
                    Author = me.Author,
                    ModVersion = me.ModVersion,
                GameVersion = me.GameVersion,
                ModLink = me.ModLink
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

            if (!ModsMatchLastInstall())
            {
                var window = new PendingChangesWindow(this,
                    "The currently selected mods do not match the last applied set. Apply changes to ensure you play with the selected mods. Do you want to continue anyway?");
                var result = window.ShowDialog();
                if (result != true || !window.UserChoseContinue)
                {
                    return;
                }
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
                SetPlayLoadingState(true);
                Log("Launching game...", "info");

                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = _config.GamePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                SetPlayLoadingState(false);
                MessageBox.Show($"Could not start the game: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetPlayLoadingState(bool isLoading)
        {
            _playResetTimer?.Stop();

            if (isLoading)
            {
                PlayButton.Content = "Launching game...";
                _playResetTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(5)
                };
                _playResetTimer.Tick += (_, _) =>
                {
                    PlayButton.Content = "▶ Play";
                    _playResetTimer?.Stop();
                };
                _playResetTimer.Start();
            }
            else
            {
                PlayButton.Content = "▶ Play";
            }
        }

        private bool ModsMatchLastInstall()
        {
            var last = _lastInstallManager.Load();
            if (!PathsMatch(last.GamePath, _config.GamePath))
            {
                // Different game path or never applied
                return last.Mods.Count == 0 && _modEntries.All(m => !m.Enabled);
            }

            var currentEnabled = _modEntries
                .Where(m => m.Enabled)
                .Select(m => string.IsNullOrWhiteSpace(m.DisplayName) ? m.Name : m.DisplayName)
                .ToList();

            if (currentEnabled.Count != last.Mods.Count)
            {
                return false;
            }

            for (int i = 0; i < currentEnabled.Count; i++)
            {
                if (!currentEnabled[i].Equals(last.Mods[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            var logText = LogTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(logText))
            {
                MessageBox.Show("Log is empty.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Clipboard.SetText(logText);
            MessageBox.Show("Log copied to clipboard.", "Info",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveLog_Click(object sender, RoutedEventArgs e)
        {
            var logText = LogTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(logText))
            {
                MessageBox.Show("Log is empty.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                FileName = "IEVR-ActivityLog.txt",
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                DefaultExt = ".txt"
            };

            var result = dialog.ShowDialog();
            if (result == true)
            {
                try
                {
                    File.WriteAllText(dialog.FileName, logText);
                    MessageBox.Show("Log saved successfully.", "Info",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not save the log: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task<bool> EnsureBackupExistsAsync(string gamePath, bool showMessageOnExisting, bool showMessageOnSuccess)
        {
            var gameFolderName = new DirectoryInfo(gamePath).Name;
            var backupRoot = Path.Combine(Config.BackupDir, gameFolderName);

            if (Directory.Exists(backupRoot))
            {
                Log("Backup folder already exists; skipping backup creation.", "info");
                if (showMessageOnExisting)
                {
                    MessageBox.Show("A backup folder already exists in the manager data folder. Delete it if you need to create a new backup.", "Backup already exists",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return true;
            }

            try
            {
                Log("Creating backup...", "info");
                await CreateBackupInternalAsync(gamePath, backupRoot);
                Log($"Backup created successfully. Location: {backupRoot}", "success");
                if (showMessageOnSuccess)
                {
                    MessageBox.Show("Backup created successfully.", "Backup",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log($"Error creating backup: {ex.Message}", "error");
                if (showMessageOnSuccess || showMessageOnExisting)
                {
                    MessageBox.Show($"Error creating backup: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return false;
            }
        }

        private Task CreateBackupInternalAsync(string gamePath, string backupRoot)
        {
            return Task.Run(() =>
            {
                Directory.CreateDirectory(backupRoot);
                CopyTopLevelFiles(gamePath, backupRoot);
                CreateDataBackup(gamePath, backupRoot);
            });
        }

        private void CopyTopLevelFiles(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
        }

        private void CreateDataBackup(string gamePath, string backupRoot)
        {
            var dataPath = Path.Combine(gamePath, "data");
            if (!Directory.Exists(dataPath))
            {
                Log("Game data folder not found; data backup skipped.", "error");
                return;
            }

            var backupDataPath = Path.Combine(backupRoot, "data");
            Directory.CreateDirectory(backupDataPath);

            var cfgSource = Path.Combine(dataPath, "cpk_list.cfg.bin");
            if (File.Exists(cfgSource))
            {
                var cfgDest = Path.Combine(backupDataPath, "cpk_list.cfg.bin");
                Directory.CreateDirectory(Path.GetDirectoryName(cfgDest)!);
                File.Copy(cfgSource, cfgDest, true);
            }
            else
            {
                Log("cpk_list.cfg.bin not found; it was not backed up.", "info");
            }

            var commonSource = Path.Combine(dataPath, "common");
            if (Directory.Exists(commonSource))
            {
                var commonDest = Path.Combine(backupDataPath, "common");
                CopyDirectoryRecursive(commonSource, commonDest, true);
            }
            else
            {
                Log("common folder not found; it was not backed up.", "info");
            }
        }

        private void RestoreDataBackup(string gamePath, string backupRoot)
        {
            var backupDataPath = Path.Combine(backupRoot, "data");
            if (!Directory.Exists(backupDataPath))
            {
                Log("Backup data folder not found; skipping data restore.", "error");
                return;
            }

            var destDataPath = Path.Combine(gamePath, "data");
            Directory.CreateDirectory(destDataPath);

            var cfgBackup = Path.Combine(backupDataPath, "cpk_list.cfg.bin");
            if (File.Exists(cfgBackup))
            {
                var cfgDest = Path.Combine(destDataPath, "cpk_list.cfg.bin");
                File.Copy(cfgBackup, cfgDest, true);
            }
            else
            {
                Log("No cpk_list.cfg.bin found in backup.", "info");
            }

            var commonBackup = Path.Combine(backupDataPath, "common");
            if (Directory.Exists(commonBackup))
            {
                var commonDest = Path.Combine(destDataPath, "common");
                if (Directory.Exists(commonDest))
                {
                    Directory.Delete(commonDest, true);
                }
                CopyDirectoryRecursive(commonBackup, commonDest, true);
            }
            else
            {
                Log("No common folder found in backup.", "info");
            }
        }

        private void CopyDirectoryRecursive(string sourceDir, string destDir, bool overwrite)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite);
            }

            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(destDir, Path.GetFileName(directory));
                CopyDirectoryRecursive(directory, destSubDir, overwrite);
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
                FileName = "https://github.com/Adr1GR/IEVR_Mod_Manager?tab=readme-ov-file#for-users",
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

        private bool CheckReadWriteAccess(string targetFolder, string label)
        {
            try
            {
                var folder = Path.GetFullPath(targetFolder);
                Directory.CreateDirectory(folder);

                var testFile = Path.Combine(folder, ".ievr_access_test.tmp");
                var payload = $"test-{DateTime.UtcNow.Ticks}";
                File.WriteAllText(testFile, payload);
                var readBack = File.ReadAllText(testFile);
                File.Delete(testFile);

                if (!string.Equals(payload, readBack, StringComparison.Ordinal))
                {
                    Log($"Could not verify read/write in {label}: content mismatch.", "error");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log($"Could not access {label}: {ex.Message}", "error");
                return false;
            }
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
        public string ModLink { get; set; } = string.Empty;

        public ModEntryViewModel(ModEntry mod)
        {
            Name = mod.Name;
            Enabled = mod.Enabled;
            DisplayName = mod.DisplayName;
            Author = mod.Author;
            ModVersion = string.IsNullOrEmpty(mod.ModVersion) ? "—" : mod.ModVersion;
            GameVersion = string.IsNullOrEmpty(mod.GameVersion) ? "—" : mod.GameVersion;
            ModLink = mod.ModLink;
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
