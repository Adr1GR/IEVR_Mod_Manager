using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using YamlDotNet.Serialization;
using static IEVRModManager.Helpers.Logger;

namespace IEVRModManager.Helpers
{
    /// <summary>
    /// Provides localization support for the application, loading strings from YAML files.
    /// </summary>
    public static class LocalizationHelper
    {
        private static Dictionary<string, string>? _currentStrings;
        private static Dictionary<string, Dictionary<string, string>>? _allStrings;
        private static CultureInfo? _currentCulture;
        private static readonly object _lockObject = new object();

        static LocalizationHelper()
        {
            LoadStrings();
        }

        private static void LoadStrings()
        {
            lock (_lockObject)
            {
                _allStrings = new Dictionary<string, Dictionary<string, string>>();
                
                try
                {
                    var resourcesDir = GetResourcesDirectory();
                    bool loadedFromFiles = TryLoadFromFileSystem(resourcesDir);

                    if (!loadedFromFiles)
                    {
                        TryLoadFromEmbeddedResources();
                    }

                    if (_allStrings.Count == 0)
                    {
                        _allStrings["en-US"] = new Dictionary<string, string>();
                    }
                }
                catch (Exception ex)
                {
                    Instance.Log(LogLevel.Warning, "Error loading localization files", true, ex);
                    _allStrings["en-US"] = new Dictionary<string, string>();
                }
            }
        }

        private static string GetResourcesDirectory()
        {
            var baseDir = AppContext.BaseDirectory;
            var resourcesDir = Path.Combine(baseDir, "Resources");
            
            if (!Directory.Exists(resourcesDir))
            {
                var fallbackDir = AppDomain.CurrentDomain.BaseDirectory;
                var fallbackResourcesDir = Path.Combine(fallbackDir, "Resources");
                if (Directory.Exists(fallbackResourcesDir))
                {
                    resourcesDir = fallbackResourcesDir;
                }
            }

            return resourcesDir;
        }

        private static bool TryLoadFromFileSystem(string resourcesDir)
        {
            if (_allStrings == null)
            {
                return false;
            }

            var defaultPath = Path.Combine(resourcesDir, "Strings.yaml");
            var spanishPath = Path.Combine(resourcesDir, "Strings.es-ES.yaml");
            var frenchPath = Path.Combine(resourcesDir, "Strings.fr-FR.yaml");
            bool loadedAny = false;

            if (File.Exists(defaultPath))
            {
                _allStrings["en-US"] = LoadYamlFile(defaultPath);
                loadedAny = true;
            }

            if (File.Exists(spanishPath))
            {
                _allStrings["es-ES"] = LoadYamlFile(spanishPath);
                loadedAny = true;
            }

            if (File.Exists(frenchPath))
            {
                _allStrings["fr-FR"] = LoadYamlFile(frenchPath);
                loadedAny = true;
            }

            return loadedAny;
        }

        private static void TryLoadFromEmbeddedResources()
        {
            if (_allStrings == null)
            {
                return;
            }

            var englishResource = LoadYamlFromEmbeddedResource("IEVRModManager.Resources.Strings.yaml");
            if (englishResource != null && englishResource.Count > 0)
            {
                _allStrings["en-US"] = englishResource;
            }

            var spanishResource = LoadYamlFromEmbeddedResource("IEVRModManager.Resources.Strings.es-ES.yaml");
            if (spanishResource != null && spanishResource.Count > 0)
            {
                _allStrings["es-ES"] = spanishResource;
            }

            var frenchResource = LoadYamlFromEmbeddedResource("IEVRModManager.Resources.Strings.fr-FR.yaml");
            if (frenchResource != null && frenchResource.Count > 0)
            {
                _allStrings["fr-FR"] = frenchResource;
            }
        }

        private static Dictionary<string, string>? LoadYamlFromEmbeddedResource(string resourceName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        return LoadYamlFromStream(stream);
                    }
                }

                var alternativeNames = new[]
                {
                    resourceName.Replace("IEVRModManager.", ""),
                    resourceName.Replace("IEVRModManager.Resources.", "Resources."),
                    resourceName.Replace("IEVRModManager.Resources.", ""),
                    resourceName.Replace("IEVRModManager.", "IEVRModManager.Resources.")
                };
                
                foreach (var altName in alternativeNames)
                {
                    using (var altStream = assembly.GetManifestResourceStream(altName))
                    {
                        if (altStream != null)
                        {
                            return LoadYamlFromStream(altStream);
                        }
                    }
                }
                
