using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using IEVRModManager.Models;
using IEVRModManager.Helpers;
using IEVRModManager.Exceptions;
using static IEVRModManager.Helpers.Logger;

namespace IEVRModManager.Managers
{
    /// <summary>
    /// Manages loading and saving of application configuration.
    /// </summary>
    public class ConfigManager
    {
        private readonly string _configPath;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigManager"/> class.
        /// </summary>
        public ConfigManager()
        {
            _configPath = Config.ConfigPath;
            FileSystemHelper.EnsureDirectoryExists(Path.GetDirectoryName(_configPath));
        }

        /// <summary>
        /// Loads the application configuration from disk.
        /// </summary>
        /// <returns>An <see cref="AppConfig"/> instance. Returns default configuration if file doesn't exist or is invalid.</returns>
        /// <exception cref="ConfigurationException">Thrown when there's an error reading or parsing the configuration file.</exception>
        /// <remarks>
        /// This method is provided for backward compatibility. For async contexts, use <see cref="LoadAsync"/> instead.
        /// Uses synchronous file operations to avoid deadlocks during initialization.
        /// </remarks>
        public AppConfig Load()
        {
            if (!File.Exists(_configPath))
            {
                return AppConfig.Default();
            }

            try
            {
                var json = File.ReadAllText(_configPath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return AppConfig.Default();
                }

                var config = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                if (config == null)
                {
                    var defaultConfig = AppConfig.Default();
                    TryDetectGamePathFromSteam(defaultConfig);
                    return defaultConfig;
                }

                var migratedConfig = MigrateConfig(config, json);
                TryDetectGamePathFromSteam(migratedConfig);
                return migratedConfig;
            }
            catch (JsonException ex)
            {
                Instance.Log(LogLevel.Error, "Error parsing config JSON", true, ex);
                throw new ConfigurationException("Failed to parse configuration file. The file may be corrupted.", ex);
            }
            catch (IOException ex)
            {
                Instance.Log(LogLevel.Error, "IO error loading config", true, ex);
                throw new ConfigurationException("Failed to read configuration file. Check file permissions.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                Instance.Log(LogLevel.Error, "Access denied loading config", true, ex);
                throw new ConfigurationException("Access denied to configuration file.", ex);
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Error, "Unexpected error loading config", true, ex);
                throw new ConfigurationException("An unexpected error occurred while loading configuration.", ex);
            }
        }

        /// <summary>
        /// Loads the application configuration from disk asynchronously.
        /// </summary>
        /// <returns>An <see cref="AppConfig"/> instance. Returns default configuration if file doesn't exist or is invalid.</returns>
        /// <exception cref="ConfigurationException">Thrown when there's an error reading or parsing the configuration file.</exception>
        public async Task<AppConfig> LoadAsync()
        {
            if (!File.Exists(_configPath))
            {
                return AppConfig.Default();
            }

            try
            {
                var json = await File.ReadAllTextAsync(_configPath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return AppConfig.Default();
                }

                var config = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                if (config == null)
                {
                    var defaultConfig = AppConfig.Default();
                    TryDetectGamePathFromSteam(defaultConfig);
                    return defaultConfig;
                }

                var migratedConfig = await MigrateConfigAsync(config, json);
                TryDetectGamePathFromSteam(migratedConfig);
                return migratedConfig;
            }
            catch (JsonException ex)
            {
                Instance.Log(LogLevel.Error, "Error parsing config JSON", true, ex);
                throw new ConfigurationException("Failed to parse configuration file. The file may be corrupted.", ex);
            }
            catch (IOException ex)
            {
                Instance.Log(LogLevel.Error, "IO error loading config", true, ex);
                throw new ConfigurationException("Failed to read configuration file. Check file permissions.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                Instance.Log(LogLevel.Error, "Access denied loading config", true, ex);
                throw new ConfigurationException("Access denied to configuration file.", ex);
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Error, "Unexpected error loading config", true, ex);
                throw new ConfigurationException("An unexpected error occurred while loading configuration.", ex);
            }
        }

