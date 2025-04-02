using System.Collections.Generic;
using System.Windows;
using System.Linq;

namespace cs_ODBC_SQLite_Explorer
{
    public partial class DsnSelectionWindow : Window
    {
        public string SelectedDsn { get; private set; } = string.Empty;

        public DsnSelectionWindow(List<string> availableDsns)
        {
            InitializeComponent();
            
            // Fill the ListBox with DSNs
            DsnListBox.ItemsSource = availableDsns;
            
            // Select the first item if available
            if (availableDsns.Count > 0)
            {
                DsnListBox.SelectedIndex = 0;
            }

            Title = "Select ODBC Data Source";
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (DsnListBox.SelectedItem != null)
            {
                SelectedDsn = DsnListBox.SelectedItem.ToString() ?? string.Empty;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please select a DSN.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
} 