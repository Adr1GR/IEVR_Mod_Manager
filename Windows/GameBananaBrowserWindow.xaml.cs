using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IEVRModManager.Helpers;

namespace IEVRModManager.Windows
{
    public partial class GameBananaBrowserWindow : Window
    {
        private readonly ObservableCollection<GameBananaMod> _mods = new();
        private readonly MainWindow _mainWindow;
        private const int PageSize = 50;
        private int _currentPage = 1;
        private bool _isLoading;
        private const int SkeletonCount = 8;
        private List<GameBananaMod> _pageMods = new();
        private string _searchQuery = string.Empty;
        private static readonly HttpClient SharedHttpClient;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
        private static readonly object CacheLock = new();
        private static readonly Dictionary<int, List<GameBananaMod>> _cachedModsByPage = new();
        private static readonly Dictionary<int, DateTime> _cacheTimestampByPage = new();
        private static readonly Dictionary<int, Task<List<GameBananaMod>>> _inflightFetchByPage = new();

        static GameBananaBrowserWindow()
        {
            SharedHttpClient = new HttpClient();
            SharedHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("IEVRModManager/1.0");
            SharedHttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            SharedHttpClient.Timeout = TimeSpan.FromSeconds(20);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GameBananaBrowserWindow"/> class.
        /// </summary>
        /// <param name="owner">The main window owner.</param>
        public GameBananaBrowserWindow(MainWindow owner)
        {
            InitializeComponent();
            Owner = owner;
            _mainWindow = owner;

            // Update localized texts
            UpdateLocalizedTexts();

            ModsList.ItemsSource = _mods;
            _ = LoadModsAsync();
        }
        
        private void UpdateLocalizedTexts()
        {
            Title = LocalizationHelper.GetString("GameBananaMods");
            RefreshButton.Content = LocalizationHelper.GetString("Refresh");
            SearchLabel.Text = LocalizationHelper.GetString("Search");
            StatusText.Text = LocalizationHelper.GetString("LoadingMods");
            FooterText.Text = LocalizationHelper.GetString("DoubleClickOpensModPage");
            PrevPageButton.Content = LocalizationHelper.GetString("PreviousPage");
            NextPageButton.Content = LocalizationHelper.GetString("NextPage");
            PageIndicator.Text = string.Format(LocalizationHelper.GetString("PageLabel"), _currentPage);
        }
        
        private void FooterText_Loaded(object sender, RoutedEventArgs e)
        {
            FooterText.Text = LocalizationHelper.GetString("DoubleClickOpensModPage");
        }

        private void ShowSkeletonPlaceholders()
        {
            _pageMods = new List<GameBananaMod>();
            _mods.Clear();
            for (var i = 0; i < SkeletonCount; i++)
            {
                _mods.Add(GameBananaMod.CreatePlaceholder());
            }
        }

        /// <summary>
        /// Prefetches mods from GameBanana in the background.
        /// </summary>
        /// <param name="owner">The main window instance for logging.</param>
        public static async Task PrefetchModsAsync(MainWindow? owner)
        {
            try
            {
                var mods = await GetOrFetchModsAsync(owner, 1, forceRefresh: false);
                owner?.LogMessage($"Prefetched {mods.Count} mods from GameBanana.", "info");
            }
            catch (Exception ex)
            {
                owner?.LogMessage($"Could not prefetch GameBanana mods: {ex.Message}", "error");
            }
        }

        private static List<GameBananaMod> CloneMods(IEnumerable<GameBananaMod> mods)
        {
            return mods.Select(m => new GameBananaMod
            {
                Id = m.Id,
                Name = m.Name,
                PageUrl = m.PageUrl,
                DownloadUrl = m.DownloadUrl,
                Downloads = m.Downloads,
                ThumbnailUrl = m.ThumbnailUrl,
                IsPlaceholder = false
            }).ToList();
        }

        private static bool CacheIsValid(int page)
        {
            return _cachedModsByPage.TryGetValue(page, out var mods)
                && mods.Count > 0
                && _cacheTimestampByPage.TryGetValue(page, out var timestamp)
                && (DateTime.UtcNow - timestamp) < CacheDuration;
        }

        private static Task<List<GameBananaMod>> StartFetchAsync(MainWindow? owner, int page)
        {
            var fetch = FetchModsPageCoreAsync(owner, page, PageSize);
            _inflightFetchByPage[page] = fetch;
            return fetch;
        }

        private static async Task<List<GameBananaMod>> GetOrFetchModsAsync(MainWindow? owner, int page, bool forceRefresh)
        {
            Task<List<GameBananaMod>>? fetchTask;
            lock (CacheLock)
            {
                if (!forceRefresh && CacheIsValid(page))
                {
                    return CloneMods(_cachedModsByPage[page]);
                }

                if (!forceRefresh && _inflightFetchByPage.TryGetValue(page, out var inflight))
                {
                    fetchTask = inflight;
                }
                else
                {
                    fetchTask = StartFetchAsync(owner, page);
                }
            }

            List<GameBananaMod> mods;
            try
            {
                mods = await fetchTask;
            }
            catch
            {
                lock (CacheLock)
                {
                    _inflightFetchByPage.Remove(page);
                }
                throw;
            }

            lock (CacheLock)
            {
                _cachedModsByPage[page] = mods;
                _cacheTimestampByPage[page] = DateTime.UtcNow;
                _inflightFetchByPage.Remove(page);
            }
            return CloneMods(mods);
        }

        private async Task LoadModsAsync(bool forceRefresh = false)
        {
            if (_isLoading)
            {
                return;
            }

            _isLoading = true;
            StatusText.Text = LocalizationHelper.GetString("LoadingMods");
            FooterText.Text = LocalizationHelper.GetString("FetchingGameBananaMods");
            ShowSkeletonPlaceholders();
            PrevPageButton.IsEnabled = false;
            NextPageButton.IsEnabled = false;
            PageIndicator.Text = string.Format(LocalizationHelper.GetString("PageLabel"), _currentPage);
            var loadFailed = false;

            try
            {
                var mods = await GetOrFetchModsAsync(_mainWindow, _currentPage, forceRefresh);
                _pageMods = mods;
            }
            catch (Exception ex)
            {
                loadFailed = true;
                StatusText.Text = LocalizationHelper.GetString("FailedToLoadMods");
                FooterText.Text = LocalizationHelper.GetString("CheckConnectionAndTryAgain");
                _mods.Clear();
                _pageMods = new List<GameBananaMod>();
                _mainWindow?.LogMessage($"Could not fetch mods from GameBanana: {ex.Message}", "error");
                var errorWindow = new MessageWindow(this, 
                    LocalizationHelper.GetString("ErrorTitle"), 
                    LocalizationHelper.GetString("CouldNotFetchGameBananaMods"), 
                    MessageType.Error);
                errorWindow.ShowDialog();
            }
            finally
            {
                _isLoading = false;
            }

            if (!loadFailed)
            {
                FooterText.Text = _pageMods.Count == 0
                    ? LocalizationHelper.GetString("NoModsFound")
                    : LocalizationHelper.GetString("DoubleClickOpensModPage");
                ApplySearchFilter();
            }
            else
            {
                UpdatePageControls(0, 0);
                StatusText.Text = LocalizationHelper.GetString("FailedToLoadMods");
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchQuery = SearchBox.Text?.Trim() ?? string.Empty;
            if (_isLoading)
            {
                return;
            }
            ApplySearchFilter();
        }

        private void ApplySearchFilter()
        {
            IEnumerable<GameBananaMod> filtered = _pageMods;
            if (!string.IsNullOrWhiteSpace(_searchQuery))
            {
                filtered = filtered.Where(m =>
                    (!string.IsNullOrWhiteSpace(m.Name) && m.Name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
                    || m.Id.ToString().Contains(_searchQuery, StringComparison.OrdinalIgnoreCase));
            }

            var filteredList = filtered.ToList();
            _mods.Clear();
            foreach (var mod in filteredList)
            {
                _mods.Add(mod);
            }

            UpdatePageControls(filteredList.Count, _pageMods.Count);
        }

        private void UpdatePageControls(int filteredCount, int totalCount)
        {
            PageIndicator.Text = string.Format(LocalizationHelper.GetString("PageLabel"), _currentPage);
            StatusText.Text = string.IsNullOrWhiteSpace(_searchQuery)
                ? string.Format(LocalizationHelper.GetString("PageModsCount"), _currentPage, totalCount)
                : string.Format(LocalizationHelper.GetString("PageModsCountFiltered"), _currentPage, filteredCount, totalCount);
            PrevPageButton.IsEnabled = !_isLoading && _currentPage > 1;
            NextPageButton.IsEnabled = !_isLoading && totalCount >= PageSize;
        }

        private async void PrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading || _currentPage <= 1)
            {
                return;
            }
            _currentPage--;
            await LoadModsAsync();
        }

        private async void NextPage_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading)
            {
                return;
            }
            _currentPage++;
            await LoadModsAsync();
        }

