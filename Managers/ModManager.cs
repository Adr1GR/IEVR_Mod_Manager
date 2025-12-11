using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using IEVRModManager.Models;

namespace IEVRModManager.Managers
{
    public class ModManager
    {
        private readonly string _modsDir;

        public ModManager(string? modsDir = null)
        {
            _modsDir = modsDir ?? Config.DefaultModsDir;
        }

        public List<ModEntry> ScanMods(List<ModData>? savedMods = null, 
            List<ModEntry>? existingEntries = null)
        {
            var modsRoot = Path.GetFullPath(_modsDir);
            Directory.CreateDirectory(modsRoot);

            var modNames = GetModDirectoryNames(modsRoot);
            var enabledStateMap = BuildEnabledStateMap(savedMods, existingEntries);
            var orderedNames = DetermineModOrder(modNames, savedMods, existingEntries);

            return CreateModEntries(orderedNames, modsRoot, enabledStateMap);
        }

        private List<string> GetModDirectoryNames(string modsRoot)
        {
            return Directory.GetDirectories(modsRoot)
                .Select(d => Path.GetFileName(d))
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList();
        }

        private Dictionary<string, bool> BuildEnabledStateMap(List<ModData>? savedMods, List<ModEntry>? existingEntries)
        {
            var stateMap = new Dictionary<string, bool>();

            // Priority: existing entries > saved config > default (true)
            if (existingEntries != null)
            {
                foreach (var entry in existingEntries)
                {
                    stateMap[entry.Name] = entry.Enabled;
                }
            }

            if (savedMods != null)
            {
                foreach (var mod in savedMods)
                {
                    if (!stateMap.ContainsKey(mod.Name))
                    {
                        stateMap[mod.Name] = mod.Enabled;
                    }
                }
            }

            return stateMap;
        }

        private List<string> DetermineModOrder(List<string> modNames, List<ModData>? savedMods, List<ModEntry>? existingEntries)
        {
            if (existingEntries != null && existingEntries.Count > 0)
            {
                // Use the current order of existing entries
                var existingOrder = existingEntries
                    .Select(me => me.Name)
                    .Where(modNames.Contains)
                    .ToList();
                
                // Add new mods at the end
                var newMods = modNames
                    .Where(n => !existingOrder.Contains(n))
                    .ToList();
                
                return existingOrder.Concat(newMods).ToList();
            }

            // Fall back to saved config order if no existing entries
            var savedOrder = (savedMods ?? new List<ModData>())
                .Select(m => m.Name)
                .ToList();

            return savedOrder
                .Where(modNames.Contains)
                .Concat(modNames.Where(n => !savedOrder.Contains(n)))
                .ToList();
        }

        private List<ModEntry> CreateModEntries(List<string> orderedNames, string modsRoot, Dictionary<string, bool> enabledStateMap)
        {
            var modEntries = new List<ModEntry>();
            
            foreach (var name in orderedNames)
            {
                var modPath = Path.Combine(modsRoot, name);
                var metadata = LoadModMetadata(modPath);
                var enabled = enabledStateMap.GetValueOrDefault(name, true);

                var modEntry = new ModEntry(
                    name: name,
                    path: modsRoot,
                    enabled: enabled,
                    displayName: metadata.DisplayName,
                    author: metadata.Author,
                    modVersion: metadata.ModVersion,
                    gameVersion: metadata.GameVersion,
                    modLink: metadata.ModLink
                );
                modEntries.Add(modEntry);
            }

            return modEntries;
        }

        private ModMetadata LoadModMetadata(string modPath)
        {
            var modDataPath = Path.Combine(modPath, "mod_data.json");
            var metadata = new ModMetadata
            {
                DisplayName = Path.GetFileName(modPath),
                Author = string.Empty,
                ModVersion = string.Empty,
                GameVersion = string.Empty,
                ModLink = string.Empty
            };

            if (!File.Exists(modDataPath))
            {
                return metadata;
            }

            try
            {
                var json = File.ReadAllText(modDataPath);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

                if (data != null)
                {
                    if (data.TryGetValue("Name", out var nameElement))
                        metadata.DisplayName = nameElement.GetString() ?? metadata.DisplayName;

                    if (data.TryGetValue("Author", out var authorElement))
                        metadata.Author = authorElement.GetString() ?? string.Empty;

                    if (data.TryGetValue("ModVersion", out var versionElement))
                        metadata.ModVersion = versionElement.GetString() ?? string.Empty;

                    if (data.TryGetValue("GameVersion", out var gameVersionElement))
                        metadata.GameVersion = gameVersionElement.GetString() ?? string.Empty;

                    if (data.TryGetValue("ModLink", out var modLinkElement))
                        metadata.ModLink = modLinkElement.GetString() ?? string.Empty;
                }
            }
            catch
            {
                // Ignore errors, return default metadata
            }

            return metadata;
        }

