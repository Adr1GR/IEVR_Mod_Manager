using System.Windows;

namespace IEVRModManager.Windows
{
    public partial class PendingChangesWindow : Window
    {
        public bool UserChoseContinue { get; private set; }

        public PendingChangesWindow(Window owner, string message)
        {
            InitializeComponent();
            Owner = owner;
            MessageText.Text = message;
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
