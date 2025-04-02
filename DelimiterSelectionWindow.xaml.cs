using System.Windows;

namespace cs_ODBC_SQLite_Explorer
{
    public partial class DelimiterSelectionWindow : Window
    {
        public string SelectedDelimiter { get; private set; } = ","; // Default to comma

        public DelimiterSelectionWindow()
        {
            InitializeComponent();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Set the selected delimiter based on radio button selection
            if (CommaRadioButton.IsChecked == true)
            {
                SelectedDelimiter = ",";
            }
            else if (PipeRadioButton.IsChecked == true)
            {
                SelectedDelimiter = "|";
            }
            else if (TabRadioButton.IsChecked == true)
            {
                SelectedDelimiter = "\t";
            }

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