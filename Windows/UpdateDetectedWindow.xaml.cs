using System.Windows;
using IEVRModManager.Helpers;

namespace IEVRModManager.Windows
{
    public partial class UpdateDetectedWindow : Window
    {
        public bool UserWantsBackup { get; private set; }

        public UpdateDetectedWindow(Window owner, string message)
        {
            InitializeComponent();
            Owner = owner;
            
            // Update localized texts
            UpdateLocalizedTexts(message);
            
            // Ensure texts are updated after window is loaded
            Loaded += (s, e) => UpdateLocalizedTexts(message);
        }
        
        private void UpdateLocalizedTexts(string message)
        {
            Title = LocalizationHelper.GetString("UpdateDetected");
            TitleText.Text = LocalizationHelper.GetString("UpdateDetected");
            MessageText.Text = message;
            CreateBackupButton.Content = LocalizationHelper.GetString("CreateBackup");
            CloseButton.Content = LocalizationHelper.GetString("Close");
        }

        private void CreateBackup_Click(object sender, RoutedEventArgs e)
        {
            UserWantsBackup = true;
            DialogResult = true;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            UserWantsBackup = false;
            DialogResult = false;
            Close();
        }
    }
}