        private static async Task<List<GameBananaMod>> FetchModsPageCoreAsync(MainWindow? owner, int page, int pageSize)
        {
            // Prefer Game subfeed (apiv11) to get recent mods; enrich with Core/Item/Data when available
            var mods = await FetchModsFromSubfeedAsync(owner, page, pageSize);
            if (mods.Count == 0)
            {
                var ids = await FetchIdsFromCoreAsync(page, pageSize);
                owner?.LogMessage("Subfeed returned 0 mods, using Core/List/Section fallback.", "info");
                foreach (var id in ids)
                {
                    mods.Add(new GameBananaMod
                    {
                        Id = id,
                        Name = $"Mod {id}",
                        PageUrl = $"https://gamebanana.com/mods/{id}",
                        DownloadUrl = string.Empty,
                        Downloads = 0
                    });
                }
            }
            else
            {
                owner?.LogMessage($"Loaded {mods.Count} mods from GameBanana subfeed.", "info");
            }

            await EnrichModsWithDetailsAsync(owner, mods);
            var visible = mods.Where(m => !string.IsNullOrWhiteSpace(m.Name)).ToList();
            if (visible.Count == 0)
            {
                owner?.LogMessage("No mods could be loaded; check network or GameBanana response.", "error");
            }
            return visible;
        }

