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
            var germanPath = Path.Combine(resourcesDir, "Strings.de-DE.yaml");
            var japanesePath = Path.Combine(resourcesDir, "Strings.ja-JP.yaml");
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

            if (File.Exists(germanPath))
            {
                var germanStrings = LoadYamlFile(germanPath);
                if (germanStrings != null && germanStrings.Count > 0)
                {
                    _allStrings["de-DE"] = germanStrings;
                    Instance.Log(LogLevel.Info, $"Successfully loaded {germanStrings.Count} German strings from {germanPath} and stored with key 'de-DE'", true);
                    // Verify the key was stored correctly
                    if (_allStrings.ContainsKey("de-DE"))
                    {
                        Instance.Log(LogLevel.Debug, $"Verified: 'de-DE' key exists in _allStrings with {_allStrings["de-DE"].Count} entries", true);
                    }
                    else
                    {
                        Instance.Log(LogLevel.Error, $"ERROR: 'de-DE' key was NOT stored in _allStrings!", true);
                    }
                }
                else
                {
                    Instance.Log(LogLevel.Warning, $"German file {germanPath} exists but loaded 0 strings", true);
                }
                loadedAny = true;
            }
            else
            {
                Instance.Log(LogLevel.Warning, $"German file not found at {germanPath}", true);
            }

            if (File.Exists(japanesePath))
            {
                var japaneseStrings = LoadYamlFile(japanesePath);
                if (japaneseStrings != null && japaneseStrings.Count > 0)
                {
                    _allStrings["ja-JP"] = japaneseStrings;
                    Instance.Log(LogLevel.Info, $"Successfully loaded {japaneseStrings.Count} Japanese strings from {japanesePath} and stored with key 'ja-JP'", true);
                }
                else
                {
                    Instance.Log(LogLevel.Warning, $"Japanese file {japanesePath} exists but loaded 0 strings", true);
                }
                loadedAny = true;
            }
            else
            {
                Instance.Log(LogLevel.Debug, $"Japanese file not found at {japanesePath}", true);
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

            var germanResource = LoadYamlFromEmbeddedResource("IEVRModManager.Resources.Strings.de-DE.yaml");
            if (germanResource != null && germanResource.Count > 0)
            {
                _allStrings["de-DE"] = germanResource;
                Instance.Log(LogLevel.Debug, $"Loaded {germanResource.Count} German strings from embedded resource", true);
            }

            var japaneseResource = LoadYamlFromEmbeddedResource("IEVRModManager.Resources.Strings.ja-JP.yaml");
            if (japaneseResource != null && japaneseResource.Count > 0)
            {
                _allStrings["ja-JP"] = japaneseResource;
                Instance.Log(LogLevel.Debug, $"Loaded {japaneseResource.Count} Japanese strings from embedded resource", true);
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
                // Try UTF-8 first, then fallback to default encoding
                // Note: Using synchronous methods here because this is called from static constructor
                // during initialization, where async would cause deadlocks
                string yamlContent;
                try
                {
                    yamlContent = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                }
                catch
                {
                    yamlContent = File.ReadAllText(filePath);
                }
                
                var deserializer = new DeserializerBuilder()
                    .Build();
                
                var yamlDict = deserializer.Deserialize<Dictionary<string, string>>(yamlContent);
                
                if (yamlDict == null || yamlDict.Count == 0)
                {
                    Instance.Log(LogLevel.Warning, $"YAML file {filePath} loaded but contains no entries", true);
                    return new Dictionary<string, string>();
                }
                
                Instance.Log(LogLevel.Debug, $"Successfully loaded {yamlDict.Count} entries from {filePath}", true);
                return yamlDict;
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Warning, $"Error loading YAML file {filePath}", true, ex);
                Instance.Log(LogLevel.Debug, $"Exception details: {ex.GetType().Name} - {ex.Message}", true);
                if (ex.InnerException != null)
                {
                    Instance.Log(LogLevel.Debug, $"Inner exception: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}", true);
                }
                return new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// Sets the application language and loads the corresponding localization strings.
        /// </summary>
        /// <param name="languageCode">The language code (e.g., "en-US", "es-ES") or "System" to use system default.</param>
        public static void SetLanguage(string languageCode)
        {
            Instance.Log(LogLevel.Info, $"SetLanguage called with languageCode: '{languageCode}'", true);
            try
            {
                var (culture, langKey) = DetermineCulture(languageCode);
                Instance.Log(LogLevel.Info, $"DetermineCulture returned langKey: '{langKey}', culture: '{culture.Name}'", true);
                
                // Try to set culture, but don't fail if it's not available on the system
                try
                {
                    SetCulture(culture);
                    Instance.Log(LogLevel.Debug, $"Successfully set culture to '{culture.Name}'", true);
                }
                catch (Exception cultureEx)
                {
                    Instance.Log(LogLevel.Debug, $"Could not set culture '{languageCode}', using language code directly", true, cultureEx);
                    // Continue with the language code even if culture setting fails
                }

                lock (_lockObject)
                {
                    EnsureStringsLoaded();
                    
                    // Log available language keys before getting strings
                    if (_allStrings != null)
                    {
                        Instance.Log(LogLevel.Debug, $"Available language keys in _allStrings: {string.Join(", ", _allStrings.Keys)}", true);
                    }
                    
                    _currentStrings = GetStringsForLanguage(langKey);
                    
                    // Verify that we got the correct language strings
                    if (_currentStrings != null && _currentStrings.Count > 0)
                    {
                        Instance.Log(LogLevel.Info, $"Successfully loaded {_currentStrings.Count} strings for language '{langKey}'", true);
                        // Test getting a specific string
                        var testString = _currentStrings.ContainsKey("AppTitle") ? _currentStrings["AppTitle"] : "NOT FOUND";
                        Instance.Log(LogLevel.Debug, $"Test: AppTitle = '{testString}'", true);
                    }
                    else
                    {
                        Instance.Log(LogLevel.Warning, $"No strings loaded for language '{langKey}', falling back to en-US", true);
                        _currentStrings = GetStringsForLanguage("en-US");
                    }
                }
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Warning, "Error in SetLanguage", true, ex);
                Instance.Log(LogLevel.Debug, $"Exception type: {ex.GetType().Name}, Message: {ex.Message}", true);
                if (ex.InnerException != null)
                {
                    Instance.Log(LogLevel.Debug, $"Inner exception: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}", true);
                }
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
                // Try to create the culture, but fall back to a neutral culture if specific one fails
                CultureInfo culture;
                try
                {
                    culture = new CultureInfo(languageCode);
                }
                catch
                {
                    // If specific culture fails (e.g., "de-DE" not installed), try neutral culture (e.g., "de")
                    try
                    {
                        var neutralCode = languageCode.Split('-')[0];
                        culture = new CultureInfo(neutralCode);
                    }
                    catch
                    {
                        // If even neutral fails, use InvariantCulture but keep the original language code for string lookup
                        culture = CultureInfo.InvariantCulture;
                    }
                }
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
                Instance.Log(LogLevel.Warning, "GetStringsForLanguage: _allStrings is null", true);
                return new Dictionary<string, string>();
            }

            Instance.Log(LogLevel.Debug, $"GetStringsForLanguage: Looking for language key '{langKey}'. Available keys: {string.Join(", ", _allStrings.Keys)}", true);

            if (_allStrings.TryGetValue(langKey, out var strings))
            {
                Instance.Log(LogLevel.Debug, $"GetStringsForLanguage: Found {strings.Count} strings for '{langKey}'", true);
                return strings;
            }

            Instance.Log(LogLevel.Warning, $"GetStringsForLanguage: Language key '{langKey}' not found, falling back to en-US", true);
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

