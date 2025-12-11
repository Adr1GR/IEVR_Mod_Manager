using System;
using System.Windows;
using System.Windows.Threading;
using IEVRModManager.Helpers;
using IEVRModManager.Managers;

namespace IEVRModManager.Windows
{
    public partial class AppUpdateWindow : Window
    {
        private readonly AppUpdateManager.ReleaseInfo _releaseInfo;
        private readonly string _currentVersion;
        private bool _isDownloading;

        public AppUpdateWindow(Window owner, AppUpdateManager.ReleaseInfo releaseInfo, string currentVersion)
        {
            InitializeComponent();
            Owner = owner;
            _releaseInfo = releaseInfo;
            _currentVersion = currentVersion;
            
            UpdateLocalizedTexts();
            Loaded += (s, e) => UpdateLocalizedTexts();
        }

        private void UpdateLocalizedTexts()
        {
            Title = LocalizationHelper.GetString("AppUpdateAvailable");
            TitleText.Text = LocalizationHelper.GetString("AppUpdateAvailable");
            
            var message = string.Format(
                LocalizationHelper.GetString("AppUpdateAvailableMessage"),
                _currentVersion,
                _releaseInfo.TagName.TrimStart('v', 'V')
            );
            MessageText.Text = message;

            if (!string.IsNullOrWhiteSpace(_releaseInfo.Body))
            {
                ReleaseNotesText.Text = LocalizationHelper.GetString("ReleaseNotes") + ":\n\n" + _releaseInfo.Body;
                ReleaseNotesText.Visibility = Visibility.Visible;
            }

            DownloadButton.Content = LocalizationHelper.GetString("DownloadAndInstall");
            OpenReleasesButton.Content = LocalizationHelper.GetString("OpenReleasesPage");
            LaterButton.Content = LocalizationHelper.GetString("Later");
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isDownloading)
                return;

            _isDownloading = true;
            DownloadButton.IsEnabled = false;
            OpenReleasesButton.IsEnabled = false;
            LaterButton.IsEnabled = false;

            ProgressBar.Visibility = Visibility.Visible;
            ProgressBar.Value = 0;
            ProgressText.Visibility = Visibility.Visible;
            ProgressText.Text = "";

            var progress = new Progress<(int percentage, string status)>(update =>
            {
                Dispatcher.Invoke(() =>
                {
                    ProgressBar.Value = update.percentage;
                    ProgressText.Text = update.status;
                });
            });

            try
            {
                var success = await AppUpdateManager.DownloadUpdateAsync(_releaseInfo, progress);
                
                if (success)
                {
                    var result = MessageBox.Show(
                        LocalizationHelper.GetString("UpdateDownloadedRestartMessage"),
                        LocalizationHelper.GetString("UpdateDownloaded"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information
                    );

                    if (result == MessageBoxResult.Yes)
                    {
                        Application.Current.Shutdown();
                    }
                    else
                    {
                        Close();
                    }
                }
                else
                {
                    MessageBox.Show(
                        LocalizationHelper.GetString("UpdateDownloadFailed"),
                        LocalizationHelper.GetString("ErrorTitle"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    DownloadButton.IsEnabled = true;
                    OpenReleasesButton.IsEnabled = true;
                    LaterButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(LocalizationHelper.GetString("UpdateDownloadError"), ex.Message),
                    LocalizationHelper.GetString("ErrorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                DownloadButton.IsEnabled = true;
                OpenReleasesButton.IsEnabled = true;
                LaterButton.IsEnabled = true;
            }
            finally
            {
                _isDownloading = false;
            }
        }

        private void OpenReleasesButton_Click(object sender, RoutedEventArgs e)
        {
            AppUpdateManager.OpenReleasesPage();
        }

        private void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