        private static async Task EnrichModsWithDetailsAsync(MainWindow? owner, List<GameBananaMod> mods)
        {
            foreach (var mod in mods)
            {
                try
                {
                    var detailed = await FetchModDetailsFromCoreAsync(mod.Id);
                    if (detailed != null)
                    {
                        mod.Name = detailed.Name;
                        mod.PageUrl = string.IsNullOrWhiteSpace(mod.PageUrl) ? detailed.PageUrl : mod.PageUrl;
                        mod.DownloadUrl = string.IsNullOrWhiteSpace(mod.DownloadUrl) ? detailed.DownloadUrl : mod.DownloadUrl;
                        mod.Downloads = detailed.Downloads;
                    }
                }
                catch (Exception ex)
                {
                    owner?.LogMessage($"Could not enrich mod {mod.Id}: {ex.Message}", "error");
                    // Keep minimal data; continue with others
                }
            }
        }

        private static async Task<List<GameBananaMod>> FetchModsFromSubfeedAsync(MainWindow? owner, int page, int pageSize)
        {
            var url = $"https://gamebanana.com/apiv11/Game/20069/Subfeed?_nPage={page}&_nPerPage={pageSize}&_sSort=new&_csvModelInclusions=Mod";
            using var response = await SharedHttpClient.GetAsync(url);
            var payload = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Game/Subfeed failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Body: {TrimForLog(payload)}");
            }

