using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using static IEVRModManager.Helpers.Logger;

namespace IEVRModManager.Helpers
{
    /// <summary>
    /// Provides utility methods for detecting Steam game installation paths.
    /// </summary>
    public static class SteamHelper
    {
        private const int SteamAppId = 2799860; // Inazuma Eleven Victory Road
        private const string GameExecutableName = "nie.exe";
        private const string GameDataFolderName = "data";

        /// <summary>
        /// Attempts to detect the game installation path from Steam.
        /// </summary>
        /// <returns>The game path if found, otherwise null.</returns>
        public static string? DetectGamePath()
        {
            try
            {
                var steamPath = GetSteamInstallPath();
                if (string.IsNullOrWhiteSpace(steamPath))
                {
                    Logger.Instance.Debug("Steam installation path not found.", true);
                    return null;
                }

                var libraryFolders = GetSteamLibraryFolders(steamPath);
                if (libraryFolders == null || libraryFolders.Count == 0)
                {
                    Logger.Instance.Debug("No Steam library folders found.", true);
                    return null;
                }

                foreach (var libraryPath in libraryFolders)
                {
                    var gamePath = FindGameInLibrary(libraryPath);
                    if (!string.IsNullOrWhiteSpace(gamePath))
                    {
                        Logger.Instance.Info($"Game path detected from Steam: {gamePath}", true);
                        return gamePath;
                    }
                }

                Logger.Instance.Debug("Game not found in any Steam library folder.", true);
                return null;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogLevel.Warning, "Error detecting Steam game path", true, ex);
                return null;
            }
        }

        /// <summary>
        /// Gets the Steam installation path from the Windows registry.
        /// </summary>
        /// <returns>The Steam installation path, or null if not found.</returns>
        private static string? GetSteamInstallPath()
        {
            try
            {
                // Try 64-bit registry first
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                {
                    if (key != null)
                    {
                        var installPath = key.GetValue("InstallPath") as string;
                        if (!string.IsNullOrWhiteSpace(installPath) && Directory.Exists(installPath))
                        {
                            return installPath;
                        }
                    }
                }

                // Try 32-bit registry
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam"))
                {
                    if (key != null)
                    {
                        var installPath = key.GetValue("InstallPath") as string;
                        if (!string.IsNullOrWhiteSpace(installPath) && Directory.Exists(installPath))
                        {
                            return installPath;
                        }
                    }
                }

                // Try current user registry
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key != null)
                    {
                        var installPath = key.GetValue("SteamPath") as string;
                        if (!string.IsNullOrWhiteSpace(installPath) && Directory.Exists(installPath))
                        {
                            return installPath;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogLevel.Warning, "Error reading Steam registry", true, ex);
            }

            return null;
        }

        /// <summary>
        /// Gets all Steam library folders from the libraryfolders.vdf file.
        /// </summary>
        /// <param name="steamPath">The Steam installation path.</param>
        /// <returns>A list of library folder paths, or null if the file cannot be read.</returns>
        private static List<string>? GetSteamLibraryFolders(string steamPath)
        {
            var libraryFolders = new List<string>();
            var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

            if (!File.Exists(vdfPath))
            {
                Logger.Instance.Debug($"libraryfolders.vdf not found at: {vdfPath}", true);
                // Fallback: use the default Steam library path
                var defaultLibrary = Path.Combine(steamPath, "steamapps", "common");
                if (Directory.Exists(defaultLibrary))
                {
                    libraryFolders.Add(Path.GetDirectoryName(defaultLibrary) ?? steamPath);
                }
                return libraryFolders.Count > 0 ? libraryFolders : null;
            }

            try
            {
                // Note: Using synchronous method here because this is called during initialization
                // where async could cause deadlocks
                var content = File.ReadAllText(vdfPath);
                
                // Parse libraryfolders.vdf format
                // Format is like: "path"		"C:\\SteamLibrary"
                var pathPattern = new Regex(@"""path""\s+""([^""]+)""", RegexOptions.IgnoreCase);
                var matches = pathPattern.Matches(content);

                foreach (Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        var libraryPath = match.Groups[1].Value;
                        // Replace escaped backslashes
                        libraryPath = libraryPath.Replace("\\\\", "\\");
                        
                        if (Directory.Exists(libraryPath))
                        {
                            libraryFolders.Add(libraryPath);
                        }
                    }
                }

                // Always include the default Steam library
                var defaultSteamLibrary = Path.Combine(steamPath, "steamapps");
                if (Directory.Exists(defaultSteamLibrary) && !libraryFolders.Contains(Path.GetDirectoryName(defaultSteamLibrary) ?? steamPath))
                {
                    libraryFolders.Insert(0, Path.GetDirectoryName(defaultSteamLibrary) ?? steamPath);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogLevel.Warning, "Error reading libraryfolders.vdf", true, ex);
                // Fallback: use the default Steam library path
                var defaultLibrary = Path.Combine(steamPath, "steamapps", "common");
                if (Directory.Exists(defaultLibrary))
                {
                    libraryFolders.Add(Path.GetDirectoryName(defaultLibrary) ?? steamPath);
                }
            }