        private AppConfig MigrateConfig(AppConfig loadedConfig, string originalJson)
        {
            if (loadedConfig == null)
            {
                throw new ArgumentNullException(nameof(loadedConfig));
            }

            if (string.IsNullOrWhiteSpace(originalJson))
            {
                return loadedConfig;
            }

            var defaultConfig = AppConfig.Default();
            var migrated = false;

            JsonDocument? jsonDoc = null;
            try
            {
                jsonDoc = JsonDocument.Parse(originalJson);
                var root = jsonDoc.RootElement;

                migrated |= MigrateStringProperties(root, loadedConfig, defaultConfig);
                migrated |= MigrateDateTimeProperties(root, loadedConfig, defaultConfig);
                migrated |= MigrateBooleanProperties(root, loadedConfig, defaultConfig);
                migrated |= MigrateModsList(root, loadedConfig);

                if (migrated)
                {
                    SaveMigratedConfig(loadedConfig);
                }

                return loadedConfig;
            }
            finally
            {
                jsonDoc?.Dispose();
            }
        }

        private async Task<AppConfig> MigrateConfigAsync(AppConfig loadedConfig, string originalJson)
        {
            if (loadedConfig == null)
            {
                throw new ArgumentNullException(nameof(loadedConfig));
            }

            if (string.IsNullOrWhiteSpace(originalJson))
            {
                return loadedConfig;
            }

            var defaultConfig = AppConfig.Default();
            var migrated = false;

            JsonDocument? jsonDoc = null;
            try
            {
                jsonDoc = JsonDocument.Parse(originalJson);
                var root = jsonDoc.RootElement;

                migrated |= MigrateStringProperties(root, loadedConfig, defaultConfig);
                migrated |= MigrateDateTimeProperties(root, loadedConfig, defaultConfig);
                migrated |= MigrateBooleanProperties(root, loadedConfig, defaultConfig);
                migrated |= MigrateModsList(root, loadedConfig);

                if (migrated)
                {
                    await SaveMigratedConfigAsync(loadedConfig);
                }

                return loadedConfig;
            }
            finally
            {
                jsonDoc?.Dispose();
            }
        }

        private bool MigrateStringProperties(JsonElement root, AppConfig config, AppConfig defaultConfig)
        {
            var migrated = false;

            migrated |= MigrateStringProperty(root, "TmpDir", () => config.TmpDir, 
                value => config.TmpDir = value, defaultConfig.TmpDir);
            migrated |= MigrateStringProperty(root, "Theme", () => config.Theme, 
                value => config.Theme = value, defaultConfig.Theme);
            migrated |= MigrateStringProperty(root, "Language", () => config.Language, 
                value => config.Language = value, defaultConfig.Language);
            migrated |= MigrateStringProperty(root, "LastAppliedProfile", () => config.LastAppliedProfile, 
                value => config.LastAppliedProfile = value, defaultConfig.LastAppliedProfile);

            var nullableStringProperties = new[]
            {
                new { Getter = (Func<string?>)(() => config.GamePath), Setter = (Action<string>)(v => config.GamePath = v) },
                new { Getter = (Func<string?>)(() => config.CfgBinPath), Setter = (Action<string>)(v => config.CfgBinPath = v) },
                new { Getter = (Func<string?>)(() => config.ViolaCliPath), Setter = (Action<string>)(v => config.ViolaCliPath = v) },
                new { Getter = (Func<string?>)(() => config.SelectedCpkName), Setter = (Action<string>)(v => config.SelectedCpkName = v) },
                new { Getter = (Func<string?>)(() => config.LastKnownPacksSignature), Setter = (Action<string>)(v => config.LastKnownPacksSignature = v) },
                new { Getter = (Func<string?>)(() => config.LastKnownSteamBuildId), Setter = (Action<string>)(v => config.LastKnownSteamBuildId = v) }
            };

            foreach (var prop in nullableStringProperties)
            {
                if (prop.Getter() == null)
                {
                    prop.Setter(string.Empty);
                    migrated = true;
                }
            }

            return migrated;
        }