            var mods = new List<GameBananaMod>();
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.TryGetProperty("_aRecords", out var records) && records.ValueKind == JsonValueKind.Array)
            {
                foreach (var record in records.EnumerateArray())
                {
                    if (record.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var id = record.GetPropertyOrDefault("_idRow", 0);
                    var model = record.GetPropertyOrDefault("_sModelName", string.Empty);
                    if (id <= 0 || !string.Equals(model, "Mod", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var name = record.GetPropertyOrDefault("_sName", $"Mod {id}");
                    var pageUrl = record.GetPropertyOrDefault("_sProfileUrl", $"https://gamebanana.com/mods/{id}");
                    var thumb = ResolveThumb(record);
                    mods.Add(new GameBananaMod
                    {
                        Id = id,
                        Name = name,
                        PageUrl = pageUrl,
                        ThumbnailUrl = thumb,
                        DownloadUrl = string.Empty,
                        Downloads = 0
                    });
                }
            }
            else
            {
                var message = root.TryGetProperty("_sErrorMessage", out var errProp)
                    ? errProp.GetString()
                    : $"Unexpected subfeed response. Body: {TrimForLog(payload)}";
                throw new InvalidOperationException(message ?? "Unexpected subfeed response from GameBanana.");
            }

            if (mods.Count == 0)
            {
                owner?.LogMessage("GameBanana subfeed returned 0 records.", "error");
            }

            return mods;
        }

        private static async Task<List<int>> FetchIdsFromCoreAsync(int page, int pageSize)
        {
            var url = $"https://api.gamebanana.com/Core/List/Section?itemtype=Mod&filter=gameid&filterval=20069&sort=_nDownloadCount&direction=desc&format=json&page={page}&perpage={pageSize}";
            using var response = await SharedHttpClient.GetAsync(url);
            var payload = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Core/List/Section failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Body: {TrimForLog(payload)}");
            }

            var ids = new List<int>();
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var id))
                    {
                        ids.Add(id);
                    }
                    else if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsed))
                    {
                        ids.Add(parsed);
                    }
                }
            }
            else
            {
                var message = root.TryGetProperty("_sError", out var errProp)
                    ? errProp.GetString()
                    : $"Unexpected response from GameBanana. Body: {TrimForLog(payload)}";
                throw new InvalidOperationException(message ?? "Unexpected response from GameBanana.");
            }

            return ids;
        }

        private static async Task<GameBananaMod?> FetchModDetailsFromCoreAsync(int id)
        {
            const string fields = "_sName,_sProfileUrl,_sDownloadUrl,_nDownloadCount";
            var url = $"https://api.gamebanana.com/Core/Item/Data?itemtype=Mod&itemid={id}&fields={fields}&format=json";

            using var response = await SharedHttpClient.GetAsync(url);
            var payload = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Core/Item/Data failed for {id}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Body: {TrimForLog(payload)}");
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            // API returns array in the order of requested fields
            if (root.ValueKind == JsonValueKind.Array)
            {
                var name = root.GetArrayLength() > 0 ? root[0].GetString() : null;
                var pageUrl = root.GetArrayLength() > 1 ? root[1].GetString() : null;
                var downloadUrl = root.GetArrayLength() > 2 ? root[2].GetString() : null;
                var downloads = root.GetArrayLength() > 3 && root[3].ValueKind == JsonValueKind.Number
                    ? root[3].GetInt32()
                    : 0;

                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    downloadUrl = pageUrl;
                }

                if (string.IsNullOrWhiteSpace(name) || id <= 0)
                {
                    return null;
                }

                return new GameBananaMod
                {
                    Id = id,
                    Name = name!,
                    PageUrl = pageUrl ?? string.Empty,
                    DownloadUrl = downloadUrl ?? string.Empty,
                    Downloads = downloads
                };
            }

            return null;
        }

        private static GameBananaMod? ParseModFromElement(JsonElement element)
        {
            var id = element.GetPropertyOrDefault("_idRow", 0);
            var name = element.GetPropertyOrDefault("_sName", $"Mod {id}");
            var pageUrl = element.GetPropertyOrDefault("_sProfileUrl", string.Empty);
            var downloadUrl = element.GetPropertyOrDefault("_sDownloadUrl", string.Empty);
            var downloads = element.GetPropertyOrDefault("_nDownloadCount", 0);
            var thumb = ResolveThumb(element);

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                downloadUrl = pageUrl;
            }

            if (id <= 0 || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return new GameBananaMod
            {
                Id = id,
                Name = name,
                PageUrl = pageUrl,
                DownloadUrl = downloadUrl,
                Downloads = downloads,
                ThumbnailUrl = thumb
            };
        }

        private static string ResolveThumb(JsonElement element)
        {
            if (element.TryGetProperty("_aPreviewMedia", out var media) &&
                media.ValueKind == JsonValueKind.Object &&
                media.TryGetProperty("_aImages", out var images) &&
                images.ValueKind == JsonValueKind.Array)
            {
                var first = images.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object)
                {
                    var baseUrl = first.GetPropertyOrDefault("_sBaseUrl", string.Empty);
                    var file = first.GetPropertyOrDefault("_sFile530", string.Empty);
                    if (string.IsNullOrWhiteSpace(file))
                    {
                        file = first.GetPropertyOrDefault("_sFile", string.Empty);
                    }
                    if (!string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(file))
                    {
                        return $"{baseUrl}/{file}";
                    }
                }
            }
            return string.Empty;
        }

        private async Task DownloadModAsync(GameBananaMod mod)
        {
            if (string.IsNullOrWhiteSpace(mod.DownloadUrl))
            {
                // Try to resolve download URL just-in-time
                var detailed = await FetchModDetailsFromCoreAsync(mod.Id);
                if (detailed != null && !string.IsNullOrWhiteSpace(detailed.DownloadUrl))
                {
                    mod.DownloadUrl = detailed.DownloadUrl;
                    mod.PageUrl = string.IsNullOrWhiteSpace(mod.PageUrl) ? detailed.PageUrl : mod.PageUrl;
                    mod.Name = string.IsNullOrWhiteSpace(mod.Name) ? detailed.Name : mod.Name;
                    mod.Downloads = detailed.Downloads;
                }
            }

            if (string.IsNullOrWhiteSpace(mod.DownloadUrl))
            {
                var infoWindow = new MessageWindow(this, 
                    LocalizationHelper.GetString("InfoTitle"), 
                    LocalizationHelper.GetString("NoDownloadUrlFound"), 
                    MessageType.Info);
                infoWindow.ShowDialog();
                return;
            }

            try
            {
                var modsRoot = Config.DefaultModsDir;
                Directory.CreateDirectory(modsRoot);

                var targetDir = GetUniqueDirectory(modsRoot, SanitizeName(mod.Name));
                var tmpDir = Config.DefaultTmpDir;
                Directory.CreateDirectory(tmpDir);
                var tmpFile = Path.Combine(tmpDir, $"gb_{mod.Id}_{Guid.NewGuid():N}");

                var data = await SharedHttpClient.GetByteArrayAsync(mod.DownloadUrl);
                await File.WriteAllBytesAsync(tmpFile, data);

                var extension = Path.GetExtension(mod.DownloadUrl);
                if (string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    ZipFile.ExtractToDirectory(tmpFile, targetDir, true);
                    File.Delete(tmpFile);
                }
                else
                {
                    Directory.CreateDirectory(targetDir);
                    var fileName = Path.GetFileName(mod.DownloadUrl);
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        fileName = $"mod_{mod.Id}{extension}";
                    }
                    var destination = Path.Combine(targetDir, fileName);
                    File.Copy(tmpFile, destination, true);
                    File.Delete(tmpFile);
                }

                _mainWindow?.LogMessage($"Downloaded {mod.Name} to {targetDir}", "success");
                var successWindow = new MessageWindow(this, 
                    LocalizationHelper.GetString("SuccessTitle"), 
                    string.Format(LocalizationHelper.GetString("DownloadComplete"), mod.Name), 
                    MessageType.Success);
                successWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                _mainWindow?.LogMessage($"Failed to download {mod.Name}: {ex.Message}", "error");
                var errorWindow = new MessageWindow(this, 
                    LocalizationHelper.GetString("ErrorTitle"), 
                    string.Format(LocalizationHelper.GetString("CouldNotDownloadMod"), ex.Message), 
                    MessageType.Error);
                errorWindow.ShowDialog();
            }
        }

        private static string GetUniqueDirectory(string root, string name)
        {
            var candidate = Path.Combine(root, name);
            var counter = 1;
            while (Directory.Exists(candidate))
            {
                candidate = Path.Combine(root, $"{name}_{counter}");
                counter++;
            }

            return candidate;
        }

        private static string SanitizeName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(sanitized) ? "mod" : sanitized;
        }

        private static string TrimForLog(string? text, int max = 200)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            return text.Length <= max ? text : text[..max] + "...";
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadModsAsync(forceRefresh: true);
        }

        private async void DownloadSelected_Click(object sender, RoutedEventArgs e)
        {
            if (ModsList.SelectedItem is GameBananaMod mod)
            {
                await DownloadModAsync(mod);
            }
            else
            {
                var infoWindow = new MessageWindow(this, 
                    LocalizationHelper.GetString("InfoTitle"), 
                    LocalizationHelper.GetString("SelectModToDownload"), 
                    MessageType.Info);
                infoWindow.ShowDialog();
            }
        }

        private async void DownloadRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is GameBananaMod mod)
            {
                await DownloadModAsync(mod);
            }
        }

        private void OpenPage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is GameBananaMod mod)
            {
                OpenModPage(mod);
            }
        }

        private void ModsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ModsList.SelectedItem is GameBananaMod mod)
            {
                OpenModPage(mod);
            }
        }

        private void OpenModPage(GameBananaMod mod)
        {
            if (mod.IsPlaceholder)
            {
                return;
            }

            var target = string.IsNullOrWhiteSpace(mod.PageUrl) ? mod.DownloadUrl : mod.PageUrl;
            if (string.IsNullOrWhiteSpace(target))
            {
                var infoWindow = new MessageWindow(this, 
                    LocalizationHelper.GetString("InfoTitle"), 
                    LocalizationHelper.GetString("NoLinkAvailable"), 
                    MessageType.Info);
                infoWindow.ShowDialog();
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _mainWindow?.LogMessage($"Could not open link: {ex.Message}", "error");
            }
        }
    }

    /// <summary>
    /// Represents a mod from GameBanana.
    /// </summary>
    public class GameBananaMod
    {
        /// <summary>
        /// Gets or sets the mod ID.
        /// </summary>
        public int Id { get; set; }
        
        /// <summary>
        /// Gets or sets the mod name.
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the URL to the mod's page.
        /// </summary>
        public string PageUrl { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the download URL for the mod.
        /// </summary>
        public string DownloadUrl { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the number of downloads.
        /// </summary>
        public int Downloads { get; set; }
        
        /// <summary>
        /// Gets or sets the thumbnail image URL.
        /// </summary>
        public string ThumbnailUrl { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets whether this is a placeholder mod (shown while loading).
        /// </summary>
        public bool IsPlaceholder { get; set; }

        /// <summary>
        /// Creates a placeholder mod instance for loading states.
        /// </summary>
        /// <returns>A placeholder <see cref="GameBananaMod"/> instance.</returns>
        public static GameBananaMod CreatePlaceholder()
        {
            return new GameBananaMod
            {
                Id = 0,
                Name = "Loading...",
                IsPlaceholder = true,
                ThumbnailUrl = string.Empty,
                PageUrl = string.Empty,
                DownloadUrl = string.Empty
            };
        }
    }

    internal static class JsonExtensions
    {
        public static string GetPropertyOrDefault(this JsonElement element, string name, string fallback)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString() ?? fallback;
            }
            return fallback;
        }

        public static int GetPropertyOrDefault(this JsonElement element, string name, int fallback)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value))
            {
                return value;
            }
            return fallback;
        }
    }
}