                var fileName = Path.GetFileName(resourceName);
                foreach (var resName in assembly.GetManifestResourceNames())
                {
                    if (resName.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        using (var foundStream = assembly.GetManifestResourceStream(resName))
                        {
                            if (foundStream != null)
                            {
                                return LoadYamlFromStream(foundStream);
                            }
                        }
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Warning, $"Error loading embedded resource '{resourceName}'", true, ex);
                return null;
            }
        }

        private static Dictionary<string, string> LoadYamlFromStream(Stream stream)
        {
            try
            {
                using (var reader = new StreamReader(stream))
                {
                    var yamlContent = reader.ReadToEnd();
                    var deserializer = new DeserializerBuilder()
                        .Build();
                    
                    var yamlDict = deserializer.Deserialize<Dictionary<string, string>>(yamlContent);
                    return yamlDict ?? new Dictionary<string, string>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading YAML from stream: {ex.Message}");
                return new Dictionary<string, string>();
            }
        }

        private static Dictionary<string, string> LoadYamlFile(string filePath)
        {
            try
            {
                var yamlContent = File.ReadAllText(filePath);
                var deserializer = new DeserializerBuilder()
                    .Build();
                
                var yamlDict = deserializer.Deserialize<Dictionary<string, string>>(yamlContent);
                return yamlDict ?? new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Warning, $"Error loading YAML file {filePath}", true, ex);
                return new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// Sets the application language and loads the corresponding localization strings.
        /// </summary>
        /// <param name="languageCode">The language code (e.g., "en-US", "es-ES") or "System" to use system default.</param>
        public static void SetLanguage(string languageCode)
        {
            try
            {
                var (culture, langKey) = DetermineCulture(languageCode);
                SetCulture(culture);

                lock (_lockObject)
                {
                    EnsureStringsLoaded();
                    _currentStrings = GetStringsForLanguage(langKey);
                }
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Warning, "Error in SetLanguage", true, ex);
                _currentCulture = CultureInfo.CurrentUICulture;
                lock (_lockObject)
                {
                    EnsureStringsLoaded();
                    _currentStrings = GetStringsForLanguage("en-US");
                }
            }
        }

        private static (CultureInfo culture, string langKey) DetermineCulture(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode) || languageCode == "System")
            {
                var culture = CultureInfo.CurrentUICulture;
                return (culture, culture.Name);
            }
            else
            {
                var culture = new CultureInfo(languageCode);
                return (culture, languageCode);
            }
        }

        private static void SetCulture(CultureInfo culture)
        {
            _currentCulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }

        private static void EnsureStringsLoaded()
        {
            if (_allStrings == null)
            {
                LoadStrings();
            }
        }

        private static Dictionary<string, string> GetStringsForLanguage(string langKey)
        {
            if (_allStrings == null)
            {
                return new Dictionary<string, string>();
            }

            if (_allStrings.TryGetValue(langKey, out var strings))
            {
                return strings;
            }

            return _allStrings.TryGetValue("en-US", out var defaultStrings) 
                ? defaultStrings 
                : new Dictionary<string, string>();
        }

        /// <summary>
        /// Gets a localized string by key.
        /// </summary>
        /// <param name="key">The localization key to retrieve.</param>
        /// <returns>The localized string, or the key itself if not found.</returns>
        public static string GetString(string key)
        {
            try
            {
                lock (_lockObject)
                {
                    EnsureStringsLoaded();
                    EnsureCurrentStringsInitialized();

                    if (_currentStrings != null && _currentStrings.TryGetValue(key, out var value))
                    {
                        return value;
                    }

                    if (_allStrings != null && _allStrings.TryGetValue("en-US", out var defaultStrings))
                    {
                        if (defaultStrings.TryGetValue(key, out var defaultValue))
                        {
                            return defaultValue;
                        }
                    }

                    return key;
                }
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Warning, $"Error in GetString('{key}')", true, ex);
                return key;
            }
        }

        private static void EnsureCurrentStringsInitialized()
        {
            if (_currentStrings == null)
            {
                EnsureStringsLoaded();
                _currentStrings = GetStringsForLanguage("en-US");
            }
        }

        /// <summary>
        /// Gets a localized string by key and formats it with the provided arguments.
        /// </summary>
        /// <param name="key">The localization key to retrieve.</param>
        /// <param name="args">Arguments to format into the localized string.</param>
        /// <returns>The formatted localized string, or the key itself if formatting fails.</returns>
        public static string GetString(string key, params object[] args)
        {
            try
            {
                var format = GetString(key);
                return string.Format(format, args);
            }
            catch
            {
                return key;
            }
        }
    }
}

