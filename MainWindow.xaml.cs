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
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;
using IEVRModManager.Managers;
using IEVRModManager.Models;
using IEVRModManager.Windows;
using IEVRModManager.Helpers;

namespace IEVRModManager
{
    public partial class MainWindow : Window
    {
        private readonly ConfigManager _configManager;
        private readonly ModManager _modManager;
        private readonly LastInstallManager _lastInstallManager;
        private ViolaIntegration _viola;
        private readonly Managers.ProfileManager _profileManager;
        private ObservableCollection<ModEntryViewModel> _modEntries;
        private readonly ObservableCollection<CpkOption> _availableCpkFiles = new();
        private static readonly HttpClient _httpClient = new();
        private AppConfig _config = null!;
        private bool _isApplying;
        private bool _isDownloadingCpkLists;
        private DispatcherTimer? _playResetTimer;
        private DispatcherTimer? _updateCheckTimer;
        private DispatcherTimer? _appUpdateCheckTimer;
        private bool _isCheckingUpdates;
        private bool _isCheckingAppUpdates;
        private bool _vanillaSeedAttempted;
        private string? _previousProfileSelection;
        private static readonly DateTime VanillaFallbackCutoffUtc = new DateTime(2025, 12, 7, 0, 0, 0, DateTimeKind.Utc);
        
        // Constants for CPK file names
        private const string VanillaCpkListFileName = "VanillaCpkList.cfg.bin";
        private const string LatestCpkListFileName = "LatestCpkList.cfg.bin";
        private const string CpkListFileName = "cpk_list.cfg.bin";

        public MainWindow()
        {
            InitializeComponent();
            
            _configManager = new ConfigManager();
            _modManager = new ModManager();
            _lastInstallManager = new LastInstallManager();
            _viola = new ViolaIntegration(message => Log(message, "info"));
            _profileManager = new Managers.ProfileManager();
            _modEntries = new ObservableCollection<ModEntryViewModel>();
            
            ModsListView.ItemsSource = _modEntries;
            CpkSelector.ItemsSource = _availableCpkFiles;
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("IEVRModManager/1.0");
            
            LoadConfig();
            EnsureStorageStructure();
            MigrateLegacyVanillaName(Config.SharedStorageCpkDir);
            RefreshCpkOptions();
            
            // Load last applied profile before scanning mods to preserve its state
            LoadLastAppliedProfileOnStartup();
            
            ScanMods();
            CleanupTempDir();
            
            // Update localized texts after window is loaded
            Loaded += (s, e) => 
            {
                UpdateLocalizedTexts();
                RefreshProfileSelector();
                // Update selector to show the loaded profile
                if (!string.IsNullOrWhiteSpace(_config.LastAppliedProfile))
                {
                    _isRefreshingProfileSelector = true;
                    try
                    {
                        if (ProfileSelector.Items.Contains(_config.LastAppliedProfile))
                        {
                            ProfileSelector.SelectedItem = _config.LastAppliedProfile;
                            _previousProfileSelection = _config.LastAppliedProfile;
                        }
                    }
                    finally
                    {
                        _isRefreshingProfileSelector = false;
                    }
                }
            };
            
            _ = DetectGameUpdateAsync();
            StartUpdateCheckTimer();
            StartAppUpdateCheckTimer();
            _ = DownloadAndRefreshCpkListsAsync();
            _ = GameBananaBrowserWindow.PrefetchModsAsync(this);
        }

        private void LoadConfig()
        {
            _config = _configManager.Load();
        }

        private void UpdateLocalizedTexts()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[Localization] Updating localized texts...");
                
                // Update window title
                var appTitle = LocalizationHelper.GetString("AppTitle");
                Title = appTitle;
                System.Diagnostics.Debug.WriteLine($"[Localization] AppTitle = '{appTitle}'");
                
                // Update title label
                if (TitleLabel != null)
                {
                    TitleLabel.Content = appTitle;
                    System.Diagnostics.Debug.WriteLine("[Localization] Updated TitleLabel");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[Localization] WARNING: TitleLabel is null");
                }
                
                // Update group boxes
                if (ModsGroupBox != null)
                {
                    ModsGroupBox.Header = LocalizationHelper.GetString("Mods");
                    System.Diagnostics.Debug.WriteLine("[Localization] Updated ModsGroupBox");
                }
                if (ActionsGroupBox != null)
                {
                    ActionsGroupBox.Header = LocalizationHelper.GetString("Actions");
                    System.Diagnostics.Debug.WriteLine("[Localization] Updated ActionsGroupBox");
                }
                if (ActivityLogGroupBox != null)
                {
                    ActivityLogGroupBox.Header = LocalizationHelper.GetString("ActivityLog");
                    System.Diagnostics.Debug.WriteLine("[Localization] Updated ActivityLogGroupBox");
                }
                
                // Update buttons
                if (ScanModsButton != null)
                    ScanModsButton.Content = LocalizationHelper.GetString("ScanMods");
                if (OpenModsFolderButton != null)
                    OpenModsFolderButton.Content = LocalizationHelper.GetString("OpenModsFolder");
                if (VersionLabel != null)
                    VersionLabel.Content = LocalizationHelper.GetString("Version");
                if (ApplyButton != null)
                    ApplyButton.Content = LocalizationHelper.GetString("ApplyChanges");
                if (PlayButton != null)
                    PlayButton.Content = LocalizationHelper.GetString("Play");
                if (BrowseModsButton != null)
                    BrowseModsButton.Content = LocalizationHelper.GetString("BrowseMods");
                if (MoveUpButton != null)
                    MoveUpButton.Content = LocalizationHelper.GetString("MoveUp");
                if (MoveDownButton != null)
                    MoveDownButton.Content = LocalizationHelper.GetString("MoveDown");
                if (EnableAllButton != null)
                    EnableAllButton.Content = LocalizationHelper.GetString("EnableAll");
                if (DisableAllButton != null)
                    DisableAllButton.Content = LocalizationHelper.GetString("DisableAll");
                if (ConfigurationButton != null)
                    ConfigurationButton.Content = LocalizationHelper.GetString("Configuration");
                if (LinksButton != null)
                    LinksButton.Content = LocalizationHelper.GetString("Links");
                if (HelpLink != null)
                    HelpLink.Text = LocalizationHelper.GetString("Help");
                if (CopyLogButton != null)
                    CopyLogButton.Content = LocalizationHelper.GetString("CopyLog");
                if (SaveLogButton != null)
                    SaveLogButton.Content = LocalizationHelper.GetString("SaveLog");
                if (ExitButton != null)
                    ExitButton.Content = LocalizationHelper.GetString("Exit");
                if (ManageProfilesButton != null)
                    ManageProfilesButton.Content = LocalizationHelper.GetString("ManageProfiles");
                if (CheckForUpdatesButton != null)
                    CheckForUpdatesButton.Content = LocalizationHelper.GetString("CheckForUpdates");
                
