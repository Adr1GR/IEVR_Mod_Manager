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

            // Get all mod directories
            var modNames = Directory.GetDirectories(modsRoot)
                .Select(d => Path.GetFileName(d))
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList();

            // Preserve enabled state from existing entries
            var oldMap = (existingEntries ?? new List<ModEntry>())
                .ToDictionary(me => me.Name, me => me.Enabled);

            // Preserve enabled state from saved config
            var savedMap = (savedMods ?? new List<ModData>())
                .ToDictionary(m => m.Name, m => m.Enabled);

            // Preserve order from saved config
            var savedOrder = (savedMods ?? new List<ModData>())
                .Select(m => m.Name)
                .ToList();

            var orderedNames = savedOrder
                .Where(n => modNames.Contains(n))
                .Concat(modNames.Where(n => !savedOrder.Contains(n)))
                .ToList();

            // Create mod entries
            var modEntries = new List<ModEntry>();
            foreach (var name in orderedNames)
            {
                var modPath = Path.Combine(modsRoot, name);
                var modData = LoadModMetadata(modPath);

                // Determine enabled state (priority: existing > saved > default)
                bool enabled;
                if (oldMap.ContainsKey(name))
                {
                    enabled = oldMap[name];
                }
                else if (savedMap.ContainsKey(name))
                {
                    enabled = savedMap[name];
                }
                else
                {
                    enabled = true;
                }

                var modEntry = new ModEntry(
                    name: name,
                    path: modsRoot,
                    enabled: enabled,
                    displayName: modData.DisplayName,
                    author: modData.Author,
                    modVersion: modData.ModVersion,
                    gameVersion: modData.GameVersion
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
                GameVersion = string.Empty
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
                var dataPath = Path.Combine(mod.FullPath, "data");
                if (!Directory.Exists(dataPath))
                {
                    continue;
                }

                var files = Directory.GetFiles(dataPath, "*", SearchOption.AllDirectories);
                var touchesPacks = files.Any(filePath =>
                {
                    var relativePath = Path.GetRelativePath(dataPath, filePath)
                        .Replace('\\', '/');
                    return relativePath.StartsWith("packs/", StringComparison.OrdinalIgnoreCase);
                });

                if (touchesPacks)
                {
                    var displayName = string.IsNullOrWhiteSpace(mod.DisplayName) ? mod.Name : mod.DisplayName;
                    modsTouchingPacks.Add(displayName);
                }
            }

            return modsTouchingPacks
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
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

            // Dictionary to track which mods have each file
            var fileToModsMap = new Dictionary<string, HashSet<string>>();

            foreach (var mod in enabledMods)
            {
                var modDataPath = Path.Combine(mod.FullPath, "data");
                if (!Directory.Exists(modDataPath))
                {
                    continue;
                }

                // Get all files in the mod's data folder
                var files = Directory.GetFiles(modDataPath, "*", SearchOption.AllDirectories);
                
                foreach (var filePath in files)
                {
                    // Get relative path from the data folder
                    var relativePath = Path.GetRelativePath(modDataPath, filePath);
                    // Normalize path separators for consistency
                    relativePath = relativePath.Replace('\\', '/');

                    if (!fileToModsMap.ContainsKey(relativePath))
                    {
                        fileToModsMap[relativePath] = new HashSet<string>();
                    }

                    fileToModsMap[relativePath].Add(mod.DisplayName);
                }
            }

            // Find files that are in multiple mods
            foreach (var kvp in fileToModsMap)
            {
                if (kvp.Key.Equals("cpk_list.cfg.bin", StringComparison.OrdinalIgnoreCase))
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
        }
    }
}

