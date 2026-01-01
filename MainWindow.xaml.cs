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
        private static bool _httpClientDisposed = false;
        private AppConfig _config = null!;
        private readonly object _configLock = new object();
        private bool _isApplying;
        private bool _isDownloadingCpkLists;
        private DispatcherTimer? _playResetTimer;
        private DispatcherTimer? _updateCheckTimer;
        private DispatcherTimer? _appUpdateCheckTimer;
        private bool _isCheckingUpdates;
        private bool _isCheckingAppUpdates;
        private string? _previousProfileSelection;
        private static DateTime? _lastRateLimitHit;
        private static readonly TimeSpan RateLimitCooldown = TimeSpan.FromMinutes(5);
        
        private const string VanillaCpkListFileName = "VanillaCpkList.cfg.bin";
        private const string LatestCpkListFileName = "LatestCpkList.cfg.bin";
        private const string CpkListFileName = "cpk_list.cfg.bin";

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class and sets up the mod manager interface.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            
            var logger = Helpers.Logger.Instance;
            logger.SetUICallback((message, level, isTechnical) =>
            {
                Dispatcher.Invoke(() =>
                {
                    var config = GetConfig();
                    if (isTechnical && !config.ShowTechnicalLogs)
                    {
                        return;
                    }
                    
                    var formattedMessage = $"{message}\n";
                    
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
            });
            
            _configManager = new ConfigManager();
            _modManager = new ModManager();
            _lastInstallManager = new LastInstallManager();
            _viola = new ViolaIntegration(message => logger.Info(message, true));
            _profileManager = new Managers.ProfileManager();
            _modEntries = new ObservableCollection<ModEntryViewModel>();
            
            ModsListView.ItemsSource = _modEntries;
            CpkSelector.ItemsSource = _availableCpkFiles;
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("IEVRModManager/1.0");
            
            LoadConfig();
            EnsureStorageStructure();
            MigrateLegacyVanillaName(Config.SharedStorageCpkDir);
            RefreshCpkOptions();
            
            LoadLastAppliedProfileOnStartup();
            
            ScanMods();
            CleanupTempDir();
            
            Loaded += (s, e) => 
            {
                UpdateLocalizedTexts();
                RefreshProfileSelector();
                var config = GetConfig();
                if (!string.IsNullOrWhiteSpace(config.LastAppliedProfile))
                {
                    _isRefreshingProfileSelector = true;
                    try
                    {
                        if (ProfileSelector.Items.Contains(config.LastAppliedProfile))
                        {
                            ProfileSelector.SelectedItem = config.LastAppliedProfile;
                            _previousProfileSelection = config.LastAppliedProfile;
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
            _ = PrefetchModsIfNeededAsync();
        }

        /// <summary>
        /// Gets a thread-safe copy of the current configuration.
        /// </summary>
        private AppConfig GetConfig()
        {
            lock (_configLock)
            {
                return _config ?? Models.AppConfig.Default();
            }
        }

        /// <summary>
        /// Sets the configuration in a thread-safe manner.
        /// </summary>
        private void SetConfig(AppConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            lock (_configLock)
            {
                _config = config;
            }
        }

        private void LoadConfig()
        {
            try
            {
                var loadedConfig = _configManager.Load();
                SetConfig(loadedConfig);
                var config = GetConfig();
                var lastCheck = config.LastAppUpdateCheckUtc;
                if (lastCheck != DateTime.MinValue)
                {
                    var timeSinceLastCheck = DateTime.UtcNow - lastCheck;
                    System.Diagnostics.Debug.WriteLine($"[MainWindow] Loaded config - LastAppUpdateCheckUtc: {lastCheck:yyyy-MM-dd HH:mm:ss} UTC ({timeSinceLastCheck.TotalMinutes:F1} minutes ago)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[MainWindow] Loaded config - LastAppUpdateCheckUtc: never");
                }
            }
            catch (Exception ex)
            {
                Helpers.Logger.Instance.Log(Helpers.LogLevel.Error, $"Failed to load config, using defaults: {ex.Message}", true, ex);
                SetConfig(Models.AppConfig.Default());
            }
        }

        private void UpdateLocalizedTexts()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[Localization] Updating localized texts...");
                
                var appTitle = LocalizationHelper.GetString("AppTitle");
                Title = appTitle;
                System.Diagnostics.Debug.WriteLine($"[Localization] AppTitle = '{appTitle}'");
                
                if (TitleLabel != null)
                {
                    TitleLabel.Content = appTitle;
                    System.Diagnostics.Debug.WriteLine("[Localization] Updated TitleLabel");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[Localization] WARNING: TitleLabel is null");
                }
                
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
                
                if (OpenModsFolderButton != null)
                    OpenModsFolderButton.Content = LocalizationHelper.GetString("OpenModsFolder");
                if (RefreshModsButton != null)
                    RefreshModsButton.Content = LocalizationHelper.GetString("ScanMods");
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
                if (HelpLink != null)
                    HelpLink.Text = LocalizationHelper.GetString("Help");
                if (ReportBugButton != null)
                    ReportBugButton.Content = LocalizationHelper.GetString("ReportBug");
                if (SaveLogButton != null)
                    SaveLogButton.Content = LocalizationHelper.GetString("SaveLog");
                if (ExitButton != null)
                    ExitButton.Content = LocalizationHelper.GetString("Exit");
                if (ManageProfilesButton != null)
                    ManageProfilesButton.Content = LocalizationHelper.GetString("ManageProfiles");
                
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

            try
            {
                var profile = _profileManager.LoadProfile(selectedProfileName);
                if (profile != null)
                {
                    _previousProfileSelection = selectedProfileName;
                    LoadProfile(profile);
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
            catch (ArgumentException)
            {
                // Invalid profile name, restore previous selection
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

            try
            {
                var profile = _profileManager.LoadProfile(_config.LastAppliedProfile);
                if (profile != null)
                {
                    _config.Mods = profile.Mods;
                    
                    if (!string.IsNullOrWhiteSpace(profile.SelectedCpkName))
                    {
                        _config.SelectedCpkName = profile.SelectedCpkName;
                    }
                    
                    Log($"Last applied profile '{profile.Name}' will be loaded on startup.", "info", true);
                }
                else
                {
                    // Profile not found, clear the saved profile name
                    _config.LastAppliedProfile = string.Empty;
                }
            }
            catch (ArgumentException)
            {
                // Invalid profile name, clear it
                _config.LastAppliedProfile = string.Empty;
            }
        }

        private void LoadLastAppliedProfile()
        {
            if (string.IsNullOrWhiteSpace(_config.LastAppliedProfile))
            {
                return;
            }

            try
            {
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
                    
                    Log($"Last applied profile '{profile.Name}' loaded on startup.", "info", true);
                }
                else
                {
                    // Profile not found, clear the saved profile name
                    _config.LastAppliedProfile = string.Empty;
                }
            }
            catch (ArgumentException)
            {
                // Invalid profile name, clear it
                _config.LastAppliedProfile = string.Empty;
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
                Log($"Error refreshing cpk list: {ex.Message}", "error", true);
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

        /// <summary>
        /// Creates a mod profile from the current state of enabled mods and selected CPK file.
        /// </summary>
        /// <param name="profileName">The name for the new profile.</param>
        /// <returns>A new <see cref="ModProfile"/> containing the current mod configuration.</returns>
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

        /// <summary>
        /// Loads a mod profile and applies its mod configuration to the current state.
        /// </summary>
        /// <param name="profile">The profile to load.</param>
        public void LoadProfile(ModProfile profile)
        {
            // Create a map of profile mods
            var profileModMap = profile.Mods.ToDictionary(m => m.Name, m => m.Enabled);
            
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
            
            if (!string.IsNullOrWhiteSpace(profile.SelectedCpkName))
            {
                _config.SelectedCpkName = profile.SelectedCpkName;
                RefreshCpkOptions();
            }
            
            _config.LastAppliedProfile = profile.Name;
            
            SaveConfig();
            RefreshProfileSelector();
            Log($"Profile '{profile.Name}' loaded.", "info", true);
        }

        private void SaveConfig()
        {
            try
            {
                var config = GetConfig();
                // Update mods list from current entries
                config.Mods = _modEntries.Select(me => new ModData
                {
                    Name = me.Name,
                    Enabled = me.Enabled,
                    ModLink = me.ModLink
                }).ToList();

                SetConfig(config);
                var success = _configManager.Save(config);

                if (!success)
                {
                    ShowError("CouldNotSaveConfiguration");
                    Log("Could not save configuration.", "error");
                }
            }
            catch (Exceptions.ConfigurationException ex)
            {
                ShowError("CouldNotSaveConfiguration");
                Log($"Configuration error: {ex.Message}", "error");
                if (ex.InnerException != null)
                {
                    Log($"Inner exception: {ex.InnerException.Message}", "error");
                }
            }
            catch (ArgumentNullException ex)
            {
                ShowError("CouldNotSaveConfiguration");
                Log($"Invalid configuration: {ex.Message}", "error");
            }
            catch (Exception ex)
            {
                ShowError("CouldNotSaveConfiguration");
                Log($"Unexpected error saving configuration: {ex.Message}", "error");
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
            if (_lastRateLimitHit.HasValue && DateTime.UtcNow - _lastRateLimitHit.Value < RateLimitCooldown)
            {
                var waitMinutes = Math.Ceiling((RateLimitCooldown - (DateTime.UtcNow - _lastRateLimitHit.Value)).TotalMinutes);
                Log($"Skipping cpk_list download - rate limit cooldown active. Wait {waitMinutes} more minutes.", "info", true);
                System.Diagnostics.Debug.WriteLine($"[CpkDownload] Skipping due to rate limit cooldown (hit at {_lastRateLimitHit.Value:HH:mm:ss}, wait {waitMinutes} min)");
                return 0;
            }

            const string apiUrl = "https://api.github.com/repos/Adr1GR/IEVR_Mod_Manager/contents/cpk_list";
            var targetDir = Config.SharedStorageCpkDir;
            Directory.CreateDirectory(targetDir);

            System.Diagnostics.Debug.WriteLine($"[CpkDownload] Starting download of cpk_list files from GitHub API");
            using var response = await _httpClient.GetAsync(apiUrl);
            
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // Rate limit exceeded
                var retryAfter = response.Headers.RetryAfter?.Delta?.TotalMinutes ?? 0;
                var rateLimitRemaining = response.Headers.Contains("X-RateLimit-Remaining") 
                    ? response.Headers.GetValues("X-RateLimit-Remaining").FirstOrDefault() 
                    : "unknown";
                var rateLimitReset = response.Headers.Contains("X-RateLimit-Reset")
                    ? response.Headers.GetValues("X-RateLimit-Reset").FirstOrDefault()
                    : null;

                var errorMsg = "GitHub API rate limit exceeded. ";
                if (retryAfter > 0)
                {
                    errorMsg += $"Please try again in approximately {Math.Ceiling(retryAfter)} minutes.";
                }
                else if (rateLimitReset != null && long.TryParse(rateLimitReset, out var resetTimestamp))
                {
                    var resetTime = DateTimeOffset.FromUnixTimeSeconds(resetTimestamp);
                    var waitTime = resetTime - DateTimeOffset.UtcNow;
                    if (waitTime.TotalMinutes > 0)
                    {
                        errorMsg += $"Please try again in approximately {Math.Ceiling(waitTime.TotalMinutes)} minutes.";
                    }
                }
                else
                {
                    errorMsg += "Please try again later.";
                }
                
                _lastRateLimitHit = DateTime.UtcNow;
                Log(errorMsg, "error");
                System.Diagnostics.Debug.WriteLine($"[CpkDownload] Rate limit hit at {_lastRateLimitHit.Value:HH:mm:ss}");
                throw new HttpRequestException(errorMsg, null, response.StatusCode);
            }
            
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();

            var items = JsonSerializer.Deserialize<List<GithubContent>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<GithubContent>();

            var candidates = items
                .Where(i => i.Type == "file" && (i.Name.EndsWith(".cfg.bin", StringComparison.OrdinalIgnoreCase) || i.Name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            System.Diagnostics.Debug.WriteLine($"[CpkDownload] Found {candidates.Count} cpk_list files to check");
            var downloaded = 0;
            var skipped = 0;
            foreach (var item in candidates)
            {
                var targetPath = Path.Combine(targetDir, item.Name);
                if (File.Exists(targetPath))
                {
                    skipped++;
                    System.Diagnostics.Debug.WriteLine($"[CpkDownload] Skipping {item.Name} (already exists)");
                    continue;
                }

                var downloadUrl = item.DownloadUrl;
                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    downloadUrl = $"https://raw.githubusercontent.com/Adr1GR/IEVR_Mod_Manager/main/cpk_list/{item.Name}";
                }

                try
                {
                    System.Diagnostics.Debug.WriteLine($"[CpkDownload] Downloading {item.Name} from {downloadUrl}");
                    using var fileResponse = await _httpClient.GetAsync(downloadUrl);
                    if (fileResponse.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        // Rate limit on individual file download - stop all downloads
                        _lastRateLimitHit = DateTime.UtcNow;
                        var retryAfter = fileResponse.Headers.RetryAfter?.Delta?.TotalMinutes ?? 0;
                        var errorMsg = $"Rate limit exceeded while downloading {item.Name}. Stopping downloads.";
                        if (retryAfter > 0)
                        {
                            errorMsg += $" Please try again in approximately {Math.Ceiling(retryAfter)} minutes.";
                        }
                        Log(errorMsg, "error");
                        System.Diagnostics.Debug.WriteLine($"[CpkDownload] Rate limit hit at {_lastRateLimitHit.Value:HH:mm:ss}. Downloaded: {downloaded}, Skipped: {skipped + (candidates.Count - downloaded - skipped - 1)}");
                        break; // Stop downloading remaining files
                    }
                    fileResponse.EnsureSuccessStatusCode();
                    var bytes = await fileResponse.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(targetPath, bytes);
                    downloaded++;
                    System.Diagnostics.Debug.WriteLine($"[CpkDownload] Successfully downloaded {item.Name} ({bytes.Length} bytes)");
                }
                catch (HttpRequestException ex)
                {
                    // Log but continue with other files (unless it's a rate limit, which we handle above)
                    Log($"Error downloading {item.Name}: {ex.Message}", "warning");
                    System.Diagnostics.Debug.WriteLine($"[CpkDownload] Error downloading {item.Name}: {ex.Message}");
                    continue;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[CpkDownload] Completed. Downloaded: {downloaded}, Skipped: {skipped}");
            return downloaded;
        }

        private async Task DownloadAndRefreshCpkListsAsync(bool forceCheck = false)
        {
            if (_isDownloadingCpkLists)
            {
                Log("Already downloading cpk_list files, please wait.", "info");
                return;
            }

            if (!forceCheck)
            {
                var cpkDir = Config.SharedStorageCpkDir;
                var hasCpkFiles = Directory.Exists(cpkDir) && 
                    Directory.GetFiles(cpkDir, "*.bin", SearchOption.TopDirectoryOnly)
                        .Concat(Directory.GetFiles(cpkDir, "*.cfg.bin", SearchOption.TopDirectoryOnly))
                        .Any();

                if (!hasCpkFiles)
                {
                    System.Diagnostics.Debug.WriteLine("[CpkDownload] No cpk files found, proceeding to download.");
                }
                else
                {
                    var signatureChanged = false;
                    if (!string.IsNullOrWhiteSpace(_config.GamePath) && Directory.Exists(_config.GamePath))
                    {
                        try
                        {
                            var currentSignature = await Task.Run(() => ComputeGameSignature(_config.GamePath));
                            signatureChanged = !string.IsNullOrWhiteSpace(currentSignature) &&
                                !string.IsNullOrWhiteSpace(_config.LastKnownPacksSignature) &&
                                !string.Equals(currentSignature, _config.LastKnownPacksSignature, StringComparison.Ordinal);
                            
                            if (signatureChanged)
                            {
                                System.Diagnostics.Debug.WriteLine($"[CpkDownload] Signature changed detected - current: {currentSignature.Substring(0, Math.Min(8, currentSignature.Length))}..., last: {_config.LastKnownPacksSignature.Substring(0, Math.Min(8, _config.LastKnownPacksSignature.Length))}...");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("[CpkDownload] No signature change detected, skipping download to avoid API calls.");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[CpkDownload] Error computing signature: {ex.Message}");
                            return;
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[CpkDownload] GamePath not configured, skipping automatic download (cpk files already exist).");
                        return;
                    }

                    if (!signatureChanged)
                    {
                        System.Diagnostics.Debug.WriteLine("[CpkDownload] Skipping automatic check - no signature change detected and cpk files already exist.");
                        return;
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[CpkDownload] Force check requested (manual button click), proceeding to download");
            }

            _isDownloadingCpkLists = true;

            try
            {
                Log("Fetching available cpk_list files from GitHub...", "info", true);
                var downloaded = await DownloadCpkFilesAsync();
                if (downloaded > 0)
                {
                    Log($"Downloaded {downloaded} cpk_list file(s).", "success");
                }
                else
                {
                    Log("No new cpk_list files downloaded (they may already exist).", "info");
                }

                await EnsureLatestCpkExistsAsync();
                
                RefreshCpkOptions();
                
                _config.LastCpkListCheckUtc = DateTime.UtcNow;
                SaveConfig();
                System.Diagnostics.Debug.WriteLine($"[CpkDownload] Updated last check time to {_config.LastCpkListCheckUtc:HH:mm:ss}");
            }
            catch (Exception ex)
            {
                Log($"Error downloading cpk_list files: {ex.Message}", "error", true);
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

        /// <summary>
        /// Handles the click event for downloading CPK list files from GitHub.
        /// </summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The routed event arguments.</param>
        public async void DownloadCpkLists_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button)
            {
                button.IsEnabled = false;
            }

            try
            {
                await DownloadAndRefreshCpkListsAsync(forceCheck: true);
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
                Log($"Could not open cpk folder: {ex.Message}", "error", true);
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
                Log("Restoring backup...", "info", true);

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

                        try
                        {
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
                        catch (Exceptions.ModManagerException ex)
                        {
                            Log($"Error saving last install info: {ex.Message}", "error", true);
                        }
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

                    Log($"Warning: {packsModifiers.Count} mod(s) will modify data/packs. User chose to continue.", "info", true);
                }

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
                    
                    Log($"Warning: {conflicts.Count} file conflict(s) detected. User chose to continue.", "info", true);
                }

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

            var config = GetConfig();
            var exePath = Path.Combine(config.GamePath, "nie.exe");
            if (!File.Exists(exePath))
            {
                ShowError("NieExeNotFound");
                return;
            }

            try
            {
                SetPlayLoadingState(true);
                Log("Launching game...", "info", true);

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = config.GamePath,
                    UseShellExecute = true
                };
                
                var gameProcess = Process.Start(processStartInfo);
                if (gameProcess != null)
                {
                    // Process started successfully - it will continue running independently
                    // We don't need to dispose it as it should continue after the app closes
                    // However, we can optionally enable process exit handling if needed
                    gameProcess.EnableRaisingEvents = false; // Don't track process exit
                }
                
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
                if (showMessageOnExisting)
                {
                    // User already confirmed, so delete the old backup and create a new one
                    Log("Backup folder already exists; deleting old backup to create a new one.", "info", true);
                    try
                    {
                        Directory.Delete(backupRoot, true);
                        Log("Old backup deleted successfully.", "info", true);
                    }
                    catch (Exception ex)
                    {
                        Log($"Error deleting old backup: {ex.Message}", "error");
                        ShowError("ErrorDeletingBackup", ex.Message);
                        return false;
                    }
                }
                else
                {
                    // User hasn't confirmed yet, just skip
                    Log("Backup folder already exists; skipping backup creation.", "info", true);
                    return true;
                }
            }

            try
            {
                Log("Creating backup...", "info", true);
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
                Log("Game data folder not found; data backup skipped.", "error", true);
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
                Log($"{CpkListFileName} not found; it was not backed up.", "info", true);
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
                Log("Backup data folder not found; skipping data restore.", "error", true);
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
                Log($"No {CpkListFileName} found in backup.", "info", true);
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
                Log("No common folder found in backup.", "info", true);
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
                    Dispatcher.Invoke(() => Log("violacli returned error; aborting copy.", "error", true));
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
                    
                    try
                    {
                        _lastInstallManager.Save(new LastInstallInfo
                        {
                            GamePath = Path.GetFullPath(gamePath),
                            Files = mergedFiles,
                            Mods = modNames,
                            AppliedAt = DateTime.UtcNow,
                            SelectedCpkName = _config.SelectedCpkName ?? string.Empty,
                            SelectedCpkInfo = GetCpkInfo(_config.CfgBinPath)
                        });
                    }
                    catch (Exceptions.ModManagerException ex)
                    {
                        Log($"Error saving last install info: {ex.Message}", "error");
                    }

                    Dispatcher.Invoke(() =>
                    {
                        var successWindow = new SuccessMessageWindow(this, message, modNames);
                        successWindow.ShowDialog();
                    });
                }
                else
                {
                    Dispatcher.Invoke(() => Log("Failed to copy merged files.", "error", true));
                    progressCallback(0, LocalizationHelper.GetString("FailedToCopyMergedFiles"));
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => Log($"Unexpected error: {ex.Message}", "error", true));
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
                Dispatcher.Invoke(() => Log("Skipped cleanup: stored install points to a different game path.", "info", true));
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
                    Dispatcher.Invoke(() => Log($"Could not delete leftover file {relativePath}: {ex.Message}", "error", true));
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

        private void ReportBug_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://gamebanana.com/tools/issues/21354",
                UseShellExecute = true
            });
        }

        private void Downloads_Click(object sender, RoutedEventArgs e)
        {
            var window = new DownloadsWindow(this);
            window.ShowDialog();
        }

        /// <summary>
        /// Logs a message with the specified level.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="level">The log level (e.g., "info", "error", "warning").</param>
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
                ShowTechnicalLogs = _config.ShowTechnicalLogs,
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
                    _config.ShowTechnicalLogs = configCopy.ShowTechnicalLogs;
                    SaveConfig();
                },
                RunCreateBackupFlowAsync,
                RunRestoreBackupFlowAsync,
                async () => await CheckForAppUpdatesAsync(true),
                () =>
                {
                    var window = new DownloadsWindow(this);
                    window.ShowDialog();
                });
            
            window.ShowDialog();
            
            // Ensure final values are saved - update config thread-safely
            var config = GetConfig();
            config.GamePath = configCopy.GamePath;
            config.CfgBinPath = configCopy.CfgBinPath;
            config.SelectedCpkName = configCopy.SelectedCpkName;
            config.ViolaCliPath = configCopy.ViolaCliPath;
            config.Theme = configCopy.Theme;
            config.ShowTechnicalLogs = configCopy.ShowTechnicalLogs;
            
            // Check if language changed
            bool languageChanged = config.Language != configCopy.Language;
            config.Language = configCopy.Language;
            SetConfig(config);
            
            // Apply language change if it changed
            if (languageChanged)
            {
                LocalizationHelper.SetLanguage(config.Language);
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

        /// <summary>
        /// Disposes the static HttpClient instance. Should be called when the application is shutting down.
        /// </summary>
        public static void DisposeHttpClient()
        {
            if (!_httpClientDisposed)
            {
                _httpClient?.Dispose();
                _httpClientDisposed = true;
            }
        }

        private void Log(string message, string level = "info", bool isTechnical = false)
        {
            var logger = Helpers.Logger.Instance;
            var logLevel = level.ToLower() switch
            {
                "debug" => LogLevel.Debug,
                "info" => LogLevel.Info,
                "warning" => LogLevel.Warning,
                "warn" => LogLevel.Warning,
                "error" => LogLevel.Error,
                "success" => LogLevel.Info, // Treat success as info
                _ => LogLevel.Info
            };
            
            logger.Log(logLevel, message, isTechnical);
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
                    Log("Possible game update detected, but data/cpk_list.cfg.bin was not found.", "error", true);
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
                Log($"Could not check for game updates: {ex.Message}", "error", true);
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

                File.Copy(cpkPath, targetPath, true);

                Log($"Detected game update{(string.IsNullOrWhiteSpace(latestBuildId) ? string.Empty : $" ({latestBuildId})")}. Copied game data/{CpkListFileName} to {LatestCpkListFileName}.", "success");
                RefreshCpkOptions();
            }
            catch (Exception ex)
            {
                Log($"Could not copy the updated {CpkListFileName}: {ex.Message}", "error", true);
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
                        Log("Created LatestCpkList.cfg.bin from 1_4_2_cpk_list.cfg.bin.", "info", true);
                        return;
                    }
                    return;
                }

                // Use the most recent file
                var mostRecent = cpkFiles.First();
                File.Copy(mostRecent, targetPath, true);
                Log($"Created LatestCpkList.cfg.bin from {Path.GetFileName(mostRecent)}.", "info", true);
            }
            catch (Exception ex)
            {
                Log($"Could not create LatestCpkList.cfg.bin: {ex.Message}", "error", true);
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
                    Log($"Renamed {VanillaCpkListFileName} to {LatestCpkListFileName}.", "info", true);
                }
            }
            catch (Exception ex)
            {
                Log($"Could not rename {VanillaCpkListFileName}: {ex.Message}", "error", true);
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

        private static string ComputeGameSignature(string gamePath)
        {
            // Only rely on data/packs contents to avoid false positives
            return ComputePacksSignature(gamePath);
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
                    Log($"Could not verify read/write in {label}: content mismatch.", "error", true);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log($"Could not access {label}: {ex.Message}", "error", true);
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
                System.Diagnostics.Debug.WriteLine("[AppUpdate] Already checking for updates, skipping duplicate call");
                return;
            }

            // Check if at least 1 hour has passed since last automatic check
            // Only skip automatic checks (when showNoUpdateMessage is false), not manual checks
            if (!showNoUpdateMessage)
            {
                // Reload config to get the latest LastAppUpdateCheckUtc value
                var currentConfig = _configManager.Load();
                var lastCheck = currentConfig.LastAppUpdateCheckUtc;
                
                // Check if lastCheck is valid (not MinValue and not default)
                var isValidLastCheck = lastCheck != DateTime.MinValue && 
                                       lastCheck.Year > 1 && 
                                       lastCheck != default(DateTime);
                
                if (isValidLastCheck)
                {
                    var timeSinceLastCheck = DateTime.UtcNow - lastCheck;
                    var lastCheckInfo = $"{timeSinceLastCheck.TotalMinutes:F1} minutes ago";
                    Log($"[AppUpdate] Last check was at: {lastCheck:yyyy-MM-dd HH:mm:ss} UTC ({lastCheckInfo})", "info", true);
                    
                    if (timeSinceLastCheck.TotalHours < 1.0)
                    {
                        var minutesRemaining = Math.Ceiling(60 - timeSinceLastCheck.TotalMinutes);
                        Log($"[AppUpdate] Skipping automatic check - last check was {timeSinceLastCheck.TotalMinutes:F1} minutes ago. Next check in {minutesRemaining} minutes.", "info", true);
                        return;
                    }
                }
                else
                {
                    Log($"[AppUpdate] Last check was never (or invalid). Proceeding with check.", "info", true);
                }
            }

            _isCheckingAppUpdates = true;
            try
            {
                System.Diagnostics.Debug.WriteLine("[AppUpdate] Starting update check");
                var currentVersion = Managers.AppUpdateManager.GetCurrentVersion();
                Log($"Current version: {currentVersion}", "info", true);
                
                var releaseInfo = await Managers.AppUpdateManager.CheckForUpdatesAsync();

                // Save the check time after attempting the check (even if it failed)
                var checkTime = DateTime.UtcNow;
                _config.LastAppUpdateCheckUtc = checkTime;
                await _configManager.UpdateLastAppUpdateCheckUtcAsync(checkTime);
                Log($"[AppUpdate] Saved check time: {checkTime:yyyy-MM-dd HH:mm:ss} UTC", "info", true);
                
                // Reload config to keep in-memory config in sync
                SetConfig(_configManager.Load());

                if (releaseInfo == null)
                {
                    Log("No release info found from GitHub API", "error", true);
                    if (showNoUpdateMessage)
                    {
                        ShowError("UpdateCheckFailed");
                    }
                    return;
                }

                var latestVersion = releaseInfo.TagName.TrimStart('v', 'V');
                Log($"Latest version from GitHub: {releaseInfo.TagName} (parsed as: {latestVersion})", "info", true);
                
                var isNewer = Managers.AppUpdateManager.IsNewerVersion(currentVersion, latestVersion);
                Log($"Is newer version? {isNewer} (current: {currentVersion}, latest: {latestVersion})", "info", true);

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
                Log($"Error checking for app updates: {ex.Message}", "error", true);
                Log($"Stack trace: {ex.StackTrace}", "error", true);
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

        private async Task PrefetchModsIfNeededAsync()
        {
            try
            {
                // Check if at least 1 hour has passed since last prefetch
                var lastPrefetch = _config.LastModPrefetchUtc;
                var timeSinceLastPrefetch = DateTime.UtcNow - lastPrefetch;
                
                if (lastPrefetch != DateTime.MinValue && timeSinceLastPrefetch.TotalHours < 1.0)
                {
                    var minutesRemaining = Math.Ceiling(60 - timeSinceLastPrefetch.TotalMinutes);
                    System.Diagnostics.Debug.WriteLine($"[ModPrefetch] Skipping prefetch - last prefetch was {timeSinceLastPrefetch.TotalMinutes:F1} minutes ago. Next prefetch in {minutesRemaining} minutes.");
                    return;
                }

                System.Diagnostics.Debug.WriteLine("[ModPrefetch] Starting mod prefetch");
                await Windows.GameBananaBrowserWindow.PrefetchModsAsync(this);
                
                // Save the prefetch time after successful prefetch
                _config.LastModPrefetchUtc = DateTime.UtcNow;
                _configManager.UpdateLastModPrefetchUtc(_config.LastModPrefetchUtc);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ModPrefetch] Error during prefetch: {ex.Message}");
                // Don't save prefetch time on error, so it will retry on next startup
            }
        }

        private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            if (_isCheckingAppUpdates)
            {
                ShowInfo("CheckingForUpdates");
                return;
            }

            Log(LocalizationHelper.GetString("CheckingForUpdates"), "info", true);
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

    /// <summary>
    /// ViewModel for displaying mod entry information in the UI.
    /// </summary>
    public class ModEntryViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _enabled;

        /// <summary>
        /// Gets or sets the mod directory name.
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets whether the mod is enabled.
        /// </summary>
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
        
        /// <summary>
        /// Gets the icon string indicating enabled status (✓ or ✗).
        /// </summary>
        public string EnabledIcon => Enabled ? "✓" : "✗";
        
        /// <summary>
        /// Gets or sets the display name of the mod.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the author of the mod.
        /// </summary>
        public string Author { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the mod version.
        /// </summary>
        public string ModVersion { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the game version this mod targets.
        /// </summary>
        public string GameVersion { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the link to the mod.
        /// </summary>
        public string ModLink { get; set; } = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="ModEntryViewModel"/> class from a <see cref="ModEntry"/>.
        /// </summary>
        /// <param name="mod">The mod entry to create the view model from.</param>
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

        /// <summary>
        /// Occurs when a property value changes.
        /// </summary>
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Raises the <see cref="PropertyChanged"/> event.
        /// </summary>
        /// <param name="propertyName">The name of the property that changed.</param>
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Represents a CPK file option for selection in the UI.
    /// </summary>
    public class CpkOption
    {
        /// <summary>
        /// Gets or sets the file name of the CPK file.
        /// </summary>
        public string FileName { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets the formatted display name for the CPK file, removing suffixes and formatting for display.
        /// </summary>
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

    /// <summary>
    /// Represents content from GitHub API (files or directories).
    /// </summary>
    public class GithubContent
    {
        /// <summary>
        /// Gets or sets the name of the content item.
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the type of content (e.g., "file" or "dir").
        /// </summary>
        public string Type { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the download URL for the content.
        /// </summary>
        [JsonPropertyName("download_url")]
        public string DownloadUrl { get; set; } = string.Empty;
    }
}
