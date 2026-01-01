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
    /// <summary>
    /// Manages application updates by checking GitHub releases and downloading updates.
    /// </summary>
    public class AppUpdateManager
    {
        private static readonly HttpClient _httpClient = new();
        private const string GitHubApiUrl = "https://api.github.com/repos/Adr1GR/IEVR_Mod_Manager/releases/latest";
        private const string GitHubReleasesUrl = "https://github.com/Adr1GR/IEVR_Mod_Manager/releases/latest";
        private static DateTime? _lastRateLimitHit;
        private static readonly TimeSpan RateLimitCooldown = TimeSpan.FromMinutes(5);
        private static string? _pendingUpdateScript;
        private static bool _disposed = false;

        static AppUpdateManager()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("IEVRModManager/1.0");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");
            // Allow redirects for file downloads (GitHub uses redirects for release assets)
            _httpClient.Timeout = TimeSpan.FromMinutes(10); // Increase timeout for large file downloads
        }

        /// <summary>
        /// Disposes the static HttpClient instance. Should be called when the application is shutting down.
        /// </summary>
        public static void DisposeHttpClient()
        {
            if (!_disposed)
            {
                _httpClient?.Dispose();
                _disposed = true;
            }
        }

        /// <summary>
        /// Represents information about a GitHub release.
        /// </summary>
        public class ReleaseInfo
        {
            /// <summary>
            /// Gets or sets the release tag name (e.g., "v1.7.0").
            /// </summary>
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets the release name/title.
            /// </summary>
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets the release notes/description body.
            /// </summary>
            [JsonPropertyName("body")]
            public string Body { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets the date and time when the release was published.
            /// </summary>
            [JsonPropertyName("published_at")]
            public DateTime PublishedAt { get; set; }

            /// <summary>
            /// Gets or sets the array of release assets (downloadable files).
            /// </summary>
            [JsonPropertyName("assets")]
            public ReleaseAsset[] Assets { get; set; } = Array.Empty<ReleaseAsset>();
        }

        /// <summary>
        /// Represents a downloadable asset from a GitHub release.
        /// </summary>
        public class ReleaseAsset
        {
            /// <summary>
            /// Gets or sets the name of the asset file.
            /// </summary>
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets the URL to download the asset.
            /// </summary>
            [JsonPropertyName("browser_download_url")]
            public string BrowserDownloadUrl { get; set; } = string.Empty;

            /// <summary>
            /// Gets or sets the size of the asset in bytes.
            /// </summary>
            [JsonPropertyName("size")]
            public long Size { get; set; }
        }

        /// <summary>
        /// Gets the current version of the application from assembly attributes.
        /// </summary>
        /// <returns>The version string (e.g., "1.7.0").</returns>
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

        /// <summary>
        /// Checks for available updates by querying the GitHub releases API.
        /// </summary>
        /// <returns>A <see cref="ReleaseInfo"/> object if an update is available; otherwise, <c>null</c>.</returns>
        public static async Task<ReleaseInfo?> CheckForUpdatesAsync()
        {
            try
            {
                // Check if we recently hit rate limit
                if (_lastRateLimitHit.HasValue && DateTime.UtcNow - _lastRateLimitHit.Value < RateLimitCooldown)
                {
                    var waitMinutes = Math.Ceiling((RateLimitCooldown - (DateTime.UtcNow - _lastRateLimitHit.Value)).TotalMinutes);
                    System.Diagnostics.Debug.WriteLine($"[UpdateCheck] Skipping due to rate limit cooldown (hit at {_lastRateLimitHit.Value:HH:mm:ss}, wait {waitMinutes} min)");
                    return null;
                }

                System.Diagnostics.Debug.WriteLine("[UpdateCheck] Making request to GitHub API");
                using var response = await _httpClient.GetAsync(GitHubApiUrl);
                
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    // Rate limit exceeded
                    var retryAfter = response.Headers.RetryAfter?.Delta?.TotalMinutes ?? 0;
                    var rateLimitRemaining = response.Headers.Contains("X-RateLimit-Remaining") 
                        ? response.Headers.GetValues("X-RateLimit-Remaining").FirstOrDefault() 
                        : "unknown";
                    var rateLimitReset = response.Headers.Contains("X-RateLimit-Reset")
                        ? response.Headers.GetValues("X-RateLimit-Reset").FirstOrDefault()
                        : null;

                    var errorMsg = "GitHub API rate limit exceeded. ";
                    if (retryAfter > 0)
                    {
                        errorMsg += $"Retry after {Math.Ceiling(retryAfter)} minutes.";
                    }
                    else if (rateLimitReset != null && long.TryParse(rateLimitReset, out var resetTimestamp))
                    {
                        var resetTime = DateTimeOffset.FromUnixTimeSeconds(resetTimestamp);
                        var waitTime = resetTime - DateTimeOffset.UtcNow;
                        if (waitTime.TotalMinutes > 0)
                        {
                            errorMsg += $"Retry after {Math.Ceiling(waitTime.TotalMinutes)} minutes.";
                        }
                    }
                    
                    _lastRateLimitHit = DateTime.UtcNow;
                    System.Diagnostics.Debug.WriteLine($"[UpdateCheck] Rate limit hit at {_lastRateLimitHit.Value:HH:mm:ss}. {errorMsg} (Remaining: {rateLimitRemaining})");
                    return null;
                }
                
                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateCheck] GitHub API returned status code: {response.StatusCode}");
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
                System.Diagnostics.Debug.WriteLine($"[UpdateCheck] Error checking for updates: {ex.Message}");
                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateCheck] Inner exception: {ex.InnerException.Message}");
                }
                return null;
            }
        }

        /// <summary>
        /// Compares two version strings to determine if the latest version is newer than the current version.
        /// </summary>
        /// <param name="currentVersion">The current version string.</param>
        /// <param name="latestVersion">The latest version string to compare against.</param>
        /// <returns><c>true</c> if the latest version is newer than the current version; otherwise, <c>false</c>.</returns>
        public static bool IsNewerVersion(string currentVersion, string latestVersion)
        {
            try
            {
                // Remove 'v' prefix if present
                currentVersion = currentVersion.TrimStart('v', 'V').Trim();
                latestVersion = latestVersion.TrimStart('v', 'V').Trim();

                // Remove metadata (e.g., "+commit_hash" or "-prerelease")
                // Extract only the version number part before + or -
                var currentVersionOnly = currentVersion.Split('+')[0].Split('-')[0].Trim();
                var latestVersionOnly = latestVersion.Split('+')[0].Split('-')[0].Trim();

                System.Diagnostics.Debug.WriteLine($"[UpdateCheck] Comparing versions - Current: '{currentVersion}' -> '{currentVersionOnly}', Latest: '{latestVersion}' -> '{latestVersionOnly}'");

                var currentParts = currentVersionOnly.Split('.').Select(int.Parse).ToArray();
                var latestParts = latestVersionOnly.Split('.').Select(int.Parse).ToArray();

                System.Diagnostics.Debug.WriteLine($"[UpdateCheck] Current parts: [{string.Join(", ", currentParts)}], Latest parts: [{string.Join(", ", latestParts)}]");
                
                if (currentParts.Length == 0 || latestParts.Length == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[UpdateCheck] Invalid version format");
                    return false;
                }

                // Compare version parts
                for (int i = 0; i < Math.Max(currentParts.Length, latestParts.Length); i++)
                {
                    var current = i < currentParts.Length ? currentParts[i] : 0;
                    var latest = i < latestParts.Length ? latestParts[i] : 0;

                    System.Diagnostics.Debug.WriteLine($"[UpdateCheck] Comparing part {i}: current={current}, latest={latest}");

                    if (latest > current)
                    {
                        System.Diagnostics.Debug.WriteLine($"[UpdateCheck] Latest version is newer (part {i}: {latest} > {current})");
                        return true;
                    }
                    if (latest < current)
                    {
                        System.Diagnostics.Debug.WriteLine($"[UpdateCheck] Current version is newer or equal (part {i}: {latest} < {current})");
                        return false;
                    }
                }

                System.Diagnostics.Debug.WriteLine("[UpdateCheck] Versions are equal");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateCheck] Error comparing versions: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Downloads the update executable from the specified release.
        /// </summary>
        /// <param name="release">The release information containing the update to download.</param>
        /// <param name="progress">Optional progress reporter for download status updates.</param>
        /// <returns><c>true</c> if the download completed successfully; otherwise, <c>false</c>.</returns>
        public static async Task<bool> DownloadUpdateAsync(ReleaseInfo release, IProgress<(int percentage, string status)>? progress = null)
        {
            try
            {
                // Log all available assets for debugging
                System.Diagnostics.Debug.WriteLine($"[UpdateDownload] Available assets in release:");
                foreach (var asset in release.Assets)
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateDownload]   - {asset.Name} ({asset.Size} bytes) - {asset.BrowserDownloadUrl}");
                }

                // Find the .exe asset (for Windows)
                var exeAsset = release.Assets.FirstOrDefault(a => 
                    a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    (a.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase) || 
                     a.Name.Contains("Windows", StringComparison.OrdinalIgnoreCase)));

                if (exeAsset == null)
                {
                    System.Diagnostics.Debug.WriteLine("[UpdateDownload] No exe found with win-x64 or Windows in name, trying any .exe");
                    // Try to find any .exe file
                    exeAsset = release.Assets.FirstOrDefault(a => 
                        a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                }

                if (exeAsset == null)
                {
                    System.Diagnostics.Debug.WriteLine("[UpdateDownload] No executable found in release assets");
                    progress?.Report((0, "No executable found in release assets"));
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"[UpdateDownload] Selected asset: {exeAsset.Name} (Size from API: {exeAsset.Size} bytes)");
                System.Diagnostics.Debug.WriteLine($"[UpdateDownload] Asset URL: {exeAsset.BrowserDownloadUrl}");
                
                // Log current executable size for comparison
                var currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(currentExePath) && File.Exists(currentExePath))
                {
                    var currentSize = new FileInfo(currentExePath).Length;
                    System.Diagnostics.Debug.WriteLine($"[UpdateDownload] Current executable size: {currentSize} bytes");
                    if (currentSize == exeAsset.Size)
                    {
                        System.Diagnostics.Debug.WriteLine($"[UpdateDownload] WARNING: Downloaded file size matches current file size! This might be the same version.");
                    }
                }
                
                progress?.Report((10, $"Downloading {exeAsset.Name}..."));

                var tempDir = Path.Combine(Path.GetTempPath(), "IEVRModManager_Update");
                Directory.CreateDirectory(tempDir);
                var tempFile = Path.Combine(tempDir, exeAsset.Name);

                System.Diagnostics.Debug.WriteLine($"[UpdateDownload] Starting download from: {exeAsset.BrowserDownloadUrl}");
                System.Diagnostics.Debug.WriteLine($"[UpdateDownload] Target file: {tempFile}");

                // Download the file (allow redirects for GitHub release assets)
                using var response = await _httpClient.GetAsync(exeAsset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    // Rate limit exceeded during download
                    _lastRateLimitHit = DateTime.UtcNow;
                    var retryAfter = response.Headers.RetryAfter?.Delta?.TotalMinutes ?? 0;
                    var errorMsg = "GitHub rate limit exceeded while downloading update. ";
                    if (retryAfter > 0)
                    {
                        errorMsg += $"Please try again in approximately {Math.Ceiling(retryAfter)} minutes.";
                    }
                    else
                    {
                        errorMsg += "Please try again later.";
                    }
                    System.Diagnostics.Debug.WriteLine($"[UpdateDownload] Rate limit hit: {errorMsg}");
                    progress?.Report((0, errorMsg));
                    throw new HttpRequestException(errorMsg, null, response.StatusCode);
                }
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorMsg = $"Failed to download update: HTTP {response.StatusCode}";
                    System.Diagnostics.Debug.WriteLine($"[UpdateDownload] HTTP error: {response.StatusCode}");
                    progress?.Report((0, errorMsg));
                    throw new HttpRequestException(errorMsg, null, response.StatusCode);
                }
                
                response.EnsureSuccessStatusCode();

                // Get file size from response headers or asset info
                var contentLength = response.Content.Headers.ContentLength;
                var totalBytes = contentLength ?? exeAsset.Size;
                
                System.Diagnostics.Debug.WriteLine($"[UpdateDownload] Content-Length header: {contentLength}");
                System.Diagnostics.Debug.WriteLine($"[UpdateDownload] Asset size from API: {exeAsset.Size}");
                System.Diagnostics.Debug.WriteLine($"[UpdateDownload] Using total size: {totalBytes} bytes ({totalBytes / 1024.0 / 1024.0:F2} MB)");

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
                    else
                    {
                        // If size is unknown, show downloaded amount only
                        var percentage = 10 + (int)Math.Min(80, (downloadedBytes / 1024 / 1024)); // Rough estimate
                        progress?.Report((percentage, $"Downloading {exeAsset.Name}... ({downloadedBytes / 1024 / 1024} MB)"));
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[UpdateDownload] Download finished. Downloaded: {downloadedBytes} bytes ({downloadedBytes / 1024.0 / 1024.0:F2} MB)");

                // Verify file was downloaded successfully
                if (!File.Exists(tempFile) || new FileInfo(tempFile).Length == 0)
                {
                    var errorMsg = "Downloaded file is missing or empty";
                    System.Diagnostics.Debug.WriteLine($"[UpdateDownload] {errorMsg}");
                    progress?.Report((0, errorMsg));
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"[UpdateDownload] Download completed. File size: {new FileInfo(tempFile).Length} bytes");
                
                // Verify the downloaded file version matches the release version
                try
                {
                    var fileVersionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(tempFile);
                    var downloadedVersion = fileVersionInfo.FileVersion ?? fileVersionInfo.ProductVersion ?? "Unknown";
                    System.Diagnostics.Debug.WriteLine($"[UpdateDownload] Downloaded file version: {downloadedVersion}");
                    System.Diagnostics.Debug.WriteLine($"[UpdateDownload] Expected release version: {release.TagName}");
                    
                    // Extract version number from release tag (e.g., "v1.8.0" -> "1.8.0")
                    var expectedVersion = release.TagName.TrimStart('v', 'V').Trim();
                    var downloadedVersionOnly = downloadedVersion.Split('+')[0].Split('-')[0].Trim();
                    
                    if (!downloadedVersionOnly.StartsWith(expectedVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        var warningMsg = $"WARNING: Downloaded file version ({downloadedVersionOnly}) does not match expected release version ({expectedVersion})";
                        System.Diagnostics.Debug.WriteLine($"[UpdateDownload] {warningMsg}");
                        // Don't fail, but log the warning - the file might still be correct
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateDownload] Could not verify file version: {ex.Message}");
                    // Continue anyway - version checking might fail for various reasons
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
                var currentExeDir = Path.GetDirectoryName(currentExe)!;
                var newExePath = Path.Combine(currentExeDir, currentExeName);
                var backupExePath = currentExe + ".old";

                // Escape paths for batch file (double quotes and handle special characters)
                var escapedCurrentExe = currentExe.Replace("\"", "\"\"");
                var escapedTempFile = tempFile.Replace("\"", "\"\"");
                var escapedNewExePath = newExePath.Replace("\"", "\"\"");
                var escapedBackupExePath = backupExePath.Replace("\"", "\"\"");
                var escapedTempDir = tempDir.Replace("\"", "\"\"");

                System.Diagnostics.Debug.WriteLine($"[UpdateDownload] Creating update script:");
                System.Diagnostics.Debug.WriteLine($"[UpdateDownload]   Current exe: {currentExe}");
                System.Diagnostics.Debug.WriteLine($"[UpdateDownload]   Temp file: {tempFile}");
                System.Diagnostics.Debug.WriteLine($"[UpdateDownload]   New exe path: {newExePath}");
                System.Diagnostics.Debug.WriteLine($"[UpdateDownload]   Backup path: {backupExePath}");

                var scriptContent = $@"@echo off
setlocal enabledelayedexpansion
set ""ERROR_OCCURRED=0""

echo ========================================
echo IEVR Mod Manager Update Script
echo Script started successfully
echo ========================================
chcp 65001 >nul 2>&1
if !ERRORLEVEL! neq 0 (
    echo Warning: Could not set code page, continuing anyway.
)

set ""CURRENT_EXE={escapedCurrentExe}""
set ""TEMP_FILE={escapedTempFile}""
set ""NEW_EXE={escapedNewExePath}""
set ""BACKUP_EXE={escapedBackupExePath}""
set ""TEMP_DIR={escapedTempDir}""
set ""EXE_NAME={currentExeName}""

echo ========================================
echo IEVR Mod Manager Update Script
echo ========================================
echo Current EXE: ""!CURRENT_EXE!""
echo Temp File: ""!TEMP_FILE!""
echo New EXE: ""!NEW_EXE!""
echo Backup EXE: ""!BACKUP_EXE!""
echo ========================================
if ""!CURRENT_EXE!""=="""" (
    echo ERROR: CURRENT_EXE variable is empty!
    goto error_exit
)
if ""!TEMP_FILE!""=="""" (
    echo ERROR: TEMP_FILE variable is empty!
    goto error_exit
)

echo.
echo Step 1: Ensuring application is closed...
set ""MAX_ATTEMPTS=3""
set ""ATTEMPT=0""

:check_app_status
set /a ATTEMPT+=1
echo [Step 1] Checking if application is running (attempt !ATTEMPT! of !MAX_ATTEMPTS!)

tasklist /FI ""IMAGENAME eq !EXE_NAME!"" 2>nul | find /I ""!EXE_NAME!"" >nul
set ""PROCESS_CHECK=!ERRORLEVEL!""
if !PROCESS_CHECK! NEQ 0 (
    echo [Step 1] Application is not running. Proceeding with update.
    goto proceed_with_update
)

echo [Step 1] Application process detected. Attempting to close.
if !ATTEMPT! EQU 1 (
    echo [Step 1] Sending graceful close signal.
    taskkill /IM ""!EXE_NAME!"" >nul 2>&1
) else (
    echo [Step 1] Forcing close.
    taskkill /F /IM ""!EXE_NAME!"" >nul 2>&1
)

echo [Step 1] Waiting 2 seconds for process to terminate.
timeout /t 2 /nobreak >nul 2>&1
if !ERRORLEVEL! NEQ 0 (
    echo [Step 1] Warning: timeout command failed, continuing anyway.
)

if !ATTEMPT! geq !MAX_ATTEMPTS! (
    echo [Step 1] WARNING: Reached maximum attempts.
    echo [Step 1] Proceeding with update anyway. If files are locked, the update will fail.
    echo [Step 1] You may need to close the application manually and try again.
    goto proceed_with_update
)

echo [Step 1] Rechecking process status...
goto check_app_status

:proceed_with_update
echo [Step 1] Proceeding with update process...
timeout /t 1 /nobreak >nul 2>&1

echo.
echo Step 2: Verifying files exist.
if not exist ""!TEMP_FILE!"" (
    echo ERROR: Update file not found at ""!TEMP_FILE!""
    set ""ERROR_OCCURRED=1""
    goto error_exit
)
echo Update file found: ""!TEMP_FILE!""

if not exist ""!CURRENT_EXE!"" (
    echo WARNING: Current executable not found at ""!CURRENT_EXE!""
    echo This might be okay if it's already been replaced.
) else (
    echo Current executable found: ""!CURRENT_EXE!""
)

echo.
echo Step 3: Creating backup.
if exist ""!BACKUP_EXE!"" (
    echo Removing old backup.
    del /F /Q ""!BACKUP_EXE!"" 2>nul
    if !ERRORLEVEL! NEQ 0 (
        echo WARNING: Could not remove old backup, continuing anyway.
    )
)

if exist ""!CURRENT_EXE!"" (
    echo Moving current executable to backup.
    echo Source: ""!CURRENT_EXE!""
    echo Destination: ""!BACKUP_EXE!""
    move /Y ""!CURRENT_EXE!"" ""!BACKUP_EXE!"" >nul 2>nul
    if !ERRORLEVEL! NEQ 0 (
        echo ERROR: Could not move current executable to backup
        echo This might be because the file is locked or in use.
        echo Trying to copy instead of move.
        copy /Y ""!CURRENT_EXE!"" ""!BACKUP_EXE!"" >nul 2>nul
        if !ERRORLEVEL! NEQ 0 (
            echo ERROR: Could not backup current executable
            goto error_exit
        )
        echo Backup created using copy method.
        del /F /Q ""!CURRENT_EXE!"" >nul 2>nul
    ) else (
        echo Backup created successfully.
    )
) else (
    echo No current executable to backup.
)

echo.
echo Step 4: Installing new executable.
echo Source: ""!TEMP_FILE!""
echo Destination: ""!NEW_EXE!""
echo Checking if temp file exists and has content.
if not exist ""!TEMP_FILE!"" (
    echo ERROR: Update file not found at ""!TEMP_FILE!""
    goto restore_backup
)
for %%A in (""!TEMP_FILE!"") do set ""TEMP_SIZE=%%~zA""
echo Temp file size: !TEMP_SIZE! bytes

echo Checking backup file size for comparison.
if exist ""!BACKUP_EXE!"" (
    for %%A in (""!BACKUP_EXE!"") do set ""BACKUP_SIZE=%%~zA""
    echo Backup file size: !BACKUP_SIZE! bytes
    if !TEMP_SIZE! EQU !BACKUP_SIZE! (
        echo WARNING: New file size matches backup file size.
        echo This might mean the same version was downloaded.
        echo Continuing anyway, but please verify the version.
    ) else (
        echo File sizes differ - this looks like a different version.
    )
)

echo Copying update file to target location.
copy /Y ""!TEMP_FILE!"" ""!NEW_EXE!"" >nul 2>nul
if !ERRORLEVEL! NEQ 0 (
    echo ERROR: Could not copy new executable
    goto restore_backup
)
echo New executable copied successfully.

echo Verifying copied file.
if not exist ""!NEW_EXE!"" (
    echo ERROR: New executable not found after copy
    goto restore_backup
)
for %%A in (""!NEW_EXE!"") do set ""NEW_SIZE_COPY=%%~zA""
echo Copied file size: !NEW_SIZE_COPY! bytes
if !NEW_SIZE_COPY! NEQ !TEMP_SIZE! (
    echo ERROR: File size mismatch after copy
    goto restore_backup
)

echo Deleting temp file.
del /F /Q ""!TEMP_FILE!"" >nul 2>nul

echo.
echo Step 5: Verifying installation.
if not exist ""!NEW_EXE!"" (
    echo ERROR: New executable not found after move
    goto restore_backup
)
echo New executable verified: ""!NEW_EXE!""

echo Verifying file size.
for %%A in (""!NEW_EXE!"") do set ""NEW_SIZE=%%~zA""
echo New executable size: !NEW_SIZE! bytes
if !NEW_SIZE! EQU 0 (
    echo ERROR: New executable is empty
    goto restore_backup
)

echo.
echo Step 6: Starting updated application.
echo Verifying we are starting the correct file.
echo File to start: ""!NEW_EXE!""
if not exist ""!NEW_EXE!"" (
    echo ERROR: New executable not found at ""!NEW_EXE!""
    goto restore_backup
)

echo Skipping version check (will start application directly).

echo Getting directory of new executable.
for %%A in (""!NEW_EXE!"") do set ""NEW_EXE_DIR=%%~dpA""
echo Directory: ""!NEW_EXE_DIR!""

echo Waiting 1 second before starting to ensure file is ready.
timeout /t 1 /nobreak >nul

echo Starting application.
cd /d ""!NEW_EXE_DIR!""
start """" ""!NEW_EXE!""
if !ERRORLEVEL! NEQ 0 (
    echo WARNING: Start command returned error code !ERRORLEVEL!
    echo Trying alternative method.
    ""!NEW_EXE!""
) else (
    echo Application started successfully.
)
echo Waiting 2 seconds for application to initialize.
timeout /t 2 /nobreak >nul

echo.
echo Step 7: Cleaning up.
echo Checking if application started successfully.
timeout /t 2 /nobreak >nul
tasklist /FI ""IMAGENAME eq !EXE_NAME!"" 2>nul | find /I ""!EXE_NAME!"" >nul
if !ERRORLEVEL! EQU 0 (
    echo Application is running successfully. Cleaning up temporary files.
    echo Waiting a moment before cleanup to ensure application is fully initialized.
    timeout /t 3 /nobreak >nul
    
    echo Removing backup file (.old).
    set ""BACKUP_REMOVED=0""
    if exist ""!BACKUP_EXE!"" (
        echo Attempting to delete: ""!BACKUP_EXE!""
        del /F /Q ""!BACKUP_EXE!"" 2>nul
        if !ERRORLEVEL! EQU 0 (
            echo Backup file removed successfully.
            set ""BACKUP_REMOVED=1""
        ) else (
            echo Warning: First attempt failed. Retrying after 2 seconds...
            timeout /t 2 /nobreak >nul
            del /F /Q ""!BACKUP_EXE!"" 2>nul
            if !ERRORLEVEL! EQU 0 (
                echo Backup file removed on retry.
                set ""BACKUP_REMOVED=1""
            ) else (
                echo Warning: Second attempt failed. Trying one more time...
                timeout /t 2 /nobreak >nul
                del /F /Q ""!BACKUP_EXE!"" 2>nul
                if !ERRORLEVEL! EQU 0 (
                    echo Backup file removed on third attempt.
                    set ""BACKUP_REMOVED=1""
                ) else (
                    echo ERROR: Could not remove backup file after 3 attempts.
                    echo The file may be locked by another process.
                    echo You can delete it manually: ""!BACKUP_EXE!""
                )
            )
        )
        
        if !BACKUP_REMOVED! EQU 0 (
            if exist ""!BACKUP_EXE!"" (
                echo Backup file still exists. Attempting to rename it for later deletion.
                set ""BACKUP_RENAME=!BACKUP_EXE!.delete""
                ren ""!BACKUP_EXE!"" ""!BACKUP_RENAME!"" 2>nul
                if !ERRORLEVEL! EQU 0 (
                    echo Backup file renamed. You can delete it manually: ""!BACKUP_RENAME!""
                )
            )
        )
    ) else (
        echo Backup file not found (may have been removed already).
    )
    
    echo Removing temp directory.
    if exist ""!TEMP_DIR!"" (
        rmdir /S /Q ""!TEMP_DIR!"" >nul 2>nul
        if !ERRORLEVEL! EQU 0 (
            echo Temp directory removed successfully.
        ) else (
            echo Warning: Could not remove temp directory (may be locked). Retrying...
            timeout /t 1 /nobreak >nul
            rmdir /S /Q ""!TEMP_DIR!"" >nul 2>nul
            if !ERRORLEVEL! EQU 0 (
                echo Temp directory removed on retry.
            ) else (
                echo Warning: Could not remove temp directory after retry. You can delete it manually: ""!TEMP_DIR!""
            )
        )
    ) else (
        echo Temp directory not found (may have been removed already).
    )
) else (
    echo Application not detected. Update may have failed. Keeping backup and temp files for recovery.
    echo Backup file: ""!BACKUP_EXE!""
    echo Temp directory: ""!TEMP_DIR!""
)

echo.
echo ========================================
echo Update completed successfully!
echo ========================================
echo.
echo Press any key to close this window...
pause >nul
exit /b 0

:restore_backup
echo.
echo ========================================
echo ERROR: Restoring backup...
echo ========================================
if exist ""!BACKUP_EXE!"" (
    echo Restoring backup executable.
    move /Y ""!BACKUP_EXE!"" ""!CURRENT_EXE!"" >nul 2>nul
    if !ERRORLEVEL! equ 0 (
        echo Backup restored successfully.
    ) else (
        echo ERROR: Could not restore backup!
    )
) else (
    echo ERROR: No backup found to restore!
)
:error_exit
echo.
echo ========================================
echo Update failed. Please try again or download manually.
echo ========================================
if exist ""!TEMP_DIR!"" (
    echo Keeping temp directory for debugging: ""!TEMP_DIR!""
)
echo.
echo Press any key to close this window...
pause >nul
exit /b 1
";

                // Write batch file without BOM (Windows batch files don't work with UTF-8 BOM)
                var utf8WithoutBom = new System.Text.UTF8Encoding(false);
                await File.WriteAllTextAsync(updateScript, scriptContent, utf8WithoutBom);
                System.Diagnostics.Debug.WriteLine($"[UpdateDownload] Update script created at: {updateScript}");

                progress?.Report((100, "Update downloaded successfully. Ready to install."));

                // Don't start the script yet - wait for user confirmation
                // The script path is stored in a static variable for later execution
                _pendingUpdateScript = updateScript;

                return true;
            }
            catch (HttpRequestException ex)
            {
                var errorMsg = $"Error downloading update: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[UpdateDownload] HttpRequestException: {ex.Message}");
                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateDownload] Inner exception: {ex.InnerException.Message}");
                }
                progress?.Report((0, errorMsg));
                return false;
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error downloading update: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[UpdateDownload] Exception: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[UpdateDownload] Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateDownload] Inner exception: {ex.InnerException.Message}");
                }
                progress?.Report((0, errorMsg));
                return false;
            }
        }

        /// <summary>
        /// Applies the downloaded update by executing the update script.
        /// </summary>
        public static void ApplyUpdate()
        {
            if (string.IsNullOrWhiteSpace(_pendingUpdateScript) || !File.Exists(_pendingUpdateScript))
            {
                System.Diagnostics.Debug.WriteLine("[UpdateCheck] No pending update script found");
                MessageBox.Show("No pending update found. Please download the update again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateCheck] Starting update script: {_pendingUpdateScript}");
                
                // Verify script exists and is readable
                var scriptInfo = new FileInfo(_pendingUpdateScript);
                if (!scriptInfo.Exists || scriptInfo.Length == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateCheck] Update script is missing or empty");
                    MessageBox.Show("Update script is missing or corrupted. Please download the update again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    _pendingUpdateScript = null;
                    return;
                }

                // Start the update script directly
                // Using cmd.exe /k to keep window open for debugging
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k \"\"{_pendingUpdateScript}\"\"",
                    WorkingDirectory = Path.GetDirectoryName(_pendingUpdateScript)!,
                    CreateNoWindow = false, // Show window for debugging
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Normal
                };

                System.Diagnostics.Debug.WriteLine($"[UpdateCheck] Executing: {startInfo.FileName} {startInfo.Arguments}");
                var process = Process.Start(startInfo);
                
                if (process == null)
                {
                    throw new Exception("Failed to start update process");
                }

                System.Diagnostics.Debug.WriteLine($"[UpdateCheck] Update script started with PID: {process.Id}");
                _pendingUpdateScript = null; // Clear after starting
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateCheck] Error starting update script: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[UpdateCheck] Stack trace: {ex.StackTrace}");
                MessageBox.Show($"Could not start update process: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Opens the GitHub releases page in the default web browser.
        /// </summary>
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
