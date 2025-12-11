using System.Windows;
using System.Windows.Input;
using IEVRModManager.Helpers;

namespace IEVRModManager.Windows
{
    public enum MessageType
    {
        Info,
        Error,
        Warning,
        Success
    }

    public enum MessageButtons
    {
        OK,
        YesNo
    }

    public partial class MessageWindow : Window
    {
        public bool? Result { get; private set; }

        public MessageWindow(Window parent, string title, string message, MessageType type = MessageType.Info, MessageButtons buttons = MessageButtons.OK)
        {
            InitializeComponent();
            Owner = parent;
            
            Title = title;
            TitleText.Text = title;
            MessageTextBlock.Text = message;
            
            // Set icon and color based on type
            switch (type)
            {
                case MessageType.Error:
                    IconText.Text = "✕";
                    IconText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ErrorBrush");
                    break;
                case MessageType.Warning:
                    IconText.Text = "⚠";
                    IconText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "WarningBrush");
                    break;
                case MessageType.Success:
                    IconText.Text = "✓";
                    IconText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "SuccessBrush");
                    break;
                case MessageType.Info:
                default:
                    IconText.Text = "ℹ";
                    IconText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "AccentBrush");
                    break;
            }
            
            // Show appropriate buttons
            if (buttons == MessageButtons.OK)
            {
                OKButton.Visibility = Visibility.Visible;
                OKButton.Content = LocalizationHelper.GetString("OK");
            }
            else if (buttons == MessageButtons.YesNo)
            {
                YesButton.Visibility = Visibility.Visible;
                NoButton.Visibility = Visibility.Visible;
                YesButton.Content = LocalizationHelper.GetString("Yes");
                NoButton.Content = LocalizationHelper.GetString("No");
            }
            
            // Close with Enter or Escape key
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && OKButton.Visibility == Visibility.Visible)
                {
                    OK_Click(null!, null!);
                }
                else if (e.Key == Key.Escape)
                {
                    if (NoButton.Visibility == Visibility.Visible)
                    {
                        No_Click(null!, null!);
                    }
                    else
                    {
                        OK_Click(null!, null!);
                    }
                }
            };
            
            Loaded += (s, e) => 
            {
                if (OKButton.Visibility == Visibility.Visible)
                    OKButton.Focus();
                else if (YesButton.Visibility == Visibility.Visible)
                    YesButton.Focus();
            };
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            DialogResult = true;
            Close();
        }

        private void Yes_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            DialogResult = true;
            Close();
        }

        private void No_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            DialogResult = false;
            Close();
        }
    }
}

