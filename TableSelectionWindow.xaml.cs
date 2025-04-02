using System.Collections.Generic;
using System.ComponentModel; // For ICollectionView
using System.Diagnostics; // Added for Debug
using System.IO; // Added
using System.Linq;
using System.Windows;
using System.Windows.Controls; // For TextBox, CheckBox
using System.Windows.Data; // For CollectionViewSource
using Microsoft.Win32; // Added for SaveFileDialog
using System.Windows.Input; // Added for MouseButtonEventArgs
using System.Windows.Media; // Added for VisualTreeHelper

namespace cs_ODBC_SQLite_Explorer
{
    public partial class TableSelectionWindow : Window
    {
        private readonly List<string>? _allTableNames; // Make nullable
        private readonly ICollectionView? _tableView;   // Make nullable
        private bool _isSelectAllChanging = false; // Flag to prevent event recursion
        private bool _isApplyingTemplate = false; // Flag for template application

        // For Templates
        private string _templatesDirectory = string.Empty;
        private class TemplateItem { public string Name { get; set; } = string.Empty; public string FilePath { get; set; } = string.Empty; }
        private int _loadedTemplateIndex = -1; // Store index of loaded template

        public List<string>? SelectedTables { get; private set; } = null; // Null means select all initially
        public int SelectedRowLimit { get; private set; }

        public TableSelectionWindow(List<string> allTableNames, int _/*ignoredDefaultRowLimit*/)
        {
            InitializeComponent();
            // DataContext = this; // Don't set DataContext to 'this' if using CollectionViewSource directly
            try
            {
                InitializeTemplatesDirectory();

                _allTableNames = allTableNames ?? new List<string>();

                // Create and bind the filtered view
                _tableView = CollectionViewSource.GetDefaultView(_allTableNames);
                _tableView.Filter = FilterTables; // Set the filter predicate
                TableListBox.ItemsSource = _tableView;

                RowLimitTextBox.Text = "0"; // Default to 0
                Debug.WriteLine($"TableSelectionWindow Constructor: Set RowLimitTextBox.Text to: 0"); // Changed Debug

                LoadTemplates(); // Load templates into ComboBox

                // Select all items in the view initially
                SelectAllVisibleItems();
                UpdateSelectAllCheckBoxState(); // Update checkbox based on initial selection
                Debug.WriteLine("TableSelectionWindow Constructor: Initialization complete."); // Added Debug
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"!!! EXCEPTION in TableSelectionWindow Constructor: {ex}"); // Added Debug for errors
                MessageBox.Show($"An error occurred initializing the table selection window: {ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                // Optionally close or set dialog result to false here if the error is critical
                // DialogResult = false;
                // Close();
            }
        }