        private bool MigrateDateTimeProperties(JsonElement root, AppConfig config, AppConfig defaultConfig)
        {
            var migrated = false;

            migrated |= MigrateDateTimeProperty(root, "LastCpkListCheckUtc", () => config.LastCpkListCheckUtc, 
                value => config.LastCpkListCheckUtc = value, defaultConfig.LastCpkListCheckUtc);
            migrated |= MigrateDateTimeProperty(root, "LastAppUpdateCheckUtc", () => config.LastAppUpdateCheckUtc, 
                value => config.LastAppUpdateCheckUtc = value, defaultConfig.LastAppUpdateCheckUtc);
            migrated |= MigrateDateTimeProperty(root, "LastModPrefetchUtc", () => config.LastModPrefetchUtc, 
                value => config.LastModPrefetchUtc = value, defaultConfig.LastModPrefetchUtc);

            return migrated;
        }

        private bool MigrateBooleanProperties(JsonElement root, AppConfig config, AppConfig defaultConfig)
        {
            return MigrateBooleanProperty(root, "ShowTechnicalLogs", () => config.ShowTechnicalLogs, 
                value => config.ShowTechnicalLogs = value, defaultConfig.ShowTechnicalLogs);
        }

        private bool MigrateModsList(JsonElement root, AppConfig config)
        {
            if (!root.TryGetProperty("Mods", out _) || config.Mods == null)
            {
                config.Mods = new List<ModData>();
                return true;
            }
            return false;
        }

        private bool MigrateStringProperty(JsonElement root, string propertyName, Func<string> getter, Action<string> setter, string defaultValue)
        {
            if (!root.TryGetProperty(propertyName, out _) || string.IsNullOrWhiteSpace(getter()))
            {
                setter(defaultValue);
                return true;
            }
            return false;
        }

        private bool MigrateDateTimeProperty(JsonElement root, string propertyName, Func<DateTime> getter, Action<DateTime> setter, DateTime defaultValue)
        {
            if (!root.TryGetProperty(propertyName, out _) || getter() == DateTime.MinValue)
            {
                setter(defaultValue);
                return true;
            }
            return false;
        }

        private bool MigrateBooleanProperty(JsonElement root, string propertyName, Func<bool> getter, Action<bool> setter, bool defaultValue)
        {
            if (!root.TryGetProperty(propertyName, out _))
            {
                setter(defaultValue);
                return true;
            }
            return false;
        }