        public List<string> GetEnabledMods(List<ModEntry> modEntries)
        {
            return modEntries
                .Where(me => me.Enabled)
                .Select(me => me.FullPath)
                .ToList();
        }

        public List<string> DetectPacksModifiers(List<ModEntry> modEntries)
        {
            var modsTouchingPacks = new List<string>();
            
            foreach (var mod in modEntries.Where(me => me.Enabled))
            {
                if (ModTouchesPacksFolder(mod))
                {
                    var displayName = string.IsNullOrWhiteSpace(mod.DisplayName) ? mod.Name : mod.DisplayName;
                    modsTouchingPacks.Add(displayName);
                }
            }

            return modsTouchingPacks
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private bool ModTouchesPacksFolder(ModEntry mod)
        {
            var dataPath = Path.Combine(mod.FullPath, "data");
            if (!Directory.Exists(dataPath))
            {
                return false;
            }

            var files = Directory.GetFiles(dataPath, "*", SearchOption.AllDirectories);
            return files.Any(filePath =>
            {
                var relativePath = Path.GetRelativePath(dataPath, filePath)
                    .Replace('\\', '/');
                return relativePath.StartsWith("packs/", StringComparison.OrdinalIgnoreCase);
            });
        }

        public Dictionary<string, List<string>> DetectFileConflicts(List<ModEntry> modEntries)
        {
            var conflicts = new Dictionary<string, List<string>>();
            var enabledMods = modEntries.Where(me => me.Enabled).ToList();

            if (enabledMods.Count < 2)
            {
                // No conflicts if less than 2 mods are enabled
                return conflicts;
            }

            var fileToModsMap = BuildFileToModsMap(enabledMods);
            return ExtractConflicts(fileToModsMap);
        }

        private Dictionary<string, HashSet<string>> BuildFileToModsMap(List<ModEntry> enabledMods)
        {
            var fileToModsMap = new Dictionary<string, HashSet<string>>();

            foreach (var mod in enabledMods)
            {
                var modDataPath = Path.Combine(mod.FullPath, "data");
                if (!Directory.Exists(modDataPath))
                {
                    continue;
                }

                var files = Directory.GetFiles(modDataPath, "*", SearchOption.AllDirectories);
                
                foreach (var filePath in files)
                {
                    var relativePath = Path.GetRelativePath(modDataPath, filePath)
                        .Replace('\\', '/');

                    if (!fileToModsMap.ContainsKey(relativePath))
                    {
                        fileToModsMap[relativePath] = new HashSet<string>();
                    }

                    fileToModsMap[relativePath].Add(mod.DisplayName);
                }
            }

            return fileToModsMap;
        }

        private Dictionary<string, List<string>> ExtractConflicts(Dictionary<string, HashSet<string>> fileToModsMap)
        {
            var conflicts = new Dictionary<string, List<string>>();
            const string cpkListFileName = "cpk_list.cfg.bin";

            foreach (var kvp in fileToModsMap)
            {
                // Skip cpk_list.cfg.bin as it's expected to be in multiple mods
                if (kvp.Key.Equals(cpkListFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                
                if (kvp.Value.Count > 1)
                {
                    conflicts[kvp.Key] = kvp.Value.OrderBy(m => m).ToList();
                }
            }

            return conflicts;
        }

        private class ModMetadata
        {
            public string DisplayName { get; set; } = string.Empty;
            public string Author { get; set; } = string.Empty;
            public string ModVersion { get; set; } = string.Empty;
            public string GameVersion { get; set; } = string.Empty;
            public string ModLink { get; set; } = string.Empty;
        }
    }
}

