using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace IEVRModManager.Windows
{
    public partial class ConflictWarningWindow : Window
    {
        public bool UserChoseContinue { get; private set; } = false;

        public ConflictWarningWindow(Window parent, Dictionary<string, List<string>> conflicts)
        {
            InitializeComponent();
            Owner = parent;

            // Create a list of anonymous objects to display in the ItemsControl
            var conflictItems = conflicts.Select(kvp => new
            {
                Key = kvp.Key,
                Value = $"Mods: {string.Join(", ", kvp.Value)}"
            }).ToList();

            ConflictsListControl.ItemsSource = conflictItems;

            // Close with Escape key
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Cancel_Click(null!, null!);
                }
            };

            Loaded += (s, e) =>
            {
                CancelButton.Focus();
            };
        }

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            UserChoseContinue = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            UserChoseContinue = false;
            DialogResult = false;
            Close();
        }
    }
}