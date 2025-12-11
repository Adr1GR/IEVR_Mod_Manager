using System.Windows;
using System.Windows.Threading;
using IEVRModManager.Helpers;

namespace IEVRModManager.Windows
{
    public partial class ProgressWindow : Window
    {
        private readonly Dispatcher _dispatcher;
        private bool _allowClose = false;

        public ProgressWindow(Window parent)
        {
            InitializeComponent();
            Owner = parent;
            _dispatcher = Dispatcher;
            
            // Update localized texts
            Title = LocalizationHelper.GetString("ApplyingMods");
            TitleText.Text = LocalizationHelper.GetString("ApplyingMods");
            
            // Prevent closing while processing, unless explicitly allowed
            Closing += (s, e) =>
            {
                if (!_allowClose)
                {
                    e.Cancel = true;
                }
            };
        }

        public void AllowClose()
        {
            _allowClose = true;
            Close();
        }

        public void UpdateProgress(int percentage, string status)
        {
            _dispatcher.Invoke(() =>
            {
                ProgressBar.Value = percentage;
                StatusText.Text = status;
                PercentageText.Text = $"{percentage}%";
            });
        }

        public void SetIndeterminate(bool indeterminate)
        {
            _dispatcher.Invoke(() =>
            {
                if (indeterminate)
                {
                    ProgressBar.IsIndeterminate = true;
                    PercentageText.Text = "";
                }
                else
                {
                    ProgressBar.IsIndeterminate = false;
                }
            });
        }
    }
}

