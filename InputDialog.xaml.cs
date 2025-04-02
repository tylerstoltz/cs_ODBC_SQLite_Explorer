using System.Windows;

namespace cs_ODBC_SQLite_Explorer
{
    public partial class InputDialog : Window
    {
        public string InputText { get; private set; } = string.Empty;

        public InputDialog(string prompt, string defaultInput = "")
        {
            InitializeComponent();
            PromptTextBlock.Text = prompt;
            InputTextBox.Text = defaultInput;
        }

        private void Window_ContentRendered(object sender, System.EventArgs e)
        {
            InputTextBox.SelectAll();
            InputTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            InputText = InputTextBox.Text;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
} 