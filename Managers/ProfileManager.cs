using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using IEVRModManager.Models;

namespace IEVRModManager.Managers
{
    public class ProfileManager
    {
        private readonly string _profilesDir;

        public ProfileManager()
        {
            _profilesDir = Path.Combine(Config.AppDataDir, "Profiles");
            EnsureDirectoryExists(_profilesDir);
        }

        public List<ModProfile> GetAllProfiles()
        {
            var profiles = new List<ModProfile>();
            
            if (!Directory.Exists(_profilesDir))
            {
                return profiles;
            }

            var profileFiles = Directory.GetFiles(_profilesDir, "*.json");
            
            foreach (var file in profileFiles)
            {
                var profile = LoadProfileFromFile(file);
                if (profile != null)
                {
                    profiles.Add(profile);
                }
            }

            return profiles.OrderByDescending(p => p.LastModifiedDate).ToList();
        }

        private ModProfile? LoadProfileFromFile(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var profile = JsonSerializer.Deserialize<ModProfile>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                return profile;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading profile {filePath}: {ex.Message}");
                return null;
            }
        }

        public ModProfile? LoadProfile(string profileName)
        {
            var filePath = GetProfilePath(profileName);
            
            if (!File.Exists(filePath))
            {
                return null;
            }

            return LoadProfileFromFile(filePath);
        }

        public bool SaveProfile(ModProfile profile)
        {
            try
            {
                EnsureDirectoryExists(_profilesDir);
                
                var safeName = GetSafeProfileFileName(profile.Name);
                var filePath = Path.Combine(_profilesDir, $"{safeName}.json");
                
                UpdateProfileDates(profile);
                SaveProfileToFile(profile, filePath);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving profile: {ex.Message}");
                return false;
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
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var json = JsonSerializer.Serialize(profile, options);
            File.WriteAllText(filePath, json);
        }

        public bool DeleteProfile(string profileName)
        {
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
                System.Diagnostics.Debug.WriteLine($"Error deleting profile {profileName}: {ex.Message}");
                return false;
            }
        }

        public bool ProfileExists(string profileName)
        {
            var filePath = GetProfilePath(profileName);
            return File.Exists(filePath);
        }

        private string GetProfilePath(string profileName)
        {
            var safeName = SanitizeFileName(profileName);
            return Path.Combine(_profilesDir, $"{safeName}.json");
        }

        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "Unnamed";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(fileName.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
            
            // Limit length
            if (sanitized.Length > 100)
            {
                sanitized = sanitized.Substring(0, 100);
            }
            
            return sanitized.Trim();
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