                System.Diagnostics.Debug.WriteLine("[Localization] Finished updating localized texts");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Localization] Error updating localized texts: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[Localization] Stack trace: {ex.StackTrace}");
            }
        }

        private bool _isRefreshingProfileSelector = false;

        private void RefreshProfileSelector()
        {
            if (ProfileSelector == null) return;

            _isRefreshingProfileSelector = true;
            
            try
            {
                var selectedProfile = ProfileSelector.SelectedItem?.ToString();
                ProfileSelector.Items.Clear();
                
                // Add "None" option
                var noProfileText = LocalizationHelper.GetString("NoProfile");
                ProfileSelector.Items.Add(noProfileText);
                
                // Add all profiles
                var profiles = _profileManager.GetAllProfiles();
                foreach (var profile in profiles)
                {
                    ProfileSelector.Items.Add(profile.Name);
                }
                
                // Restore selection if it still exists
                if (!string.IsNullOrEmpty(selectedProfile) && ProfileSelector.Items.Contains(selectedProfile))
                {
                    ProfileSelector.SelectedItem = selectedProfile;
                    _previousProfileSelection = selectedProfile;
                }
                else
                {
                    ProfileSelector.SelectedIndex = 0; // Select "None"
                    _previousProfileSelection = noProfileText;
                }
            }
            finally
            {
                _isRefreshingProfileSelector = false;
            }
        }

        private void ProfileSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Ignore events triggered by programmatic changes
            if (_isRefreshingProfileSelector) return;
            
            if (ProfileSelector?.SelectedItem == null) return;
            
            var selectedProfileName = ProfileSelector.SelectedItem.ToString();
            if (string.IsNullOrEmpty(selectedProfileName) || selectedProfileName == LocalizationHelper.GetString("NoProfile"))
            {
                _previousProfileSelection = selectedProfileName;
                return;
            }

            // Prevent loading the same profile twice
            if (selectedProfileName == _previousProfileSelection)
            {
                return;
            }

            var profile = _profileManager.LoadProfile(selectedProfileName);
            if (profile != null)
            {
                _previousProfileSelection = selectedProfileName;
                LoadProfile(profile);
                // Refresh to update the selector, but don't trigger selection change
                _isRefreshingProfileSelector = true;
                try
                {
                    ProfileSelector.SelectedItem = selectedProfileName;
                }
                finally
                {
                    _isRefreshingProfileSelector = false;
                }
            }
            else
            {
                // Profile not found, restore previous selection
                _isRefreshingProfileSelector = true;
                try
                {
                    ProfileSelector.SelectedItem = _previousProfileSelection ?? LocalizationHelper.GetString("NoProfile");
                }
                finally
                {
                    _isRefreshingProfileSelector = false;
                }
            }
        }

        private void LoadLastAppliedProfileOnStartup()
        {
            if (string.IsNullOrWhiteSpace(_config.LastAppliedProfile))
            {
                return;
            }

            var profile = _profileManager.LoadProfile(_config.LastAppliedProfile);
            if (profile != null)
            {
                // Update config with profile mods before ScanMods runs
                // This ensures ScanMods uses the profile's mod state
                _config.Mods = profile.Mods;
                
                if (!string.IsNullOrWhiteSpace(profile.SelectedCpkName))
                {
                    _config.SelectedCpkName = profile.SelectedCpkName;
                }
                
                Log($"Last applied profile '{profile.Name}' will be loaded on startup.", "info");
            }
            else
            {
                // Profile not found, clear the saved profile name
                _config.LastAppliedProfile = string.Empty;
            }
        }

        private void LoadLastAppliedProfile()
        {
            if (string.IsNullOrWhiteSpace(_config.LastAppliedProfile))
            {
                return;
            }

            var profile = _profileManager.LoadProfile(_config.LastAppliedProfile);
            if (profile != null)
            {
                // Load profile silently (without showing confirmation)
                var profileModMap = profile.Mods.ToDictionary(m => m.Name, m => m.Enabled);
                
                foreach (var modEntry in _modEntries)
                {
                    if (profileModMap.ContainsKey(modEntry.Name))
                    {
                        modEntry.Enabled = profileModMap[modEntry.Name];
                    }
                    else
                    {
                        modEntry.Enabled = false;
                    }
                }
                
                if (!string.IsNullOrWhiteSpace(profile.SelectedCpkName))
                {
                    _config.SelectedCpkName = profile.SelectedCpkName;
                    RefreshCpkOptions();
                }
                
                SaveConfig();
                RefreshProfileSelector();
                
                // Set the selector to show the loaded profile
                _isRefreshingProfileSelector = true;
                try
                {
                    if (ProfileSelector.Items.Contains(profile.Name))
                    {
                        ProfileSelector.SelectedItem = profile.Name;
                        _previousProfileSelection = profile.Name;
                    }
                }
                finally
                {
                    _isRefreshingProfileSelector = false;
                }
                
                Log($"Last applied profile '{profile.Name}' loaded on startup.", "info");
            }
            else
            {
                // Profile not found, clear the saved profile name
                _config.LastAppliedProfile = string.Empty;
                SaveConfig();
            }
        }

        private void ManageProfilesButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new Windows.ProfileManagerWindow(this);
            var result = window.ShowDialog();
            
            if (result == true && window.ProfileLoaded && window.SelectedProfile != null)
            {
                LoadProfile(window.SelectedProfile);
            }
            
            RefreshProfileSelector();
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
                MigrateLegacyVanillaName(Config.SharedStorageCpkDir);
                MigrateVanillaToLatestCpkName();

                var cpkFiles = GetAvailableCpkFiles();
                PopulateCpkOptions(cpkFiles);
                SelectPreferredCpkFile(cpkFiles);
            }
            catch (Exception ex)
            {
                Log($"Error refreshing cpk list: {ex.Message}", "error");
            }
        }

        private void MigrateVanillaToLatestCpkName()
        {
            if (string.Equals(_config.SelectedCpkName, VanillaCpkListFileName, StringComparison.OrdinalIgnoreCase))
            {
                _config.SelectedCpkName = LatestCpkListFileName;
                if (!string.IsNullOrWhiteSpace(_config.CfgBinPath) &&
                    _config.CfgBinPath.EndsWith(VanillaCpkListFileName, StringComparison.OrdinalIgnoreCase))
                {
                    _config.CfgBinPath = Path.Combine(Config.SharedStorageCpkDir, LatestCpkListFileName);
                }
            }
        }

        private List<string> GetAvailableCpkFiles()
        {
            var cpkDir = Config.SharedStorageCpkDir;
            if (!Directory.Exists(cpkDir))
            {
                return new List<string>();
            }

            return Directory.GetFiles(cpkDir, "*.bin", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(cpkDir, "*.cfg.bin", SearchOption.TopDirectoryOnly))
                .Where(f => !string.IsNullOrWhiteSpace(Path.GetFileName(f)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(Path.GetFileName)
                .ToList();
        }

        private void PopulateCpkOptions(List<string> files)
        {
            _availableCpkFiles.Clear();
            foreach (var file in files)
            {
                _availableCpkFiles.Add(new CpkOption { FileName = Path.GetFileName(file) });
            }
        }

        private void SelectPreferredCpkFile(List<string> files)
        {
            if (_availableCpkFiles.Count == 0)
            {
                ClearCpkSelection();
                return;
            }

            var preferred = GetPreferredCpkName();
            if (!string.IsNullOrWhiteSpace(preferred) && _availableCpkFiles.Any(o => o.FileName == preferred))
            {
                SetSelectedCpkFile(preferred);
            }
            else if (_availableCpkFiles.Count > 0)
            {
                SetSelectedCpkFile(_availableCpkFiles[0].FileName);
            }
            else
            {
                ClearCpkSelection();
            }
        }

        private string GetPreferredCpkName()
        {
            var preferred = _config.SelectedCpkName;
            if (string.IsNullOrWhiteSpace(preferred) && !string.IsNullOrWhiteSpace(_config.CfgBinPath))
            {
                preferred = Path.GetFileName(_config.CfgBinPath);
            }
            return preferred ?? string.Empty;
        }

        private void SetSelectedCpkFile(string fileName)
        {
            CpkSelector.SelectedValue = fileName;
            _config.SelectedCpkName = fileName;
            _config.CfgBinPath = Path.Combine(Config.SharedStorageCpkDir, fileName);
        }

        private void ClearCpkSelection()
        {
            CpkSelector.SelectedIndex = -1;
            _config.SelectedCpkName = string.Empty;
            _config.CfgBinPath = string.Empty;
        }

        public ModProfile CreateProfileFromCurrentState(string profileName)
        {
            var modData = _modEntries.Select(me => new ModData
            {
                Name = me.Name,
                Enabled = me.Enabled,
                ModLink = me.ModLink
            }).ToList();

            return new ModProfile
            {
                Name = profileName,
                Mods = modData,
                SelectedCpkName = _config.SelectedCpkName
            };
        }

        public void LoadProfile(ModProfile profile)
        {
            // Create a map of profile mods
            var profileModMap = profile.Mods.ToDictionary(m => m.Name, m => m.Enabled);
            
            // Update enabled state and preserve order
            foreach (var modEntry in _modEntries)
            {
                if (profileModMap.ContainsKey(modEntry.Name))
                {
                    modEntry.Enabled = profileModMap[modEntry.Name];
                }
                else
                {
                    // Disable mods not in profile
                    modEntry.Enabled = false;
                }
            }
            
            // Update selected CPK if profile has one
            if (!string.IsNullOrWhiteSpace(profile.SelectedCpkName))
            {
                _config.SelectedCpkName = profile.SelectedCpkName;
                RefreshCpkOptions();
            }
            
            // Save the profile name as last applied
            _config.LastAppliedProfile = profile.Name;
            
            SaveConfig();
            RefreshProfileSelector();
            Log($"Profile '{profile.Name}' loaded.", "info");
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
                }).ToList(),
                _config.LastKnownPacksSignature,
                _config.LastKnownSteamBuildId,
                _config.VanillaFallbackUntilUtc,
                _config.Theme,
                _config.Language,
                _config.LastAppliedProfile
            );

            if (!success)
            {
                ShowError("CouldNotSaveConfiguration");
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
                Log($"No {CpkListFileName} selected.", "error");
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
                ShowError("NoViolaExecutableFound");
                return null;
            }

            if (executables.Length > 1)
            {
                ShowError("MultipleViolaExecutables");
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

        private async Task DownloadAndRefreshCpkListsAsync()
        {
            if (_isDownloadingCpkLists)
            {
                Log("Already downloading cpk_list files, please wait.", "info");
                return;
            }

            _isDownloadingCpkLists = true;

            try
            {
                Log("Fetching available cpk_list files from GitHub...", "info");
                var downloaded = await DownloadCpkFilesAsync();
                if (downloaded > 0)
                {
                    Log($"Downloaded {downloaded} cpk_list file(s).", "success");
                }
                else
                {
                    Log("No new cpk_list files downloaded (they may already exist).", "info");
                }

                await EnsureInitialVanillaSeedAsync();
                
                // Ensure LatestCpkList.cfg.bin exists after downloading
                await EnsureLatestCpkExistsAsync();
                
                RefreshCpkOptions();
            }
            catch (Exception ex)
            {
                Log($"Error downloading cpk_list files: {ex.Message}", "error");
            }
            finally
            {
                _isDownloadingCpkLists = false;
            }
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
                    ShowError("InvalidModLink");
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
                    ShowError("CouldNotOpenLink", ex.Message);
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
                await DownloadAndRefreshCpkListsAsync();
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
            await RunCreateBackupFlowAsync();
        }

        private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            await RunRestoreBackupFlowAsync();
        }

        private async Task RunCreateBackupFlowAsync()
        {
            if (_viola.IsRunning || _isApplying)
            {
                ShowInfo("PleaseWaitOperation");
                return;
            }

            var gamePath = _config.GamePath;
            if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
            {
                ShowError("InvalidGamePath");
                return;
            }

            // Show confirmation dialog
            var confirmWindow = new Windows.BackupConfirmationWindow(this,
                Helpers.LocalizationHelper.GetString("CreateBackupConfirmMessage"), false);
            var result = confirmWindow.ShowDialog();
            if (result != true || !confirmWindow.UserConfirmed)
            {
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
                ShowError("ErrorCreatingBackup", ex.Message);
            }
            finally
            {
                SetApplyButtonEnabled(true);
            }
        }

        private async Task RunRestoreBackupFlowAsync()
        {
            if (_viola.IsRunning || _isApplying)
            {
                ShowInfo("PleaseWaitOperation");
                return;
            }

            var gamePath = _config.GamePath;
            if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
            {
                ShowError("InvalidGamePath");
                return;
            }

            var gameFolderName = new DirectoryInfo(gamePath).Name;
            var backupRoot = Path.Combine(Config.BackupDir, gameFolderName);
            if (!Directory.Exists(backupRoot))
            {
                ShowMessage("BackupNotFoundTitle", "NoBackupFolderFound", Windows.MessageType.Warning);
                Log("No backup folder found; cannot restore.", "error");
                return;
            }

            // Show confirmation dialog
            var confirmWindow = new Windows.BackupConfirmationWindow(this,
                Helpers.LocalizationHelper.GetString("RestoreBackupConfirmMessage"), true);
            var result = confirmWindow.ShowDialog();
            if (result != true || !confirmWindow.UserConfirmed)
            {
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
                ShowMessage("BackupTitle", "BackupRestoredSuccessfully", Windows.MessageType.Success);
            }
            catch (Exception ex)
            {
                Log($"Error restoring backup: {ex.Message}", "error");
                ShowError("ErrorRestoringBackup", ex.Message);
            }
            finally
            {
                SetApplyButtonEnabled(true);
            }
        }

        private async void ApplyMods_Click(object sender, RoutedEventArgs e)
        {
            if (_viola.IsRunning || _isApplying)
            {
                ShowInfo("ProcessAlreadyRunning");
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
                    ShowError("InvalidGamePath");
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
                    ShowError("NoCpkListSelected");
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
                    ShowError("InvalidGamePathNoData");
                    return;
                }

                // Quick read/write checks so we fail fast on permission issues
                if (!CheckReadWriteAccess(gameDataPath, "game data folder"))
                {
                    ShowError("CouldNotWriteGameFolder");
                    return;
                }

                if (!CheckReadWriteAccess(tmpRoot, "temporary folder"))
                {
                    ShowError("CouldNotWriteTempFolder");
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
                    var targetCpk = Path.Combine(gameDataPath, CpkListFileName);
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
                        var successWindow = new SuccessMessageWindow(this, LocalizationHelper.GetString("OriginalGameFilesRestored"));
                        successWindow.ShowDialog();

                        _lastInstallManager.Save(new LastInstallInfo
                        {
                            GamePath = Path.GetFullPath(_config.GamePath),
                            Files = new List<string>(),
                            Mods = new List<string>(),
                            AppliedAt = DateTime.UtcNow,
                            SelectedCpkName = _config.SelectedCpkName ?? string.Empty,
                            SelectedCpkInfo = GetCpkInfo(_config.CfgBinPath)
                        });
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

                // Create and show progress window
                ProgressWindow? progressWindow = null;
                Dispatcher.Invoke(() =>
                {
                    progressWindow = new ProgressWindow(this);
                    progressWindow.Show();
                });

                try
                {
                    // Create ViolaIntegration with progress callback
                    var progressCallback = new Action<int, string>((percentage, status) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            // Translate status message if it's a translation key
                            string translatedStatus = TranslateProgressMessage(status);
                            progressWindow?.UpdateProgress(percentage, translatedStatus);
                        });
                    });

                    var violaWithProgress = new ViolaIntegration(
                        message => Log(message, "info"),
                        progressCallback);

                    // Run merge in background
                    await Task.Run(async () =>
                    {
                        await RunMergeAndCopyWithProgress(violaWithProgress, violaExePath, selectedCpkPath, 
                            enabledMods, tmpRoot, _config.GamePath, lastInstall, progressCallback, progressWindow);
                    });
                }
                finally
                {
                    // Ensure progress window is closed
                    Dispatcher.Invoke(() =>
                    {
                        if (progressWindow != null && progressWindow.IsLoaded)
                        {
                            progressWindow.AllowClose();
                            progressWindow = null;
                        }
                    });
                }
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
                ShowInfo("PleaseWaitOperation");
                return;
            }

            if (string.IsNullOrWhiteSpace(_config.GamePath) || !Directory.Exists(_config.GamePath))
            {
                ShowError("SelectGamePathBeforePlay");
                return;
            }

            if (!ModsMatchLastInstall())
            {
                var window = new PendingChangesWindow(this,
                    Helpers.LocalizationHelper.GetString("PendingChangesModsMessage"));
                var result = window.ShowDialog();
                if (result != true || !window.UserChoseContinue)
                {
                    return;
                }
            }

            if (!VersionMatchesLastInstall())
            {
                var window = new PendingChangesWindow(this,
                    Helpers.LocalizationHelper.GetString("PendingChangesVersionMessage"));
                var result = window.ShowDialog();
                if (result != true || !window.UserChoseContinue)
                {
                    return;
                }
            }

            var exePath = Path.Combine(_config.GamePath, "nie.exe");
            if (!File.Exists(exePath))
            {
                ShowError("NieExeNotFound");
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
                
                // Minimize window after launching game (only if no alerts were shown)
                WindowState = WindowState.Minimized;
            }
            catch (Exception ex)
            {
                SetPlayLoadingState(false);
                ShowError("CouldNotStartGame", ex.Message);
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

        private bool VersionMatchesLastInstall()
        {
            var last = _lastInstallManager.Load();
            if (!PathsMatch(last.GamePath, _config.GamePath))
            {
                // Different game path or never applied -> treat as mismatch so user is warned
                return false;
            }

            var currentCpkName = _config.SelectedCpkName ?? string.Empty;
            var currentCpkInfo = GetCpkInfo(_config.CfgBinPath);

            return string.Equals(currentCpkName, last.SelectedCpkName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(currentCpkInfo, last.SelectedCpkInfo, StringComparison.Ordinal);
        }

        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            var logText = LogTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(logText))
            {
                ShowInfo("LogIsEmpty");
                return;
            }

            Clipboard.SetText(logText);
            ShowSuccess("LogCopiedToClipboard");
        }

        private void SaveLog_Click(object sender, RoutedEventArgs e)
        {
            var logText = LogTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(logText))
            {
                ShowInfo("LogIsEmpty");
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
                    ShowSuccess("LogSavedSuccessfully");
                }
                catch (Exception ex)
                {
                    ShowError("CouldNotSaveLog", ex.Message);
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
                    ShowMessage("BackupAlreadyExistsTitle", "BackupAlreadyExistsMessage", Windows.MessageType.Info);
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
                    ShowMessage("BackupTitle", "BackupCreatedSuccessfully", Windows.MessageType.Success);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log($"Error creating backup: {ex.Message}", "error");
                if (showMessageOnSuccess || showMessageOnExisting)
                {
                    ShowError("ErrorCreatingBackup", ex.Message);
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

            var cfgSource = Path.Combine(dataPath, CpkListFileName);
            if (File.Exists(cfgSource))
            {
                var cfgDest = Path.Combine(backupDataPath, CpkListFileName);
                Directory.CreateDirectory(Path.GetDirectoryName(cfgDest)!);
                File.Copy(cfgSource, cfgDest, true);
            }
            else
            {
                Log($"{CpkListFileName} not found; it was not backed up.", "info");
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

            var cfgBackup = Path.Combine(backupDataPath, CpkListFileName);
            if (File.Exists(cfgBackup))
            {
                var cfgDest = Path.Combine(destDataPath, CpkListFileName);
                File.Copy(cfgBackup, cfgDest, true);
            }
            else
            {
                Log($"No {CpkListFileName} found in backup.", "info");
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

        private async Task RunMergeAndCopyWithProgress(ViolaIntegration viola, string violaCli, string cfgBin, 
            List<string> modPaths, string tmpRoot, string gamePath, LastInstallInfo lastInstall, Action<int, string> progressCallback, ProgressWindow? progressWindow)
        {
            try
            {
                // Merge mods
                var success = await viola.MergeModsAsync(violaCli, cfgBin, modPaths, tmpRoot);
                
                if (!success)
                {
                    Dispatcher.Invoke(() => Log("violacli returned error; aborting copy.", "error"));
                    progressCallback(0, LocalizationHelper.GetString("ErrorDuringMergeAborting"));
                    return;
                }

                // Copy merged files
                var tmpData = Path.Combine(tmpRoot, "data");
                var destData = Path.Combine(gamePath, "data");

                var mergedFiles = GetRelativeFiles(tmpData);
                var mergedSet = new HashSet<string>(mergedFiles, StringComparer.OrdinalIgnoreCase);

                if (viola.CopyMergedFiles(tmpData, destData))
                {
                    progressCallback(90, LocalizationHelper.GetString("CleaningUpTemporaryFiles"));
                    viola.CleanupTemp(tmpRoot);
                    
                    progressCallback(95, LocalizationHelper.GetString("RemovingObsoleteFiles"));
                    var removed = RemoveObsoleteFiles(lastInstall, destData, mergedSet);
                    if (removed > 0)
                    {
                        Dispatcher.Invoke(() => Log($"Removed {removed} leftover file(s) from previous install.", "info"));
                    }
                    
                    progressCallback(100, LocalizationHelper.GetString("ModsAppliedSuccessfully"));
                    Dispatcher.Invoke(() => Log("MODS APPLIED!!", "success"));
                    
                    // Close progress window after showing completion message
                    await Task.Delay(500);
                    Dispatcher.Invoke(() =>
                    {
                        if (progressWindow != null && progressWindow.IsLoaded)
                        {
                            progressWindow.AllowClose();
                        }
                    });
                    
                    // Small delay to ensure window closes before showing success dialog
                    await Task.Delay(200);
                    
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
                        ? LocalizationHelper.GetString("OneModAppliedSuccessfully")
                        : string.Format(LocalizationHelper.GetString("ModsAppliedSuccessfullyFormat"), modCount);
                    
                    _lastInstallManager.Save(new LastInstallInfo
                    {
                        GamePath = Path.GetFullPath(gamePath),
                        Files = mergedFiles,
                        Mods = modNames,
                        AppliedAt = DateTime.UtcNow,
                        SelectedCpkName = _config.SelectedCpkName ?? string.Empty,
                        SelectedCpkInfo = GetCpkInfo(_config.CfgBinPath)
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
                    progressCallback(0, LocalizationHelper.GetString("FailedToCopyMergedFiles"));
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => Log($"Unexpected error: {ex.Message}", "error"));
                progressCallback(0, $"UnexpectedError:{ex.Message}");
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
                ShowInfo("PathDoesNotExist", path);
            }
        }

        private void HelpLink_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBlock textBlock)
            {
                textBlock.TextDecorations = System.Windows.TextDecorations.Underline;
            }
        }

        private void HelpLink_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBlock textBlock)
            {
                textBlock.TextDecorations = null;
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

        public void LogMessage(string message, string level = "info")
        {
            Log(message, level);
        }

        private void OpenGameBananaBrowser_Click(object sender, RoutedEventArgs e)
        {
            var window = new GameBananaBrowserWindow(this);
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
                Theme = _config.Theme,
                Language = _config.Language,
                Mods = _config.Mods
            };
            
            var window = new ConfigPathsWindow(
                this,
                configCopy,
                () =>
                {
                    // Save when something changes
                    _config.GamePath = configCopy.GamePath;
                    _config.CfgBinPath = configCopy.CfgBinPath;
                    _config.SelectedCpkName = configCopy.SelectedCpkName;
                    _config.ViolaCliPath = configCopy.ViolaCliPath;
                    _config.Theme = configCopy.Theme;
                    _config.Language = configCopy.Language;
                    SaveConfig();
                },
                RunCreateBackupFlowAsync,
                RunRestoreBackupFlowAsync);
            
            window.ShowDialog();
            
            // Ensure final values are saved
            _config.GamePath = configCopy.GamePath;
            _config.CfgBinPath = configCopy.CfgBinPath;
            _config.SelectedCpkName = configCopy.SelectedCpkName;
            _config.ViolaCliPath = configCopy.ViolaCliPath;
            _config.Theme = configCopy.Theme;
            
            // Check if language changed
            bool languageChanged = _config.Language != configCopy.Language;
            _config.Language = configCopy.Language;
            
            // Apply language change if it changed
            if (languageChanged)
            {
                LocalizationHelper.SetLanguage(_config.Language);
                UpdateLocalizedTexts();
            }
            
            RefreshCpkOptions();
            SaveConfig();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            if (_viola.IsRunning)
            {
                var resultWindow = new Windows.MessageWindow(
                    this,
                    LocalizationHelper.GetString("ExitTitle"),
                    LocalizationHelper.GetString("ExitConfirmationMessage"),
                    Windows.MessageType.Warning,
                    Windows.MessageButtons.YesNo);
                
                var result = resultWindow.ShowDialog();
                if (result != true || resultWindow.Result != true)
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
                const int maxLogLines = 1000;
                var lines = LogTextBox.LineCount;
                if (lines > maxLogLines)
                {
                    var startIndex = LogTextBox.GetCharacterIndexFromLineIndex(lines - maxLogLines);
                    LogTextBox.Text = LogTextBox.Text.Substring(startIndex);
                }
                
                LogTextBox.AppendText(formattedMessage);
                LogTextBox.CaretIndex = LogTextBox.Text.Length;
                LogTextBox.ScrollToEnd();
            });
        }

        // Helper methods for showing message windows
        private void ShowMessage(string titleKey, string messageKey, Windows.MessageType messageType)
        {
            var window = new Windows.MessageWindow(this,
                LocalizationHelper.GetString(titleKey),
                LocalizationHelper.GetString(messageKey),
                messageType);
            window.ShowDialog();
        }

        private void ShowMessage(string titleKey, string message, Windows.MessageType messageType, bool useMessageDirectly = false)
        {
            var title = LocalizationHelper.GetString(titleKey);
            var window = new Windows.MessageWindow(this, title, message, messageType);
            window.ShowDialog();
        }

        private void ShowError(string messageKey, params object[] args)
        {
            var message = args.Length > 0
                ? string.Format(LocalizationHelper.GetString(messageKey), args)
                : LocalizationHelper.GetString(messageKey);
            ShowMessage("ErrorTitle", message, Windows.MessageType.Error, true);
        }

        private void ShowInfo(string messageKey, params object[] args)
        {
            var message = args.Length > 0
                ? string.Format(LocalizationHelper.GetString(messageKey), args)
                : LocalizationHelper.GetString(messageKey);
            ShowMessage("InfoTitle", message, Windows.MessageType.Info, true);
        }

        private void ShowSuccess(string messageKey, params object[] args)
        {
            var message = args.Length > 0
                ? string.Format(LocalizationHelper.GetString(messageKey), args)
                : LocalizationHelper.GetString(messageKey);
            ShowMessage("InfoTitle", message, Windows.MessageType.Success, true);
        }

        private void ShowWarning(string messageKey, params object[] args)
        {
            var message = args.Length > 0
                ? string.Format(LocalizationHelper.GetString(messageKey), args)
                : LocalizationHelper.GetString(messageKey);
            ShowMessage("InfoTitle", message, Windows.MessageType.Warning, true);
        }

        private async Task DetectGameUpdateAsync()
        {
            if (string.IsNullOrWhiteSpace(_config.GamePath) || !Directory.Exists(_config.GamePath))
            {
                return;
            }

            // During the fixed 12h window we do NOT check for updates
            if (IsFallbackWindowActive())
            {
                return;
            }

            try
            {
                var latestBuildId = await GetLatestSteamBuildIdAsync();
                var signature = await Task.Run(() => ComputeGameSignature(_config.GamePath));

                var buildChanged = !string.IsNullOrWhiteSpace(latestBuildId) &&
                    !string.Equals(latestBuildId, _config.LastKnownSteamBuildId, StringComparison.OrdinalIgnoreCase);
                var signatureChanged = !string.IsNullOrWhiteSpace(signature) &&
                    !string.Equals(signature, _config.LastKnownPacksSignature, StringComparison.Ordinal);

                // Require BOTH: new Steam build AND changed packs signature
                if (!buildChanged || !signatureChanged)
                {
                    return;
                }

                var cpkPath = Path.Combine(_config.GamePath, "data", "cpk_list.cfg.bin");
                if (File.Exists(cpkPath))
                {
                    await BackupVanillaCpkAsync(cpkPath, latestBuildId);
                }
                else
                {
                    Log("Possible game update detected, but data/cpk_list.cfg.bin was not found.", "error");
                }

                if (!string.IsNullOrWhiteSpace(latestBuildId))
                {
                    _config.LastKnownSteamBuildId = latestBuildId;
                }

                _config.LastKnownPacksSignature = signature;
                SaveConfig();

                // Show popup recommending backup after update detection
                await Dispatcher.InvokeAsync(async () =>
                {
                    var message = LocalizationHelper.GetString("UpdateDetectedBackupMessage");
                    var updateWindow = new Windows.UpdateDetectedWindow(this, message);
                    var result = updateWindow.ShowDialog();
                    
                    if (result == true && updateWindow.UserWantsBackup)
                    {
                        // User wants to create backup
                        await RunCreateBackupFlowAsync();
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"Could not check for game updates: {ex.Message}", "error");
            }
        }

        private void StartUpdateCheckTimer()
        {
            _updateCheckTimer?.Stop();
            _updateCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(10)
            };

            // Run once at startup (when the timer is created)
            _ = RunGuardedUpdateCheckAsync();

            _updateCheckTimer.Tick += async (_, _) =>
            {
                await RunGuardedUpdateCheckAsync();
            };

            _updateCheckTimer.Start();
        }

        private async Task RunGuardedUpdateCheckAsync()
        {
            if (_isCheckingUpdates)
            {
                return;
            }

            _isCheckingUpdates = true;
            try
            {
                await DetectGameUpdateAsync();
            }
            finally
            {
                _isCheckingUpdates = false;
            }
        }

        private async Task<string?> GetLatestSteamBuildIdAsync()
        {
            try
            {
                const string rssUrl = "https://steamdb.info/api/PatchnotesRSS/?appid=2799860";
                using var response = await _httpClient.GetAsync(rssUrl);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync();
                var doc = XDocument.Load(stream);
                var item = doc.Descendants("item").FirstOrDefault();
                if (item == null)
                {
                    return null;
                }

                var guid = item.Element("guid")?.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(guid))
                {
                    return guid;
                }

                var title = item.Element("title")?.Value?.Trim();
                return string.IsNullOrWhiteSpace(title) ? null : title;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SteamDB RSS fetch failed: {ex.Message}");
                return null;
            }
        }

        private async Task BackupVanillaCpkAsync(string cpkPath, string? latestBuildId)
        {
            try
            {
                var targetDir = Config.SharedStorageCpkDir;
                Directory.CreateDirectory(targetDir);

                MigrateLegacyVanillaName(targetDir);

                var targetPath = Path.Combine(targetDir, LatestCpkListFileName);

                var useFallback = IsFallbackWindowActive() && TryCopyFallbackVanilla(targetPath);
                if (!useFallback)
                {
                    File.Copy(cpkPath, targetPath, true);
                }

                var sourceLabel = useFallback ? "bundled 1_4_2 cpk_list" : $"game data/{CpkListFileName}";
                Log($"Detected game update{(string.IsNullOrWhiteSpace(latestBuildId) ? string.Empty : $" ({latestBuildId})")}. Copied {sourceLabel} to {LatestCpkListFileName}.", "success");
                RefreshCpkOptions();
            }
            catch (Exception ex)
            {
                Log($"Could not copy the updated {CpkListFileName}: {ex.Message}", "error");
            }
        }

        private async Task EnsureInitialVanillaSeedAsync()
        {
            if (_vanillaSeedAttempted)
            {
                return;
            }

            _vanillaSeedAttempted = true;

            if (!IsFallbackWindowActive())
            {
                return;
            }

            var targetDir = Config.SharedStorageCpkDir;
            Directory.CreateDirectory(targetDir);
            MigrateLegacyVanillaName(targetDir);
            var targetPath = Path.Combine(targetDir, LatestCpkListFileName);

            var source = ResolveDownloaded142Cpk() ?? ResolveBundled142Cpk();
            if (string.IsNullOrWhiteSpace(source))
            {
                Log("Could not find 1_4_2_cpk_list.cfg.bin to seed Latest. It will be created from the game when available.", "error");
                return;
            }

            try
            {
                File.Copy(source, targetPath, true);
                Log("Seeded LatestCpkList.cfg.bin from 1_4_2_cpk_list.cfg.bin for the next 12 hours.", "success");
            }
            catch (Exception ex)
            {
                Log($"Could not seed LatestCpkList.cfg.bin from 1_4_2: {ex.Message}", "error");
            }
        }

        private async Task EnsureLatestCpkExistsAsync()
        {
            var targetDir = Config.SharedStorageCpkDir;
            Directory.CreateDirectory(targetDir);
            MigrateLegacyVanillaName(targetDir);
            var targetPath = Path.Combine(targetDir, LatestCpkListFileName);

            // If LatestCpkList.cfg.bin already exists, no need to create it
            if (File.Exists(targetPath))
            {
                return;
            }

            // Try to find the most recent cpk file to use as Latest
            try
            {
                var cpkFiles = Directory.GetFiles(targetDir, "*.cfg.bin", SearchOption.TopDirectoryOnly)
                    .Where(f => !Path.GetFileName(f).Equals(LatestCpkListFileName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .ToList();

                if (cpkFiles.Count == 0)
                {
                    // Try to use 1_4_2 as fallback
                    var source142 = ResolveDownloaded142Cpk() ?? ResolveBundled142Cpk();
                    if (!string.IsNullOrWhiteSpace(source142) && File.Exists(source142))
                    {
                        File.Copy(source142, targetPath, true);
                        Log("Created LatestCpkList.cfg.bin from 1_4_2_cpk_list.cfg.bin.", "info");
                        return;
                    }
                    return;
                }

                // Use the most recent file
                var mostRecent = cpkFiles.First();
                File.Copy(mostRecent, targetPath, true);
                Log($"Created LatestCpkList.cfg.bin from {Path.GetFileName(mostRecent)}.", "info");
            }
            catch (Exception ex)
            {
                Log($"Could not create LatestCpkList.cfg.bin: {ex.Message}", "error");
            }
        }

        private void MigrateLegacyVanillaName(string targetDir)
        {
            try
            {
                var legacyPath = Path.Combine(targetDir, VanillaCpkListFileName);
                var latestPath = Path.Combine(targetDir, LatestCpkListFileName);

                if (File.Exists(legacyPath))
                {
                    if (File.Exists(latestPath))
                    {
                        File.Delete(latestPath);
                    }
                    File.Move(legacyPath, latestPath);
                    Log($"Renamed {VanillaCpkListFileName} to {LatestCpkListFileName}.", "info");
                }
            }
            catch (Exception ex)
            {
                Log($"Could not rename {VanillaCpkListFileName}: {ex.Message}", "error");
            }
        }

        private string? ResolveBundled142Cpk()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var bundled = Path.Combine(baseDir, "cpk_list", "1_4_2_cpk_list.cfg.bin");
                return File.Exists(bundled) ? bundled : null;
            }
            catch
            {
                return null;
            }
        }

        private string? ResolveDownloaded142Cpk()
        {
            try
            {
                var storageDir = Config.SharedStorageCpkDir;
                Directory.CreateDirectory(storageDir);

                var exact = Path.Combine(storageDir, "1_4_2_cpk_list.cfg.bin");
                if (File.Exists(exact))
                {
                    return exact;
                }

                var candidates = Directory.GetFiles(storageDir, "*1_4_2*.cfg.bin", SearchOption.TopDirectoryOnly)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return candidates.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private bool TryCopyFallbackVanilla(string targetPath)
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var fallbackPath = Path.Combine(baseDir, "cpk_list", "1_4_2_cpk_list.cfg.bin");
                if (!File.Exists(fallbackPath))
                {
                    return false;
                }

                File.Copy(fallbackPath, targetPath, true);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fallback vanilla copy failed: {ex.Message}");
                return false;
            }
        }

        private static string ComputeGameSignature(string gamePath)
        {
            // Only rely on data/packs contents to avoid false positives
            return ComputePacksSignature(gamePath);
        }

        private static bool IsFallbackWindowActive()
        {
            return DateTime.UtcNow < VanillaFallbackCutoffUtc;
        }

        private static string GetCpkInfo(string? cpkPath)
        {
            if (string.IsNullOrWhiteSpace(cpkPath) || !File.Exists(cpkPath))
            {
                return "missing";
            }

            var info = new FileInfo(cpkPath);
            return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        }

        private static string ComputePacksSignature(string gamePath)
        {
            var packsPath = Path.Combine(gamePath, "data", "packs");
            if (!Directory.Exists(packsPath))
            {
                return string.Empty;
            }

            var files = Directory.GetFiles(packsPath, "*", SearchOption.AllDirectories);
            if (files.Length == 0)
            {
                return string.Empty;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            using var sha = SHA256.Create();
            foreach (var file in files)
            {
                var info = new FileInfo(file);
                var relative = Path.GetRelativePath(packsPath, file).Replace('\\', '/');
                var line = $"{relative}|{info.Length}|{info.LastWriteTimeUtc.Ticks}\n";
                var bytes = Encoding.UTF8.GetBytes(line);
                sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
            }

            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToHexString(sha.Hash ?? Array.Empty<byte>());
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

        private void StartAppUpdateCheckTimer()
        {
            _appUpdateCheckTimer?.Stop();
            _appUpdateCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromHours(24) // Check once per day
            };

            // Run once at startup (after a delay to not interfere with initial loading)
            var startupTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            startupTimer.Tick += async (_, _) =>
            {
                startupTimer.Stop();
                await CheckForAppUpdatesAsync(false);
            };
            startupTimer.Start();

            _appUpdateCheckTimer.Tick += async (_, _) =>
            {
                await CheckForAppUpdatesAsync(false);
            };

            _appUpdateCheckTimer.Start();
        }

        private async Task CheckForAppUpdatesAsync(bool showNoUpdateMessage = false)
        {
            if (_isCheckingAppUpdates)
            {
                return;
            }

            _isCheckingAppUpdates = true;
            try
            {
                var currentVersion = Managers.AppUpdateManager.GetCurrentVersion();
                var releaseInfo = await Managers.AppUpdateManager.CheckForUpdatesAsync();

                if (releaseInfo == null)
                {
                    if (showNoUpdateMessage)
                    {
                        ShowError("UpdateCheckFailed");
                    }
                    return;
                }

                var latestVersion = releaseInfo.TagName.TrimStart('v', 'V');
                var isNewer = Managers.AppUpdateManager.IsNewerVersion(currentVersion, latestVersion);

                if (isNewer)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        var updateWindow = new Windows.AppUpdateWindow(this, releaseInfo, currentVersion);
                        updateWindow.ShowDialog();
                    });
                }
                else if (showNoUpdateMessage)
                {
                    ShowInfo("NoUpdatesAvailable");
                }
            }
            catch (Exception ex)
            {
                Log($"Error checking for app updates: {ex.Message}", "error");
                if (showNoUpdateMessage)
                {
                    ShowError("UpdateCheckFailed");
                }
            }
            finally
            {
                _isCheckingAppUpdates = false;
            }
        }

        private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            if (_isCheckingAppUpdates)
            {
                ShowInfo("CheckingForUpdates");
                return;
            }

            Log(LocalizationHelper.GetString("CheckingForUpdates"), "info");
            await CheckForAppUpdatesAsync(true);
        }

        private string TranslateProgressMessage(string status)
        {
            // Check if status is a translation key (format: "Key" or "Key:param1:param2")
            if (status.Contains(':'))
            {
                var parts = status.Split(':');
                var key = parts[0];
                var parameters = parts.Skip(1).ToArray();

                try
                {
                    var translated = LocalizationHelper.GetString(key);
                    
                    // Handle specific cases with parameters
                    switch (key)
                    {
                        case "CopyingFiles":
                            if (parameters.Length >= 2)
                            {
                                return string.Format(translated, parameters[0], parameters[1]);
                            }
                            break;
                        case "MergeFailedWithCode":
                            if (parameters.Length >= 1)
                            {
                                return string.Format(translated, parameters[0]);
                            }
                            break;
                        case "ExecutionError":
                        case "UnexpectedError":
                        case "FailedToCopyMergedFiles":
                            if (parameters.Length >= 1)
                            {
                                return string.Format(translated, string.Join(":", parameters));
                            }
                            break;
                        case "TmpDataNotFound":
                            if (parameters.Length >= 1)
                            {
                                return string.Format(translated, string.Join(":", parameters));
                            }
                            break;
                    }
                    
                    return translated;
                }
                catch
                {
                    // If translation fails, return original status
                    return status;
                }
            }
            else
            {
                // Simple translation key without parameters
                try
                {
                    return LocalizationHelper.GetString(status);
                }
                catch
                {
                    // If translation fails, return original status
                    return status;
                }
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
                var baseName = name.EndsWith(".cfg.bin", StringComparison.OrdinalIgnoreCase)
                    ? name[..^8]
                    : Path.GetFileNameWithoutExtension(name);

                const string suffix = "_cpk_list";
                if (baseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    baseName = baseName[..^suffix.Length];
                }

                var formatted = baseName.Replace('_', '.');
                return string.IsNullOrWhiteSpace(formatted) ? baseName : formatted;
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
