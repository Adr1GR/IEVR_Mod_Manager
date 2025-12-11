using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using IEVRModManager.Helpers;
using IEVRModManager.Managers;
using IEVRModManager.Models;

namespace IEVRModManager.Windows
{
    public partial class ProfileManagerWindow : Window
    {
        private readonly ProfileManager _profileManager;
        private readonly ObservableCollection<ProfileViewModel> _profiles;
        public ModProfile? SelectedProfile { get; private set; }
        public bool ProfileLoaded { get; private set; }

        public ProfileManagerWindow(Window owner)
        {
            InitializeComponent();
            Owner = owner;
            
            _profileManager = new ProfileManager();
            _profiles = new ObservableCollection<ProfileViewModel>();
            
            ProfilesListBox.ItemsSource = _profiles;
            
            UpdateLocalizedTexts();
            LoadProfiles();
            
            ProfilesListBox.SelectionChanged += ProfilesListBox_SelectionChanged;
        }

        private void UpdateLocalizedTexts()
        {
            Title = LocalizationHelper.GetString("ModProfiles");
            TitleLabel.Content = LocalizationHelper.GetString("ModProfiles");
            LoadButton.Content = LocalizationHelper.GetString("Load");
            SaveButton.Content = LocalizationHelper.GetString("Save");
            DeleteButton.Content = LocalizationHelper.GetString("Delete");
            CloseButton.Content = LocalizationHelper.GetString("Close");
        }

        private void LoadProfiles()
        {
            _profiles.Clear();
            var profiles = _profileManager.GetAllProfiles();
            
            foreach (var profile in profiles)
            {
                var enabledCount = profile.Mods.Count(m => m.Enabled);
                var totalCount = profile.Mods.Count;
                _profiles.Add(new ProfileViewModel
                {
                    Name = profile.Name,
                    CreatedDate = profile.CreatedDate,
                    LastModifiedDate = profile.LastModifiedDate,
                    ModsCount = $"{enabledCount}/{totalCount} mods enabled",
                    Profile = profile
                });
            }
        }

        private void ProfilesListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (ProfilesListBox.SelectedItem is ProfileViewModel selected)
            {
                ProfileNameTextBox.Text = selected.Name;
            }
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesListBox.SelectedItem is ProfileViewModel selected)
            {
                SelectedProfile = selected.Profile;
                ProfileLoaded = true;
                DialogResult = true;
                Close();
            }
            else
            {
                var infoWindow = new MessageWindow(this, LocalizationHelper.GetString("ModProfiles"), LocalizationHelper.GetString("SelectProfileToLoad"), MessageType.Info);
                infoWindow.ShowDialog();
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var profileName = ProfileNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(profileName))
            {
                var warningWindow = new MessageWindow(this, LocalizationHelper.GetString("ModProfiles"), LocalizationHelper.GetString("EnterProfileName"), MessageType.Warning);
                warningWindow.ShowDialog();
                return;
            }

            // Check if profile name already exists
            if (_profileManager.ProfileExists(profileName) && 
                ProfilesListBox.SelectedItem is ProfileViewModel selected && 
                selected.Name != profileName)
            {
                var resultWindow = new MessageWindow(
                    this,
                    LocalizationHelper.GetString("ModProfiles"),
                    string.Format(LocalizationHelper.GetString("ProfileExistsOverwrite"), profileName),
                    MessageType.Warning,
                    MessageButtons.YesNo);
                
                var result = resultWindow.ShowDialog();
                if (result != true || resultWindow.Result != true)
                {
                    return;
                }
            }

            // Get current mod configuration from parent window
            if (Owner is MainWindow mainWindow)
            {
                var profile = mainWindow.CreateProfileFromCurrentState(profileName);
                
                if (_profileManager.SaveProfile(profile))
                {
                    var successWindow = new MessageWindow(this, LocalizationHelper.GetString("ModProfiles"), string.Format(LocalizationHelper.GetString("ProfileSaved"), profileName), MessageType.Success);
                    successWindow.ShowDialog();
                    LoadProfiles();
                    ProfileNameTextBox.Text = string.Empty;
                }
                else
                {
                    var errorWindow = new MessageWindow(this, LocalizationHelper.GetString("ModProfiles"), LocalizationHelper.GetString("ErrorSavingProfile"), MessageType.Error);
                    errorWindow.ShowDialog();
                }
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesListBox.SelectedItem is ProfileViewModel selected)
            {
                var resultWindow = new MessageWindow(
                    this,
                    LocalizationHelper.GetString("ModProfiles"),
                    string.Format(LocalizationHelper.GetString("ConfirmDeleteProfile"), selected.Name),
                    MessageType.Warning,
                    MessageButtons.YesNo);
                
                var result = resultWindow.ShowDialog();
                if (result == true && resultWindow.Result == true)
                {
                    if (_profileManager.DeleteProfile(selected.Name))
                    {
                        LoadProfiles();
                        ProfileNameTextBox.Text = string.Empty;
                    }
                    else
                    {
                        var errorWindow = new MessageWindow(this, LocalizationHelper.GetString("ModProfiles"), LocalizationHelper.GetString("ErrorDeletingProfile"), MessageType.Error);
                        errorWindow.ShowDialog();
                    }
                }
            }
            else
            {
                var infoWindow = new MessageWindow(this, LocalizationHelper.GetString("ModProfiles"), LocalizationHelper.GetString("SelectProfileToDelete"), MessageType.Info);
                infoWindow.ShowDialog();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public class ProfileViewModel
    {
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public DateTime LastModifiedDate { get; set; }
        public string ModsCount { get; set; } = string.Empty;
        public ModProfile Profile { get; set; } = null!;
        
        public string LastModifiedDateDisplay => LastModifiedDate.ToString("yyyy-MM-dd HH:mm");
        
        public override string ToString() => Name;
    }
}

