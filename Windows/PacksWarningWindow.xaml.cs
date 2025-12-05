using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace IEVRModManager.Windows
{
    public partial class PacksWarningWindow : Window
    {
        public bool UserChoseContinue { get; private set; }

        public PacksWarningWindow(Window parent, List<string> modNames)
        {
            InitializeComponent();
            Owner = parent;

            ModsListControl.ItemsSource = modNames?.Where(n => !string.IsNullOrWhiteSpace(n)).ToList() ?? new List<string>();

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
