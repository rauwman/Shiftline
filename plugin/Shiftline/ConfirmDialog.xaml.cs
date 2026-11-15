using System.Windows;
using System.Windows.Input;

namespace Shiftline.Plugin
{
    /// <summary>A small question window in the style of the plugin (replaces the grey Windows message box).</summary>
    public partial class ConfirmDialog : Window
    {
        public ConfirmDialog()
        {
            InitializeComponent();
            // Attached here, not in the XAML: events on the root element make the generated code resolve "Shiftline.Plugin" wrongly (CS0426).
            MouseLeftButtonDown += Window_MouseLeftButtonDown;
            PreviewKeyDown += Window_PreviewKeyDown;
        }

        private ConfirmDialog(string title, string message, string yes, string no)
            : this()
        {
            TitleText.Text = title;
            MessageText.Text = message;
            YesButton.Content = yes;
            NoButton.Content = no;
        }

        /// <summary>Shows the question centred over the plugin window; true when the person confirmed.</summary>
        public static bool Ask(DependencyObject owner, string title, string message, string yes, string no)
        {
            ConfirmDialog dialog = new ConfirmDialog(title, message, yes, no);
            Window ownerWindow = owner != null ? Window.GetWindow(owner) : null;
            if (ownerWindow != null)
            {
                dialog.Owner = ownerWindow;
            }

            return dialog.ShowDialog() == true;
        }

        private void Yes_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void No_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && !(e.OriginalSource is FrameworkElement && ((FrameworkElement)e.OriginalSource).TemplatedParent is System.Windows.Controls.Button))
            {
                DragMove();
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
            }
            else if (e.Key == Key.Enter)
            {
                DialogResult = true;
            }
        }
    }
}