            return libraryFolders.Count > 0 ? libraryFolders : null;
        }

        /// <summary>
        /// Searches for the game in a Steam library folder.
        /// </summary>
        /// <param name="libraryPath">The Steam library folder path.</param>
        /// <returns>The game path if found, otherwise null.</returns>
        private static string? FindGameInLibrary(string libraryPath)
        {
            try
            {
                // Check if the game is installed in this library
                var appManifestPath = Path.Combine(libraryPath, "steamapps", $"appmanifest_{SteamAppId}.acf");
                
                if (File.Exists(appManifestPath))
                {
                    // Read the manifest to get the install directory name
                    // Note: Using synchronous method here because this is called during initialization
                    var manifestContent = File.ReadAllText(appManifestPath);
                    var installDirMatch = Regex.Match(manifestContent, @"""installdir""\s+""([^""]+)""", RegexOptions.IgnoreCase);
                    
                    if (installDirMatch.Success && installDirMatch.Groups.Count > 1)
                    {
                        var installDirName = installDirMatch.Groups[1].Value;
                        var gamePath = Path.Combine(libraryPath, "steamapps", "common", installDirName);
                        
                        if (ValidateGamePath(gamePath))
                        {
                            return gamePath;
                        }
                    }
                    
                    // Fallback: try common directory with AppID-based name
                    var commonPath = Path.Combine(libraryPath, "steamapps", "common");
                    if (Directory.Exists(commonPath))
                    {
                        // Try to find the game directory by looking for nie.exe
                        var directories = Directory.GetDirectories(commonPath);
                        foreach (var dir in directories)
                        {
                            if (ValidateGamePath(dir))
                            {
                                return dir;
                            }
                        }
                    }
                }
                else
                {
                    // No manifest file, but try searching in common directory anyway
                    var commonPath = Path.Combine(libraryPath, "steamapps", "common");
                    if (Directory.Exists(commonPath))
                    {
                        var directories = Directory.GetDirectories(commonPath);
                        foreach (var dir in directories)
                        {
                            if (ValidateGamePath(dir))
                            {
                                return dir;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogLevel.Debug, $"Error searching library folder: {libraryPath}", true, ex);
            }

            return null;
        }

        /// <summary>
        /// Validates that a path is a valid game installation directory.
        /// </summary>
        /// <param name="gamePath">The path to validate.</param>
        /// <returns>True if the path is valid, otherwise false.</returns>
        private static bool ValidateGamePath(string gamePath)
        {
            if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
            {
                return false;
            }

            // Check for the game executable
            var exePath = Path.Combine(gamePath, GameExecutableName);
            if (!File.Exists(exePath))
            {
                return false;
            }

            // Check for the data folder
            var dataPath = Path.Combine(gamePath, GameDataFolderName);
            if (!Directory.Exists(dataPath))
            {
                return false;
            }

            return true;
        }
    }
}
