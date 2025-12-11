using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using IEVRModManager.Models;

namespace IEVRModManager.Managers
{
    public class ConfigManager
    {
        private readonly string _configPath;

        public ConfigManager()
        {
            _configPath = Config.ConfigPath;
            EnsureDirectoryExists(Path.GetDirectoryName(_configPath));
        }

        public AppConfig Load()
        {
            if (!File.Exists(_configPath))
            {
                return AppConfig.Default();
            }

            try
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                if (config == null)
                {
                    return AppConfig.Default();
                }

                // Migrate configuration to ensure all new properties are added
                // This detects missing properties from old config files and adds them with default values
                // The migrated config is automatically saved if changes are detected
                return MigrateConfig(config, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading config: {ex.Message}");
                return AppConfig.Default();
            }
        }

        private AppConfig MigrateConfig(AppConfig loadedConfig, string originalJson)
        {
            var defaultConfig = AppConfig.Default();
            var migrated = false;

            // Check if JSON contains new properties by parsing it
            using var jsonDoc = JsonDocument.Parse(originalJson);
            var root = jsonDoc.RootElement;

            // Migrate properties that might be missing from old config files
            // Check TmpDir - new property that might not exist in old configs
            if (!root.TryGetProperty("TmpDir", out _) || string.IsNullOrWhiteSpace(loadedConfig.TmpDir))
            {
                loadedConfig.TmpDir = defaultConfig.TmpDir;
                migrated = true;
            }

            // Check Theme - new property that might not exist in old configs
            if (!root.TryGetProperty("Theme", out _) || string.IsNullOrWhiteSpace(loadedConfig.Theme))
            {
                loadedConfig.Theme = defaultConfig.Theme;
                migrated = true;
            }

            // Check Language - new property that might not exist in old configs
            if (!root.TryGetProperty("Language", out _) || string.IsNullOrWhiteSpace(loadedConfig.Language))
            {
                loadedConfig.Language = defaultConfig.Language;
                migrated = true;
            }

            // Check LastAppliedProfile - new property that might not exist in old configs
            if (!root.TryGetProperty("LastAppliedProfile", out _) || string.IsNullOrWhiteSpace(loadedConfig.LastAppliedProfile))
            {
                loadedConfig.LastAppliedProfile = defaultConfig.LastAppliedProfile;
                migrated = true;
            }

            // Ensure Mods list is initialized if null or missing
            if (!root.TryGetProperty("Mods", out _) || loadedConfig.Mods == null)
            {
                loadedConfig.Mods = new List<ModData>();
                migrated = true;
            }

            // Ensure other string properties are not null (defensive check for corrupted configs)
            if (loadedConfig.GamePath == null)
            {
                loadedConfig.GamePath = string.Empty;
                migrated = true;
            }

            if (loadedConfig.CfgBinPath == null)
            {
                loadedConfig.CfgBinPath = string.Empty;
                migrated = true;
            }

            if (loadedConfig.ViolaCliPath == null)
            {
                loadedConfig.ViolaCliPath = string.Empty;
                migrated = true;
            }

            if (loadedConfig.SelectedCpkName == null)
            {
                loadedConfig.SelectedCpkName = string.Empty;
                migrated = true;
            }

            if (loadedConfig.LastKnownPacksSignature == null)
            {
                loadedConfig.LastKnownPacksSignature = string.Empty;
                migrated = true;
            }

            if (loadedConfig.LastKnownSteamBuildId == null)
            {
                loadedConfig.LastKnownSteamBuildId = string.Empty;
                migrated = true;
            }

            // Save migrated config if any changes were made
            if (migrated)
            {
                SaveMigratedConfig(loadedConfig);
            }

            return loadedConfig;
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
                System.Diagnostics.Debug.WriteLine("Configuration migrated and saved successfully.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving migrated config: {ex.Message}");
            }
        }

        public bool Save(string gamePath, string selectedCpkName, string cfgBinPath, string violaCliPath, 
            string tmpDir, System.Collections.Generic.List<ModEntry> modEntries,
            string lastKnownPacksSignature, string lastKnownSteamBuildId,
            DateTime vanillaFallbackUntilUtc, string theme, string language, string lastAppliedProfile = "")
        {
            try
            {
                EnsureDirectoryExists(Path.GetDirectoryName(_configPath));
                var config = CreateAppConfig(gamePath, selectedCpkName, cfgBinPath, violaCliPath, 
                    tmpDir, modEntries, lastKnownPacksSignature, lastKnownSteamBuildId, 
                    vanillaFallbackUntilUtc, theme, language, lastAppliedProfile);

                SaveConfigToFile(config);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving config: {ex.Message}");
                return false;
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
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(_configPath, json);
        }

        private static void EnsureDirectoryExists(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
    }
}