        private void InitializeTemplatesDirectory()
        {
            try
            {
                string myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                _templatesDirectory = System.IO.Path.Combine(myDocuments, "OdbcExplorerTableTemplates");
                if (!Directory.Exists(_templatesDirectory))
                {
                    Directory.CreateDirectory(_templatesDirectory);
                    Debug.WriteLine($"Created table templates directory: {_templatesDirectory}");
                }
                else
                {
                    Debug.WriteLine($"Using table templates directory: {_templatesDirectory}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing table templates directory: {ex}");
                MessageBox.Show($"Could not create or access the table templates directory: {_templatesDirectory}\nError: {ex.Message}", "Directory Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                TemplateComboBox.IsEnabled = false; // Disable if directory fails
            }
        }

        private void LoadTemplates()
        {
            TemplateComboBox.SelectionChanged -= TemplateComboBox_SelectionChanged;
            TemplateComboBox.Items.Clear();
            TemplateComboBox.Items.Add(new TemplateItem { Name = "<Load Template>" });
            TemplateComboBox.SelectedIndex = 0;

            if (string.IsNullOrEmpty(_templatesDirectory) || !Directory.Exists(_templatesDirectory))
                return;

            try
            {
                // Look for .txt files (adjust if different extension preferred)
                var templateFiles = Directory.EnumerateFiles(_templatesDirectory, "*.txt");
                foreach (var filePath in templateFiles.OrderBy(f => f))
                {
                    TemplateComboBox.Items.Add(new TemplateItem
                    {
                        Name = System.IO.Path.GetFileNameWithoutExtension(filePath),
                        FilePath = filePath
                    });
                }
            }
            catch (Exception ex)
            {
                 Debug.WriteLine($"Error loading table templates: {ex}");
                 MessageBox.Show($"Could not load table templates from directory: {_templatesDirectory}\nError: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                 TemplateComboBox.SelectionChanged += TemplateComboBox_SelectionChanged;
                 SaveTemplateButton.IsEnabled = !string.IsNullOrEmpty(_templatesDirectory); // Enable button if dir exists
            }
        }

        // Filter predicate
        private bool FilterTables(object item)
        {
            // Add null check for _tableView
            if (_tableView == null) return false;

            if (string.IsNullOrEmpty(FilterTextBox.Text))
                return true; // No filter text means show everything

            var tableName = item as string;
            return tableName != null && tableName.IndexOf(FilterTextBox.Text, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Event Handlers
        private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _tableView?.Refresh(); // Use null-conditional operator
            UpdateSelectAllCheckBoxState(); // Update checkbox state after filter changes
        }

        private void TemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TemplateComboBox.SelectedIndex > 0 && // Index 0 is the placeholder
                TemplateComboBox.SelectedItem is TemplateItem selectedTemplate)
            {
                _isApplyingTemplate = true; // Set flag before applying
                ApplyTemplateSelection(selectedTemplate.FilePath);
                _loadedTemplateIndex = TemplateComboBox.SelectedIndex; // Store the index
                // Don't reset selection back to placeholder immediately
                 _isApplyingTemplate = false; // Clear flag after applying
            }
        }

        private void ApplyTemplateSelection(string templateFilePath)
        {
            if (_allTableNames == null) return;

            try
            {
                List<string> tablesInTemplate = File.ReadAllLines(templateFilePath)
                                                   .Select(line => line.Trim()) // Trim whitespace
                                                   .Where(line => !string.IsNullOrWhiteSpace(line)) // Ignore empty lines
                                                   .ToList();

                if (!tablesInTemplate.Any())
                {
                    MessageBox.Show("Template file is empty.", "Load Template", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Use a HashSet for efficient lookup of available tables
                var availableTablesSet = new HashSet<string>(_allTableNames, StringComparer.OrdinalIgnoreCase);
                var tablesToSelect = new List<string>();
                var missingTables = new List<string>();

                foreach (string table in tablesInTemplate)
                {
                    if (availableTablesSet.Contains(table))
                    {
                        tablesToSelect.Add(table);
                    }
                    else
                    {
                        missingTables.Add(table);
                    }
                }

                // Apply selection
                TableListBox.SelectedItems.Clear(); // Clear existing selection first
                foreach (string table in tablesToSelect)
                {
                     // Check if the item is currently visible due to filtering
                     // We still select it, but the UI won't show if filtered out.
                     // This ensures the correct tables are processed even if filter is active.
                    TableListBox.SelectedItems.Add(table);
                }

                if (missingTables.Any())
                {
                     string missingList = string.Join("\n - ", missingTables);
                     MessageBox.Show($"The following tables listed in the template were not found in the ODBC source and were ignored:\n - {missingList}",
                                       "Template Load Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                 MessageBox.Show($"Error reading or applying template file: {templateFilePath}\nError: {ex.Message}", "Load Template Error", MessageBoxButton.OK, MessageBoxImage.Error);
                 // On error loading template, reset combobox
                 TemplateComboBox.SelectedIndex = 0;
                 _loadedTemplateIndex = -1;
            }
            finally
            {
                // UpdateSelectAllCheckBoxState is called by SelectionChanged handler, no need here if triggered by ListBox selection change
            }
        }

        private void SelectAllCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            // Handle only user-initiated check (IsChecked == true)
             if (!_isSelectAllChanging && SelectAllCheckBox.IsChecked == true)
             {
                 _isSelectAllChanging = true;
                 SelectAllVisibleItems();
                 _isSelectAllChanging = false;
             }
        }

        private void SelectAllCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
             // Handle only user-initiated uncheck (IsChecked == false)
             if (!_isSelectAllChanging && SelectAllCheckBox.IsChecked == false)
             {
                 _isSelectAllChanging = true;
                 DeselectAllVisibleItems();
                 _isSelectAllChanging = false;
             }
        }

        private void TableListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
             // Only reset template ComboBox if selection is changed by user interaction
             // and not during template application or select all/deselect all
             if (!_isApplyingTemplate && !_isSelectAllChanging)
             {
                 if (_loadedTemplateIndex != -1) // If a template was loaded
                 {
                     Debug.WriteLine("Manual selection change detected, resetting template ComboBox.");
                     TemplateComboBox.SelectedIndex = 0; // Reset to placeholder
                     _loadedTemplateIndex = -1;
                 }
             }

             if (!_isSelectAllChanging) // Prevent updates while SelectAll/DeselectAll is running
             {
                  UpdateSelectAllCheckBoxState();
             }
        }

        // Handles clicks directly on the ListBox area to toggle selection
        private void TableListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
             // Find the ListBoxItem that was clicked
             DependencyObject? source = e.OriginalSource as DependencyObject;
             while (source != null && !(source is ListBoxItem))
             {
                 source = VisualTreeHelper.GetParent(source);
             }

             if (source is ListBoxItem clickedItem)
             {
                 // Toggle selection state
                 clickedItem.IsSelected = !clickedItem.IsSelected;
                 // Prevent the default ListBox selection behavior which might interfere
                 e.Handled = true;
             }
        }

        // Helper methods
        private void UpdateSelectAllCheckBoxState()
        {
            // Add null check for _tableView
            if (_tableView == null) 
            {
                 SelectAllCheckBox.IsChecked = false;
                 SelectAllCheckBox.IsEnabled = false;
                 return;
            }

            _isSelectAllChanging = true; // Prevent triggering checked/unchecked handlers
            int visibleItemCount = _tableView.Cast<object>().Count();
            int selectedVisibleItemCount = TableListBox.SelectedItems.Cast<object>().Count(item => _tableView.Contains(item));

            if (visibleItemCount == 0)
            {
                 SelectAllCheckBox.IsChecked = false; // No items visible
                 SelectAllCheckBox.IsEnabled = false;
            }
            else
            {
                 SelectAllCheckBox.IsEnabled = true;
                 if (selectedVisibleItemCount == visibleItemCount)
                 {
                     SelectAllCheckBox.IsChecked = true; // All visible items are selected
                 }
                 else if (selectedVisibleItemCount == 0)
                 {
                     SelectAllCheckBox.IsChecked = false; // No visible items are selected
                 }
                 else
                 {
                     SelectAllCheckBox.IsChecked = null; // Some visible items are selected (indeterminate)
                 }
            }
             _isSelectAllChanging = false;
        }

        private void SelectAllVisibleItems()
        {
            // Add null check for _tableView
            if (_tableView == null) return;

            // Select only the items currently passing the filter
            foreach (var item in _tableView)
            {
                if (!TableListBox.SelectedItems.Contains(item))
                {
                     TableListBox.SelectedItems.Add(item);
                }
            }
        }

        private void DeselectAllVisibleItems()
        {
            // Add null check for _tableView
            if (_tableView == null) return;

             // Deselect only the items currently passing the filter
            // Need to create a temporary list as modifying the collection during iteration causes issues
            var itemsToDeselect = TableListBox.SelectedItems.Cast<object>().Where(item => _tableView.Contains(item)).ToList();
            foreach (var item in itemsToDeselect)
            {
                 TableListBox.SelectedItems.Remove(item);
            }
        }


        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Add null check for _allTableNames
            if (_allTableNames == null) 
            {
                 // Should not happen if constructor succeeded, but handle defensively
                 MessageBox.Show("Internal error: Table list is missing.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                 DialogResult = false;
                 Close();
                 return;
            }

            string currentLimitText = RowLimitTextBox.Text;
            Debug.WriteLine($"OkButton_Click: Current RowLimitTextBox text: '{currentLimitText}'"); // Added Debug

            // Get selection from the ListBox.SelectedItems which reflects user interaction
            var currentlySelectedItems = TableListBox.SelectedItems.Cast<string>().ToList();

            // Determine final selection based on whether ALL original tables are selected
            if (currentlySelectedItems.Count == _allTableNames.Count)
            {
                SelectedTables = null; // Treat selecting everything as 'null' (meaning all)
            }
            else if (currentlySelectedItems.Any())
            {
                SelectedTables = currentlySelectedItems;
            }
            else
            {
                // No tables selected - warn and maybe default to null (all)
                MessageBox.Show("No tables selected. All tables will be mirrored.", "Selection Info", MessageBoxButton.OK, MessageBoxImage.Information);
                SelectedTables = null;
            }

            if (int.TryParse(currentLimitText, out int limit) && limit >= 0) // Use variable
            {
                SelectedRowLimit = limit;
            }
            else
            {
                MessageBox.Show("Invalid row limit. Please enter a non-negative integer.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return; // Prevent closing
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SaveTemplateButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = TableListBox.SelectedItems.Cast<string>().OrderBy(s => s).ToList();
            if (!selectedItems.Any())
            {
                MessageBox.Show("Please select at least one table to save in a template.", "Save Template", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                InitialDirectory = _templatesDirectory,
                Filter = "Template Files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = ".txt",
                Title = "Save Table Selection Template"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllLines(saveFileDialog.FileName, selectedItems);
                    MessageBox.Show($"Template saved as {System.IO.Path.GetFileName(saveFileDialog.FileName)}", "Template Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadTemplates(); // Refresh the dropdown
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving template to file: {saveFileDialog.FileName}\nError: {ex.Message}", "Save Template Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
} 