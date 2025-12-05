using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using IEVRModManager.Models;

namespace IEVRModManager.Managers
{
    public class LastInstallManager
    {
        private readonly string _recordPath;

        public LastInstallManager()
        {
            _recordPath = Config.LastInstallPath;
            EnsureDirectoryExists(Path.GetDirectoryName(_recordPath));
        }

        public LastInstallInfo Load()
        {
            if (!File.Exists(_recordPath))
            {
                return LastInstallInfo.Empty();
            }

            try
            {
                var json = File.ReadAllText(_recordPath);
                var info = JsonSerializer.Deserialize<LastInstallInfo>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                return info ?? LastInstallInfo.Empty();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading last install info: {ex.Message}");
                return LastInstallInfo.Empty();
            }
        }

        public bool Save(LastInstallInfo info)
        {
            try
            {
                EnsureDirectoryExists(Path.GetDirectoryName(_recordPath));
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
                Debug.WriteLine($"Error saving last install info: {ex.Message}");
                return false;
            }
        }

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
                Debug.WriteLine($"Error deleting last install info: {ex.Message}");
            }
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
