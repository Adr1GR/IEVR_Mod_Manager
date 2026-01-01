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

            migrated |= MigrateStringProperty(root, "TmpDir", () => loadedConfig.TmpDir, 
                value => loadedConfig.TmpDir = value, defaultConfig.TmpDir);
            migrated |= MigrateStringProperty(root, "Theme", () => loadedConfig.Theme, 
                value => loadedConfig.Theme = value, defaultConfig.Theme);
            migrated |= MigrateStringProperty(root, "Language", () => loadedConfig.Language, 
                value => loadedConfig.Language = value, defaultConfig.Language);
            migrated |= MigrateStringProperty(root, "LastAppliedProfile", () => loadedConfig.LastAppliedProfile, 
                value => loadedConfig.LastAppliedProfile = value, defaultConfig.LastAppliedProfile);

            migrated |= MigrateDateTimeProperty(root, "LastCpkListCheckUtc", () => loadedConfig.LastCpkListCheckUtc, 
                value => loadedConfig.LastCpkListCheckUtc = value, defaultConfig.LastCpkListCheckUtc);
            migrated |= MigrateDateTimeProperty(root, "LastAppUpdateCheckUtc", () => loadedConfig.LastAppUpdateCheckUtc, 
                value => loadedConfig.LastAppUpdateCheckUtc = value, defaultConfig.LastAppUpdateCheckUtc);
            migrated |= MigrateDateTimeProperty(root, "LastModPrefetchUtc", () => loadedConfig.LastModPrefetchUtc, 
                value => loadedConfig.LastModPrefetchUtc = value, defaultConfig.LastModPrefetchUtc);
            migrated |= MigrateBooleanProperty(root, "ShowTechnicalLogs", () => loadedConfig.ShowTechnicalLogs, 
                value => loadedConfig.ShowTechnicalLogs = value, defaultConfig.ShowTechnicalLogs);

            if (!root.TryGetProperty("Mods", out _) || loadedConfig.Mods == null)
            {
                loadedConfig.Mods = new List<ModData>();
                migrated = true;
            }

            var stringProperties = new[]
            {
                new { Getter = (Func<string?>)(() => loadedConfig.GamePath), Setter = (Action<string>)(v => loadedConfig.GamePath = v) },
                new { Getter = (Func<string?>)(() => loadedConfig.CfgBinPath), Setter = (Action<string>)(v => loadedConfig.CfgBinPath = v) },
                new { Getter = (Func<string?>)(() => loadedConfig.ViolaCliPath), Setter = (Action<string>)(v => loadedConfig.ViolaCliPath = v) },
                new { Getter = (Func<string?>)(() => loadedConfig.SelectedCpkName), Setter = (Action<string>)(v => loadedConfig.SelectedCpkName = v) },
                new { Getter = (Func<string?>)(() => loadedConfig.LastKnownPacksSignature), Setter = (Action<string>)(v => loadedConfig.LastKnownPacksSignature = v) },
                new { Getter = (Func<string?>)(() => loadedConfig.LastKnownSteamBuildId), Setter = (Action<string>)(v => loadedConfig.LastKnownSteamBuildId = v) }
            };

            foreach (var prop in stringProperties)
            {
                if (prop.Getter() == null)
                {
                    prop.Setter(string.Empty);
                    migrated = true;
                }
            }

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

        /// <summary>
        /// Saves the configuration using an AppConfig object directly.
        /// </summary>
        /// <param name="config">The configuration object to save.</param>
        /// <returns><c>true</c> if the configuration was saved successfully; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is null.</exception>
        /// <exception cref="ConfigurationException">Thrown when there's an error writing the configuration file.</exception>
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
            catch (IOException ex)
            {
                Instance.Log(LogLevel.Error, "IO error saving config", true, ex);
                throw new ConfigurationException("Failed to write configuration file. Check file permissions.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                Instance.Log(LogLevel.Error, "Access denied saving config", true, ex);
                throw new ConfigurationException("Access denied to configuration file.", ex);
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Error, "Unexpected error saving config", true, ex);
                throw new ConfigurationException("An unexpected error occurred while saving configuration.", ex);
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
            catch (IOException ex)
            {
                Instance.Log(LogLevel.Error, "IO error saving config", true, ex);
                throw new ConfigurationException("Failed to write configuration file. Check file permissions.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                Instance.Log(LogLevel.Error, "Access denied saving config", true, ex);
                throw new ConfigurationException("Access denied to configuration file.", ex);
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Error, "Unexpected error saving config", true, ex);
                throw new ConfigurationException("An unexpected error occurred while saving configuration.", ex);
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

        /// <summary>
        /// Updates the last application update check timestamp in the configuration.
        /// </summary>
        /// <param name="checkTime">The UTC timestamp of when the update check was performed.</param>
        public void UpdateLastAppUpdateCheckUtc(DateTime checkTime)
        {
            // Use async version but run synchronously for backward compatibility
            UpdateLastAppUpdateCheckUtcAsync(checkTime).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Updates the last application update check timestamp in the configuration (async version).
        /// </summary>
        /// <param name="checkTime">The UTC timestamp of when the update check was performed.</param>
        public async Task UpdateLastAppUpdateCheckUtcAsync(DateTime checkTime)
        {
            try
            {
                var config = Load();
                var oldValue = config.LastAppUpdateCheckUtc;
                config.LastAppUpdateCheckUtc = checkTime;
                SaveConfigToFile(config);
                
                // Verify the file was written correctly by reading it back
                // Use retry logic instead of Thread.Sleep to handle potential file system delays
                const int maxRetries = 5;
                const int retryDelayMs = 10;
                bool verified = false;
                
                for (int i = 0; i < maxRetries && !verified; i++)
                {
                    if (i > 0)
                    {
                        await Task.Delay(retryDelayMs);
                    }
                    
                    if (File.Exists(_configPath))
                    {
                        try
                        {
                            var json = File.ReadAllText(_configPath);
                            using var jsonDoc = JsonDocument.Parse(json);
                            if (jsonDoc.RootElement.TryGetProperty("LastAppUpdateCheckUtc", out var prop))
                            {
                                var savedValue = prop.GetDateTime();
                                if (savedValue == checkTime)
                                {
                                    Instance.Debug($"Updated LastAppUpdateCheckUtc from {oldValue:yyyy-MM-dd HH:mm:ss} UTC to {checkTime:yyyy-MM-dd HH:mm:ss} UTC. File contains: {savedValue:yyyy-MM-dd HH:mm:ss} UTC", true);
                                    verified = true;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // If we're on the last retry, log the warning
                            if (i == maxRetries - 1)
                            {
                                Instance.Warning($"Could not verify LastAppUpdateCheckUtc after {maxRetries} attempts: {ex.Message}", true);
                            }
                        }
                    }
                }
                
                if (!verified)
                {
                    Instance.Warning("LastAppUpdateCheckUtc property not found or incorrect in saved config file after verification attempts!", true);
                }
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
        /// Attempts to detect the game path from Steam if it's not already configured.
        /// </summary>
        /// <param name="config">The configuration object to update.</param>
        private void TryDetectGamePathFromSteam(AppConfig config)
        {
            if (config == null)
            {
                return;
            }

            // Only detect if GamePath is empty or invalid
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
                    
                    // Save the detected path immediately
                    try
                    {
                        SaveConfigToFile(config);
                    }
                    catch (Exception ex)
                    {
                        Instance.Log(LogLevel.Warning, "Error saving auto-detected game path", true, ex);
                    }
                }
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Debug, "Error detecting game path from Steam", true, ex);
            }
        }

    }
}