        private void SaveMigratedConfig(AppConfig config)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                var json = JsonSerializer.Serialize(config, options);
                File.WriteAllText(_configPath, json);
                Instance.Debug("Configuration migrated and saved successfully.", true);
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Warning, "Error saving migrated config", true, ex);
            }
        }

        private async Task SaveMigratedConfigAsync(AppConfig config)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                var json = JsonSerializer.Serialize(config, options);
                await File.WriteAllTextAsync(_configPath, json);
                Instance.Debug("Configuration migrated and saved successfully.", true);
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Warning, "Error saving migrated config", true, ex);
            }
        }

        /// <summary>
        /// Saves the configuration using an AppConfig object directly.
        /// </summary>
        /// <param name="config">The configuration object to save.</param>
        /// <returns><c>true</c> if the configuration was saved successfully; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is null.</exception>
        /// <exception cref="ConfigurationException">Thrown when there's an error writing the configuration file.</exception>
        /// <remarks>
        /// This method is provided for backward compatibility. For async contexts, use <see cref="SaveAsync(AppConfig)"/> instead.
        /// Uses synchronous file operations to avoid deadlocks.
        /// </remarks>
        public bool Save(AppConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            try
            {
                FileSystemHelper.EnsureDirectoryExists(Path.GetDirectoryName(_configPath));
                
                var existingConfig = Load();
                
                config.LastAppUpdateCheckUtc = existingConfig.LastAppUpdateCheckUtc;
                config.LastModPrefetchUtc = existingConfig.LastModPrefetchUtc;
                config.LastCpkListCheckUtc = existingConfig.LastCpkListCheckUtc;

                SaveConfigToFile(config);
                return true;
            }
            catch (ConfigurationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                HandleFileOperationException(ex, "saving config");
                return false; // This line will never be reached, but satisfies compiler
            }
        }

        /// <summary>
        /// Saves the configuration using an AppConfig object directly asynchronously.
        /// </summary>
        /// <param name="config">The configuration object to save.</param>
        /// <returns><c>true</c> if the configuration was saved successfully; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is null.</exception>
        /// <exception cref="ConfigurationException">Thrown when there's an error writing the configuration file.</exception>
        public async Task<bool> SaveAsync(AppConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            try
            {
                FileSystemHelper.EnsureDirectoryExists(Path.GetDirectoryName(_configPath));
                
                var existingConfig = await LoadAsync();
                
                config.LastAppUpdateCheckUtc = existingConfig.LastAppUpdateCheckUtc;
                config.LastModPrefetchUtc = existingConfig.LastModPrefetchUtc;
                config.LastCpkListCheckUtc = existingConfig.LastCpkListCheckUtc;

                await SaveConfigToFileAsync(config);
                return true;
            }
            catch (ConfigurationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                HandleFileOperationException(ex, "saving config");
                return false; // This line will never be reached, but satisfies compiler
            }
        }

        /// <summary>
        /// Saves the configuration using individual parameters (legacy method for backward compatibility).
        /// </summary>
        /// <param name="gamePath">The path to the game directory.</param>
        /// <param name="selectedCpkName">The name of the selected CPK file.</param>
        /// <param name="cfgBinPath">The path to the CPK list configuration file.</param>
        /// <param name="violaCliPath">The path to the Viola CLI executable.</param>
        /// <param name="tmpDir">The temporary directory path.</param>
        /// <param name="modEntries">The list of mod entries to save.</param>
        /// <param name="lastKnownPacksSignature">The last known packs signature.</param>
        /// <param name="lastKnownSteamBuildId">The last known Steam build ID.</param>
        /// <param name="vanillaFallbackUntilUtc">The UTC date until which vanilla fallback is active.</param>
        /// <param name="theme">The selected theme name.</param>
        /// <param name="language">The selected language code.</param>
        /// <param name="lastAppliedProfile">The name of the last applied profile.</param>
        /// <returns><c>true</c> if the configuration was saved successfully; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="modEntries"/> is null.</exception>
        /// <exception cref="ConfigurationException">Thrown when there's an error writing the configuration file.</exception>
        /// <remarks>
        /// This method is provided for backward compatibility. For async contexts, use <see cref="SaveAsync(string, string, string, string, string, System.Collections.Generic.List{ModEntry}, string, string, DateTime, string, string, string)"/> instead.
        /// </remarks>
        public bool Save(string gamePath, string selectedCpkName, string cfgBinPath, string violaCliPath, 
            string tmpDir, System.Collections.Generic.List<ModEntry> modEntries,
            string lastKnownPacksSignature, string lastKnownSteamBuildId,
            DateTime vanillaFallbackUntilUtc, string theme, string language, string lastAppliedProfile = "")
        {
            if (modEntries == null)
            {
                throw new ArgumentNullException(nameof(modEntries));
            }

            try
            {
                FileSystemHelper.EnsureDirectoryExists(Path.GetDirectoryName(_configPath));
                
                var existingConfig = Load();
                
                var config = CreateAppConfig(gamePath, selectedCpkName, cfgBinPath, violaCliPath, 
                    tmpDir, modEntries, lastKnownPacksSignature, lastKnownSteamBuildId, 
                    vanillaFallbackUntilUtc, theme, language, lastAppliedProfile);
                
                config.LastAppUpdateCheckUtc = existingConfig.LastAppUpdateCheckUtc;
                config.LastModPrefetchUtc = existingConfig.LastModPrefetchUtc;
                config.LastCpkListCheckUtc = existingConfig.LastCpkListCheckUtc;

                SaveConfigToFile(config);
                return true;
            }
            catch (ConfigurationException)
            {
                throw;
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                HandleFileOperationException(ex, "saving config");
                return false; // This line will never be reached, but satisfies compiler
            }
        }

        /// <summary>
        /// Saves the configuration using individual parameters asynchronously.
        /// </summary>
        /// <param name="gamePath">The path to the game directory.</param>
        /// <param name="selectedCpkName">The name of the selected CPK file.</param>
        /// <param name="cfgBinPath">The path to the CPK list configuration file.</param>
        /// <param name="violaCliPath">The path to the Viola CLI executable.</param>
        /// <param name="tmpDir">The temporary directory path.</param>
        /// <param name="modEntries">The list of mod entries to save.</param>
        /// <param name="lastKnownPacksSignature">The last known packs signature.</param>
        /// <param name="lastKnownSteamBuildId">The last known Steam build ID.</param>
        /// <param name="vanillaFallbackUntilUtc">The UTC date until which vanilla fallback is active.</param>
        /// <param name="theme">The selected theme name.</param>
        /// <param name="language">The selected language code.</param>
        /// <param name="lastAppliedProfile">The name of the last applied profile.</param>
        /// <returns><c>true</c> if the configuration was saved successfully; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="modEntries"/> is null.</exception>
        /// <exception cref="ConfigurationException">Thrown when there's an error writing the configuration file.</exception>
        public async Task<bool> SaveAsync(string gamePath, string selectedCpkName, string cfgBinPath, string violaCliPath, 
            string tmpDir, System.Collections.Generic.List<ModEntry> modEntries,
            string lastKnownPacksSignature, string lastKnownSteamBuildId,
            DateTime vanillaFallbackUntilUtc, string theme, string language, string lastAppliedProfile = "")
        {
            if (modEntries == null)
            {
                throw new ArgumentNullException(nameof(modEntries));
            }

            try
            {
                FileSystemHelper.EnsureDirectoryExists(Path.GetDirectoryName(_configPath));
                
                var existingConfig = await LoadAsync();
                
                var config = CreateAppConfig(gamePath, selectedCpkName, cfgBinPath, violaCliPath, 
                    tmpDir, modEntries, lastKnownPacksSignature, lastKnownSteamBuildId, 
                    vanillaFallbackUntilUtc, theme, language, lastAppliedProfile);
                
                config.LastAppUpdateCheckUtc = existingConfig.LastAppUpdateCheckUtc;
                config.LastModPrefetchUtc = existingConfig.LastModPrefetchUtc;
                config.LastCpkListCheckUtc = existingConfig.LastCpkListCheckUtc;

                await SaveConfigToFileAsync(config);
                return true;
            }
            catch (ConfigurationException)
            {
                throw;
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                HandleFileOperationException(ex, "saving config");
                return false; // This line will never be reached, but satisfies compiler
            }
        }

        private AppConfig CreateAppConfig(string gamePath, string selectedCpkName, string cfgBinPath, 
            string violaCliPath, string tmpDir, System.Collections.Generic.List<ModEntry> modEntries,
            string lastKnownPacksSignature, string lastKnownSteamBuildId,
            DateTime vanillaFallbackUntilUtc, string theme, string language, string lastAppliedProfile)
        {
            return new AppConfig
            {
                GamePath = gamePath,
                SelectedCpkName = selectedCpkName,
                CfgBinPath = cfgBinPath,
                ViolaCliPath = violaCliPath,
                TmpDir = tmpDir,
                LastKnownPacksSignature = lastKnownPacksSignature,
                LastKnownSteamBuildId = lastKnownSteamBuildId,
                VanillaFallbackUntilUtc = vanillaFallbackUntilUtc,
                Theme = theme,
                Language = language,
                LastAppliedProfile = lastAppliedProfile,
                Mods = modEntries.ConvertAll(me => me.ToData())
            };
        }

        private void SaveConfigToFile(AppConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(_configPath, json);
        }

        private async Task SaveConfigToFileAsync(AppConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var json = JsonSerializer.Serialize(config, options);
            await File.WriteAllTextAsync(_configPath, json);
        }

        /// <summary>
        /// Updates the last application update check timestamp in the configuration.
        /// </summary>
        /// <param name="checkTime">The UTC timestamp of when the update check was performed.</param>
        /// <remarks>
        /// This method is provided for backward compatibility. For async contexts, use <see cref="UpdateLastAppUpdateCheckUtcAsync"/> instead.
        /// </remarks>
        public void UpdateLastAppUpdateCheckUtc(DateTime checkTime)
        {
            try
            {
                var config = Load();
                var oldValue = config.LastAppUpdateCheckUtc;
                config.LastAppUpdateCheckUtc = checkTime;
                SaveConfigToFile(config);
                
                Instance.Debug($"Updated LastAppUpdateCheckUtc from {oldValue:yyyy-MM-dd HH:mm:ss} UTC to {checkTime:yyyy-MM-dd HH:mm:ss} UTC", true);
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Warning, "Error updating LastAppUpdateCheckUtc", true, ex);
            }
        }

        /// <summary>
        /// Updates the last application update check timestamp in the configuration (async version).
        /// </summary>
        /// <param name="checkTime">The UTC timestamp of when the update check was performed.</param>
        public async Task UpdateLastAppUpdateCheckUtcAsync(DateTime checkTime)
        {
            try
            {
                var config = await LoadAsync();
                var oldValue = config.LastAppUpdateCheckUtc;
                config.LastAppUpdateCheckUtc = checkTime;
                await SaveConfigToFileAsync(config);
                
                Instance.Debug($"Updated LastAppUpdateCheckUtc from {oldValue:yyyy-MM-dd HH:mm:ss} UTC to {checkTime:yyyy-MM-dd HH:mm:ss} UTC", true);
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Warning, "Error updating LastAppUpdateCheckUtc", true, ex);
            }
        }

        /// <summary>
        /// Updates the last mod prefetch timestamp in the configuration.
        /// </summary>
        /// <param name="prefetchTime">The UTC timestamp of when the mod prefetch was performed.</param>
        public void UpdateLastModPrefetchUtc(DateTime prefetchTime)
        {
            try
            {
                var config = Load();
                config.LastModPrefetchUtc = prefetchTime;
                SaveConfigToFile(config);
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Warning, "Error updating LastModPrefetchUtc", true, ex);
            }
        }

        /// <summary>
        /// Updates the last mod prefetch timestamp in the configuration asynchronously.
        /// </summary>
        /// <param name="prefetchTime">The UTC timestamp of when the mod prefetch was performed.</param>
        public async Task UpdateLastModPrefetchUtcAsync(DateTime prefetchTime)
        {
            try
            {
                var config = await LoadAsync();
                config.LastModPrefetchUtc = prefetchTime;
                await SaveConfigToFileAsync(config);
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Warning, "Error updating LastModPrefetchUtc", true, ex);
            }
        }

        /// <summary>
        /// Handles file operation exceptions by logging and throwing appropriate ConfigurationException.
        /// </summary>
        /// <param name="ex">The exception that occurred.</param>
        /// <param name="operation">A description of the operation that failed.</param>
        private static void HandleFileOperationException(Exception ex, string operation)
        {
            switch (ex)
            {
                case IOException ioEx:
                    Instance.Log(LogLevel.Error, $"IO error {operation}", true, ioEx);
                    throw new ConfigurationException($"Failed to {operation}. Check file permissions.", ioEx);
                case UnauthorizedAccessException uaEx:
                    Instance.Log(LogLevel.Error, $"Access denied {operation}", true, uaEx);
                    throw new ConfigurationException("Access denied to configuration file.", uaEx);
                default:
                    Instance.Log(LogLevel.Error, $"Unexpected error {operation}", true, ex);
                    throw new ConfigurationException($"An unexpected error occurred while {operation}.", ex);
            }
        }

        private void TryDetectGamePathFromSteam(AppConfig config)
        {
            if (config == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(config.GamePath) && Directory.Exists(config.GamePath))
            {
                return;
            }

            try
            {
                var detectedPath = SteamHelper.DetectGamePath();
                if (!string.IsNullOrWhiteSpace(detectedPath) && Directory.Exists(detectedPath))
                {
                    config.GamePath = detectedPath;
                    Instance.Info($"Auto-detected game path from Steam: {detectedPath}", true);
                    
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await SaveConfigToFileAsync(config);
                        }
                        catch (Exception ex)
                        {
                            Instance.Log(LogLevel.Warning, "Error saving auto-detected game path", true, ex);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Debug, "Error detecting game path from Steam", true, ex);
            }
        }

    }
}
