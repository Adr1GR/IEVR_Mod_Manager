using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;

namespace IEVRModManager.Managers
{
    public class AppUpdateManager
    {
        private static readonly HttpClient _httpClient = new();
        private const string GitHubApiUrl = "https://api.github.com/repos/Adr1GR/IEVR_Mod_Manager/releases/latest";
        private const string GitHubReleasesUrl = "https://github.com/Adr1GR/IEVR_Mod_Manager/releases/latest";

        static AppUpdateManager()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("IEVRModManager/1.0");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");
        }

        public class ReleaseInfo
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("body")]
            public string Body { get; set; } = string.Empty;

            [JsonPropertyName("published_at")]
            public DateTime PublishedAt { get; set; }

            [JsonPropertyName("assets")]
            public ReleaseAsset[] Assets { get; set; } = Array.Empty<ReleaseAsset>();
        }

        public class ReleaseAsset
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("browser_download_url")]
            public string BrowserDownloadUrl { get; set; } = string.Empty;

            [JsonPropertyName("size")]
            public long Size { get; set; }
        }

        public static string GetCurrentVersion()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                
                // Try to get version from AssemblyInformationalVersionAttribute
                var versionAttribute = assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                    .FirstOrDefault() as AssemblyInformationalVersionAttribute;

                if (versionAttribute != null && !string.IsNullOrWhiteSpace(versionAttribute.InformationalVersion))
                {
                    return versionAttribute.InformationalVersion;
                }

                // Try to get version from AssemblyFileVersionAttribute
                var fileVersionAttribute = assembly.GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false)
                    .FirstOrDefault() as AssemblyFileVersionAttribute;

                if (fileVersionAttribute != null && !string.IsNullOrWhiteSpace(fileVersionAttribute.Version))
                {
                    return fileVersionAttribute.Version;
                }

                // Fallback to assembly version
                var version = assembly.GetName().Version;
                return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.1.0";
            }
            catch
            {
                return "1.1.0";
            }
        }

        public static async Task<ReleaseInfo?> CheckForUpdatesAsync()
        {
            try
            {
                using var response = await _httpClient.GetAsync(GitHubApiUrl);
                
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var release = JsonSerializer.Deserialize<ReleaseInfo>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return release;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking for updates: {ex.Message}");
                return null;
            }
        }

        public static bool IsNewerVersion(string currentVersion, string latestVersion)
        {
            try
            {
                // Remove 'v' prefix if present
                currentVersion = currentVersion.TrimStart('v', 'V');
                latestVersion = latestVersion.TrimStart('v', 'V');

                var currentParts = currentVersion.Split('.').Select(int.Parse).ToArray();
                var latestParts = latestVersion.Split('.').Select(int.Parse).ToArray();

                // Compare version parts
                for (int i = 0; i < Math.Max(currentParts.Length, latestParts.Length); i++)
                {
                    var current = i < currentParts.Length ? currentParts[i] : 0;
                    var latest = i < latestParts.Length ? latestParts[i] : 0;

                    if (latest > current)
                        return true;
                    if (latest < current)
                        return false;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> DownloadUpdateAsync(ReleaseInfo release, IProgress<(int percentage, string status)>? progress = null)
        {
            try
            {
                // Find the .exe asset (for Windows)
                var exeAsset = release.Assets.FirstOrDefault(a => 
                    a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    (a.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase) || 
                     a.Name.Contains("Windows", StringComparison.OrdinalIgnoreCase)));

                if (exeAsset == null)
                {
                    // Try to find any .exe file
                    exeAsset = release.Assets.FirstOrDefault(a => 
                        a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                }

                if (exeAsset == null)
                {
                    progress?.Report((0, "No executable found in release assets"));
                    return false;
                }

                progress?.Report((10, $"Downloading {exeAsset.Name}..."));

                var tempDir = Path.Combine(Path.GetTempPath(), "IEVRModManager_Update");
                Directory.CreateDirectory(tempDir);
                var tempFile = Path.Combine(tempDir, exeAsset.Name);

                // Download the file
                using var response = await _httpClient.GetAsync(exeAsset.BrowserDownloadUrl);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? exeAsset.Size;
                var downloadedBytes = 0L;

                await using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None);
                await using var httpStream = await response.Content.ReadAsStreamAsync();

                var buffer = new byte[8192];
                int bytesRead;

                while ((bytesRead = await httpStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    downloadedBytes += bytesRead;

                    if (totalBytes > 0)
                    {
                        var percentage = 10 + (int)((downloadedBytes * 80) / totalBytes);
                        progress?.Report((percentage, $"Downloading {exeAsset.Name}... ({downloadedBytes / 1024 / 1024} MB / {totalBytes / 1024 / 1024} MB)"));
                    }
                }

                progress?.Report((90, "Preparing to install update..."));

                // Get current executable path
                var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(currentExe))
                {
                    // For single-file apps, use AppContext.BaseDirectory instead of Assembly.Location
                    var baseDir = AppContext.BaseDirectory;
                    var exeName = Path.GetFileName(Process.GetCurrentProcess().MainModule?.FileName ?? "IEVRModManager.exe");
                    currentExe = Path.Combine(baseDir, exeName);
                    
                    // If the exe doesn't exist in base directory, try to find it
                    if (!File.Exists(currentExe))
                    {
                        currentExe = Process.GetCurrentProcess().MainModule?.FileName;
                    }
                }

                if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
                {
                    progress?.Report((0, "Could not determine current executable path"));
                    return false;
                }

                // Create update script
                var updateScript = Path.Combine(tempDir, "update.bat");
                var currentExeName = Path.GetFileName(currentExe);
                var newExePath = Path.Combine(Path.GetDirectoryName(currentExe)!, currentExeName);
                var backupExePath = currentExe + ".old";

                var scriptContent = $@"@echo off
chcp 65001 >nul
echo Waiting for application to close...
timeout /t 3 /nobreak >nul
echo Closing application...
taskkill /F /IM ""{currentExeName}"" 2>nul
timeout /t 2 /nobreak >nul
echo Replacing executable...
if exist ""{backupExePath}"" del /F /Q ""{backupExePath}""
if exist ""{currentExe}"" move /Y ""{currentExe}"" ""{backupExePath}""
if exist ""{tempFile}"" move /Y ""{tempFile}"" ""{newExePath}""
if exist ""{newExePath}"" (
    echo Starting updated application...
    start """" ""{newExePath}""
    timeout /t 3 /nobreak >nul
    if exist ""{backupExePath}"" del /F /Q ""{backupExePath}""
    echo Update completed successfully.
) else (
    echo Error: Update file not found. Restoring backup...
    if exist ""{backupExePath}"" move /Y ""{backupExePath}"" ""{currentExe}""
)
timeout /t 2 /nobreak >nul
rmdir /S /Q ""{tempDir}"" 2>nul
";

                await File.WriteAllTextAsync(updateScript, scriptContent);

                progress?.Report((95, "Starting update process..."));

                // Start the update script
                var startInfo = new ProcessStartInfo
                {
                    FileName = updateScript,
                    WorkingDirectory = tempDir,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process.Start(startInfo);

                progress?.Report((100, "Update will be installed after restart"));

                return true;
            }
            catch (Exception ex)
            {
                progress?.Report((0, $"Error downloading update: {ex.Message}"));
                return false;
            }
        }

        public static void OpenReleasesPage()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = GitHubReleasesUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open releases page: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
