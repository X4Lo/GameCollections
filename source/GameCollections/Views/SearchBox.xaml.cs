using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GameCollections.Views
{
    /// <summary>
    /// Text box with a search icon, placeholder text and a clear button. Escape clears the text.
    /// </summary>
    public partial class SearchBox : UserControl
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(SearchBox),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
            nameof(Placeholder), typeof(string), typeof(SearchBox), new PropertyMetadata(string.Empty));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public string Placeholder
        {
            get => (string)GetValue(PlaceholderProperty);
            set => SetValue(PlaceholderProperty, value);
        }

        public SearchBox()
        {
            InitializeComponent();
            InputBox.PreviewKeyDown += InputBox_PreviewKeyDown;
        }

        public void FocusInput()
        {
            InputBox.Focus();
            Keyboard.Focus(InputBox);
            InputBox.SelectAll();
        }

        private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Only consume Escape when there is something to clear, so dialogs still close on Escape.
            if (e.Key == Key.Escape && !string.IsNullOrEmpty(Text))
            {
                Text = string.Empty;
                e.Handled = true;
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            Text = string.Empty;
            FocusInput();
        }
    }
}
