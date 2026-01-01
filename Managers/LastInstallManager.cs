using System;
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
    /// Manages the last installation record, tracking which mods were last applied.
    /// </summary>
    public class LastInstallManager
    {
        private readonly string _recordPath;

        /// <summary>
        /// Initializes a new instance of the <see cref="LastInstallManager"/> class.
        /// </summary>
        public LastInstallManager()
        {
            _recordPath = Config.LastInstallPath;
            FileSystemHelper.EnsureDirectoryExists(Path.GetDirectoryName(_recordPath));
        }

        /// <summary>
        /// Loads the last installation information from disk.
        /// </summary>
        /// <returns>A <see cref="LastInstallInfo"/> object. Returns empty info if file doesn't exist or is invalid.</returns>
        /// <remarks>
        /// This method is provided for backward compatibility. For async contexts, use <see cref="LoadAsync"/> instead.
        /// </remarks>
        public LastInstallInfo Load()
        {
            if (!File.Exists(_recordPath))
            {
                return LastInstallInfo.Empty();
            }

            try
            {
                var json = File.ReadAllText(_recordPath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return LastInstallInfo.Empty();
                }

                var info = JsonSerializer.Deserialize<LastInstallInfo>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                return info ?? LastInstallInfo.Empty();
            }
            catch (JsonException ex)
            {
                Instance.Log(LogLevel.Warning, "Error parsing last install info JSON", true, ex);
                return LastInstallInfo.Empty();
            }
            catch (IOException ex)
            {
                Instance.Log(LogLevel.Warning, "IO error loading last install info", true, ex);
                return LastInstallInfo.Empty();
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Warning, "Unexpected error loading last install info", true, ex);
                return LastInstallInfo.Empty();
            }
        }

        /// <summary>
        /// Loads the last installation information from disk asynchronously.
        /// </summary>
        /// <returns>A <see cref="LastInstallInfo"/> object. Returns empty info if file doesn't exist or is invalid.</returns>
        public async Task<LastInstallInfo> LoadAsync()
        {
            if (!File.Exists(_recordPath))
            {
                return LastInstallInfo.Empty();
            }

            try
            {
                var json = await File.ReadAllTextAsync(_recordPath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return LastInstallInfo.Empty();
                }

                var info = JsonSerializer.Deserialize<LastInstallInfo>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                return info ?? LastInstallInfo.Empty();
            }
            catch (JsonException ex)
            {
                Instance.Log(LogLevel.Warning, "Error parsing last install info JSON", true, ex);
                return LastInstallInfo.Empty();
            }
            catch (IOException ex)
            {
                Instance.Log(LogLevel.Warning, "IO error loading last install info", true, ex);
                return LastInstallInfo.Empty();
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Warning, "Unexpected error loading last install info", true, ex);
                return LastInstallInfo.Empty();
            }
        }

        /// <summary>
        /// Saves the last installation information to disk.
        /// </summary>
        /// <param name="info">The installation information to save.</param>
        /// <returns><c>true</c> if the information was saved successfully; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="info"/> is null.</exception>
        /// <exception cref="ModManagerException">Thrown when there's an error writing the file.</exception>
        /// <remarks>
        /// This method is provided for backward compatibility. For async contexts, use <see cref="SaveAsync"/> instead.
        /// </remarks>
        public bool Save(LastInstallInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            try
            {
                FileSystemHelper.EnsureDirectoryExists(Path.GetDirectoryName(_recordPath));
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                var json = JsonSerializer.Serialize(info, options);
                File.WriteAllText(_recordPath, json);
                return true;
            }
            catch (Exception ex)
            {
                HandleFileOperationException(ex, "saving last install info");
                return false; // Never reached, but satisfies compiler
            }
        }

        /// <summary>
        /// Saves the last installation information to disk asynchronously.
        /// </summary>
        /// <param name="info">The installation information to save.</param>
        /// <returns><c>true</c> if the information was saved successfully; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="info"/> is null.</exception>
        /// <exception cref="ModManagerException">Thrown when there's an error writing the file.</exception>
        public async Task<bool> SaveAsync(LastInstallInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            try
            {
                FileSystemHelper.EnsureDirectoryExists(Path.GetDirectoryName(_recordPath));
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                var json = JsonSerializer.Serialize(info, options);
                await File.WriteAllTextAsync(_recordPath, json);
                return true;
            }
            catch (Exception ex)
            {
                HandleFileOperationException(ex, "saving last install info");
                return false; // Never reached, but satisfies compiler
            }
        }

        /// <summary>
        /// Handles file operation exceptions by logging and throwing appropriate ModManagerException.
        /// </summary>
        private static void HandleFileOperationException(Exception ex, string operation)
        {
            switch (ex)
            {
                case IOException ioEx:
                    Instance.Log(LogLevel.Error, $"IO error {operation}", true, ioEx);
                    throw new ModManagerException($"Failed to {operation}. Check file permissions.", ioEx);
                case UnauthorizedAccessException uaEx:
                    Instance.Log(LogLevel.Error, $"Access denied {operation}", true, uaEx);
                    throw new ModManagerException($"Access denied to {operation}.", uaEx);
                default:
                    Instance.Log(LogLevel.Error, $"Unexpected error {operation}", true, ex);
                    throw new ModManagerException($"An unexpected error occurred while {operation}.", ex);
            }
        }

        /// <summary>
        /// Clears the last installation record by deleting the file.
        /// </summary>
        public void Clear()
        {
            if (!File.Exists(_recordPath))
            {
                return;
            }

            try
            {
                File.Delete(_recordPath);
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Warning, "Error deleting last install info", true, ex);
            }
        }

    }
}
