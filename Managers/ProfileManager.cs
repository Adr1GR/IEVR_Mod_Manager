using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IEVRModManager.Models;
using IEVRModManager.Helpers;
using IEVRModManager.Exceptions;
using static IEVRModManager.Helpers.Logger;

namespace IEVRModManager.Managers
{
    /// <summary>
    /// Manages mod profiles, allowing users to save and load different mod configurations.
    /// </summary>
    public class ProfileManager
    {
        private readonly string _profilesDir;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="ProfileManager"/> class.
        /// </summary>
        public ProfileManager()
        {
            _profilesDir = Path.Combine(Config.BaseDir, "Profiles");
            FileSystemHelper.EnsureDirectoryExists(_profilesDir);
        }

        /// <summary>
        /// Gets all available mod profiles, ordered by last modified date (newest first).
        /// </summary>
        /// <returns>A list of all <see cref="ModProfile"/> objects found in the profiles directory.</returns>
        public List<ModProfile> GetAllProfiles()
        {
            if (!Directory.Exists(_profilesDir))
            {
                return new List<ModProfile>();
            }

            return Directory.GetFiles(_profilesDir, "*.json")
                .Select(LoadProfileFromFile)
                .Where(profile => profile != null)
                .Select(profile =>
                {
                    // Normalize empty or whitespace-only names to "Unnamed"
                    if (profile != null && string.IsNullOrWhiteSpace(profile.Name))
                    {
                        profile.Name = "Unnamed";
                    }
                    return profile;
                })
                .Where(profile => profile != null)
                .Cast<ModProfile>() // Cast to non-nullable after filtering nulls
                .OrderByDescending(p => p.LastModifiedDate)
                .ToList();
        }

        private ModProfile? LoadProfileFromFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            try
            {
                if (!File.Exists(filePath))
                {
                    return null;
                }

                var json = File.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                return JsonSerializer.Deserialize<ModProfile>(json, options);
            }
            catch (JsonException ex)
            {
                Instance.Log(LogLevel.Warning, $"Error parsing profile JSON {filePath}", true, ex);
                return null;
            }
            catch (IOException ex)
            {
                Instance.Log(LogLevel.Warning, $"IO error loading profile {filePath}", true, ex);
                return null;
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Warning, $"Unexpected error loading profile {filePath}", true, ex);
                return null;
            }
        }

        private async Task<ModProfile?> LoadProfileFromFileAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            try
            {
                if (!File.Exists(filePath))
                {
                    return null;
                }

                var json = await File.ReadAllTextAsync(filePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                return JsonSerializer.Deserialize<ModProfile>(json, options);
            }
            catch (JsonException ex)
            {
                Instance.Log(LogLevel.Warning, $"Error parsing profile JSON {filePath}", true, ex);
                return null;
            }
            catch (IOException ex)
            {
                Instance.Log(LogLevel.Warning, $"IO error loading profile {filePath}", true, ex);
                return null;
            }
            catch (Exception ex)
            {
                Instance.Log(LogLevel.Warning, $"Unexpected error loading profile {filePath}", true, ex);
                return null;
            }
        }

        /// <summary>
        /// Loads a mod profile by name.
        /// </summary>
        /// <param name="profileName">The name of the profile to load.</param>
        /// <returns>The loaded <see cref="ModProfile"/> if found; otherwise, <c>null</c>.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="profileName"/> is null or empty.</exception>
        public ModProfile? LoadProfile(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
            {
                throw new ArgumentException("Profile name cannot be null or empty.", nameof(profileName));
            }

            var filePath = GetProfilePath(profileName);
            
            if (!File.Exists(filePath))
            {
                return null;
            }

            return LoadProfileFromFile(filePath);
        }

        /// <summary>
        /// Saves a mod profile to disk.
        /// </summary>
        /// <param name="profile">The profile to save.</param>
        /// <returns><c>true</c> if the profile was saved successfully; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="profile"/> is null.</exception>
        /// <exception cref="ModManagerException">Thrown when there's an error saving the profile file.</exception>
        /// <remarks>
        /// This method is provided for backward compatibility. For async contexts, use <see cref="SaveProfileAsync"/> instead.
        /// </remarks>
        public bool SaveProfile(ModProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            try
            {
                FileSystemHelper.EnsureDirectoryExists(_profilesDir);
                
                var safeName = GetSafeProfileFileName(profile.Name);
                var filePath = Path.Combine(_profilesDir, $"{safeName}.json");
                
                UpdateProfileDates(profile);
                SaveProfileToFile(profile, filePath);
                return true;
            }
            catch (Exception ex)
            {
                HandleFileOperationException(ex, "saving profile");
                return false; // Never reached, but satisfies compiler
            }
        }

        /// <summary>
        /// Saves a mod profile to disk asynchronously.
        /// </summary>
        /// <param name="profile">The profile to save.</param>
        /// <returns><c>true</c> if the profile was saved successfully; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="profile"/> is null.</exception>
        /// <exception cref="ModManagerException">Thrown when there's an error saving the profile file.</exception>
        public async Task<bool> SaveProfileAsync(ModProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            try
            {
                FileSystemHelper.EnsureDirectoryExists(_profilesDir);
                
                var safeName = GetSafeProfileFileName(profile.Name);
                var filePath = Path.Combine(_profilesDir, $"{safeName}.json");
                
                UpdateProfileDates(profile);
                await SaveProfileToFileAsync(profile, filePath);
                return true;
            }
            catch (Exception ex)
            {
                HandleFileOperationException(ex, "saving profile");
                return false; // Never reached, but satisfies compiler
            }
        }

        private string GetSafeProfileFileName(string profileName)
        {
            var safeName = SanitizeFileName(profileName);
            return string.IsNullOrWhiteSpace(safeName) ? "Unnamed" : safeName;
        }

        private void UpdateProfileDates(ModProfile profile)
        {
            profile.LastModifiedDate = DateTime.Now;
            if (profile.CreatedDate == DateTime.MinValue)
            {
                profile.CreatedDate = DateTime.Now;
            }
        }

        private void SaveProfileToFile(ModProfile profile, string filePath)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
            }

            var json = JsonSerializer.Serialize(profile, JsonOptions);
            File.WriteAllText(filePath, json);
        }

        private async Task SaveProfileToFileAsync(ModProfile profile, string filePath)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
            }

            var json = JsonSerializer.Serialize(profile, JsonOptions);
            await File.WriteAllTextAsync(filePath, json);
        }

        /// <summary>
        /// Deletes a mod profile from disk.
        /// </summary>
        /// <param name="profileName">The name of the profile to delete.</param>
        /// <returns><c>true</c> if the profile was deleted successfully; otherwise, <c>false</c> if the profile doesn't exist.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="profileName"/> is null or empty.</exception>
        /// <exception cref="ModManagerException">Thrown when there's an error deleting the profile file.</exception>
        public bool DeleteProfile(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
            {
                throw new ArgumentException("Profile name cannot be null or empty.", nameof(profileName));
            }

            try
            {
                var filePath = GetProfilePath(profileName);
                
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                HandleFileOperationException(ex, $"deleting profile '{profileName}'");
                return false; // Never reached, but satisfies compiler
            }
        }

        /// <summary>
        /// Checks if a profile with the specified name exists.
        /// </summary>
        /// <param name="profileName">The name of the profile to check.</param>
        /// <returns><c>true</c> if the profile exists; otherwise, <c>false</c>.</returns>
        public bool ProfileExists(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
            {
                return false;
            }

            var filePath = GetProfilePath(profileName);
            return File.Exists(filePath);
        }

        private string GetProfilePath(string profileName)
        {
            var safeName = SanitizeFileName(profileName);
            return Path.Combine(_profilesDir, $"{safeName}.json");
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
                    if (operation.Contains("delete"))
                    {
                        throw new ModManagerException($"Failed to {operation}. The file may be in use.", ioEx);
                    }
                    throw new ModManagerException($"Failed to {operation}. Check file permissions.", ioEx);
                case UnauthorizedAccessException uaEx:
                    Instance.Log(LogLevel.Error, $"Access denied {operation}", true, uaEx);
                    throw new ModManagerException($"Access denied to {operation}.", uaEx);
                default:
                    Instance.Log(LogLevel.Error, $"Unexpected error {operation}", true, ex);
                    throw new ModManagerException($"An unexpected error occurred while {operation}.", ex);
            }
        }

        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "Unnamed";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(fileName.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
            
            if (sanitized.Length > 100)
            {
                sanitized = sanitized.Substring(0, 100);
            }
            
            return sanitized.Trim();
        }

    }
}

