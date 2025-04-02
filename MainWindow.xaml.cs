using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System; // Added for diagnostics
using System.Diagnostics; // Added for diagnostics
using System.Threading.Tasks;
using System.Data;
using System.Collections.Generic; // Added for List
using System.Windows.Threading;
using System.IO; // For File, Directory, Path
using Microsoft.Win32; // For SaveFileDialog
using System.Text.RegularExpressions;
using System.Linq;

namespace cs_ODBC_SQLite_Explorer;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    // Remove ConfigManager field
    // private ConfigManager? _configManager;
    private DataService? _dataService;
    // private string? _provideXConnectionString; // Store the DSN directly in DataService now
    private List<string>? _selectedTables = null; // Store selected tables
    private int _selectedRowLimit = 0; // Store selected row limit
    private const string OdbcDsn = "DSN=SOTAMAS90;"; // Define DSN constant
    private const int DefaultRowLimit = 1000; // Define default row limit

    // For Query History
    private List<string> _queryHistory = new List<string>();
    private int _queryHistoryIndex = -1;

    // For Saved Queries
    private string _savedQueriesDirectory = string.Empty;
    private class SavedQueryItem { public string Name { get; set; } = string.Empty; public string FilePath { get; set; } = string.Empty; }

    // For Query Chains
    private string _queryChainsDirectory = string.Empty;
    private class QueryChainItem { public string Name { get; set; } = string.Empty; public string FilePath { get; set; } = string.Empty; }

    // For Table Templates (Added)
    private string _savedTemplatesDirectory = string.Empty;

    // Class to track last query information
    private class LastQueryInfo
    {
        public DataTable? DataTable { get; set; }
        public bool IsSelectQuery { get; set; }
        public string? SelectQueryText { get; set; }
    }

    private LastQueryInfo _lastQueryInfo = new LastQueryInfo();
    private bool _isUserEditingCell = false;
    private bool _hasSelectedCells = false;

    // Fields to store selection state when context menu is open
    private List<DataGridCellInfo> _storedCellSelection = new List<DataGridCellInfo>();
    private List<object> _storedItemSelection = new List<object>();

    public MainWindow()
    {
        InitializeComponent();
        InitializeSavedQueriesDirectory();
        InitializeQueryChainsDirectory();
        InitializeTableTemplatesDirectory(); // Added
        UpdateButtonStates();
        LoadSavedQueries();
        LoadQueryChains();

        // Attach ContextMenu event handler programmatically to the TreeView
        ObjectExplorerTreeView.ContextMenuOpening += ObjectExplorerContextMenu_Opening;
        Debug.WriteLine("TreeView ContextMenuOpening event handler attached programmatically.");

        // Initialization moved to Window_Loaded
    }

    // Use Loaded event to trigger initial setup
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Disable UI elements initially
        RefreshButton.IsEnabled = false;
        ExecuteQueryButton.IsEnabled = false;
        QueryEditorTextBox.IsEnabled = false;
        ObjectExplorerTreeView.Items.Clear();
        ObjectExplorerTreeView.Items.Add(new TreeViewItem { Header = "Initializing..." });

        bool initialized = await InitializeAppAsync();

        if (initialized)
        {
             ObjectExplorerTreeView.Items.Clear();
             ObjectExplorerTreeView.Items.Add(new TreeViewItem { Header = "Ready. Please Refresh Data." });
             RefreshButton.IsEnabled = true; // Enable refresh button after successful init
             QueryEditorTextBox.IsEnabled = true; // Enable after successful init
             // Execute button remains disabled until refresh is done
        }
        else
        {
            ObjectExplorerTreeView.Items.Clear();
            ObjectExplorerTreeView.Items.Add(new TreeViewItem { Header = "Initialization Failed. Check ODBC/Login." });
            // Optionally close the application if init fails critically
            // Close();
        }
    }

    private async Task<bool> InitializeAppAsync()
    {
        try
        {
            // No ConfigManager needed
            Debug.WriteLine("Initializing DataService...");
            _dataService = new DataService(OdbcDsn);

            // Connect to ODBC (triggers login prompt)
            ObjectExplorerTreeView.Items.Clear();
            ObjectExplorerTreeView.Items.Add(new TreeViewItem { Header = "Connecting to ODBC..." });
            await _dataService.ConnectOdbcAsync(); // Single login prompt here
            Debug.WriteLine("ODBC Connection successful.");

            // Fetch table names using the opened connection
            List<string> allTableNames;
            Debug.WriteLine("Attempting to fetch ODBC table names...");
            try
            {
                 ObjectExplorerTreeView.Items.Clear();
                 ObjectExplorerTreeView.Items.Add(new TreeViewItem { Header = "Fetching table list..." });
                 // Call the instance method now
                 allTableNames = _dataService.GetOdbcTableNames();
            }
            catch (Exception tableEx)
            {
                Debug.WriteLine($"Exception caught while fetching ODBC table names: {tableEx}");
                MessageBox.Show($"Error fetching table list from ODBC source: {tableEx.Message}", "ODBC Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            Debug.WriteLine($"Successfully fetched {allTableNames.Count} table names.");

            // Show table selection window (use constant default limit)
            Debug.WriteLine("Creating TableSelectionWindow...");
            var selectionWindow = new TableSelectionWindow(allTableNames, DefaultRowLimit)
            {
                Owner = this
            };

            Debug.WriteLine("Calling ShowDialog() on TableSelectionWindow...");
            bool? dialogResult = selectionWindow.ShowDialog();
            Debug.WriteLine($"ShowDialog() returned: {dialogResult}");

            if (dialogResult == true)
            {
                _selectedTables = selectionWindow.SelectedTables;
                _selectedRowLimit = selectionWindow.SelectedRowLimit; // Store selected limit

                // DataService is already initialized
                Debug.WriteLine($"Initialization successful. Selected Tables: {(_selectedTables == null ? "All" : string.Join(", ", _selectedTables))}, Row Limit: {_selectedRowLimit}");

                // Trigger initial refresh automatically
                RefreshButton_Click(RefreshButton, new RoutedEventArgs());

                return true;
            }
            else
            {
                // User cancelled
                Debug.WriteLine("Table selection cancelled by user.");
                Close(); // Close main window if user cancels selection
                return false;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error during initialization: {ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Debug.WriteLine($"Initialization Exception: {ex}");
            return false;
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_dataService == null)
        {
             MessageBox.Show("Data service is not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
             return;
        }

        Debug.WriteLine("Refresh Button Clicked");
        RefreshButton.IsEnabled = false;
        ExecuteQueryButton.IsEnabled = false;
        ObjectExplorerTreeView.ItemsSource = null; // Clear binding if used
        ObjectExplorerTreeView.Items.Clear(); // Clear items directly
        ResultsDataGrid.ItemsSource = null;
        var loadingItem = new TreeViewItem { Header = "Mirroring data..." };
        ObjectExplorerTreeView.Items.Add(loadingItem);
        StatusTextBlock.Text = "Mirroring data..."; // Update status bar
        StatusProgressBar.Visibility = Visibility.Visible; // Show progress bar

        try
        {
            // Pass the stored selected row limit and tables
             // Run on background thread, update UI back on dispatcher
             await Task.Run(() => 
             {
                 // Ensure we're calling DataService.MirrorDataAsync directly through await
                 return _dataService.MirrorDataAsync(_selectedRowLimit, _selectedTables);
             });

             // UI updates must happen on the UI thread
             Dispatcher.Invoke(() => {
                Debug.WriteLine("Mirroring complete. Populating TreeView...");
                PopulateObjectExplorer();
                ExecuteQueryButton.IsEnabled = true;
                StatusTextBlock.Text = "Data refresh complete.";
                StatusProgressBar.Visibility = Visibility.Collapsed;
            });
        }
        catch (Exception ex)
        {
             // Ensure UI updates are on the correct thread
             Dispatcher.Invoke(() => {
                MessageBox.Show($"Error refreshing data: {ex.Message}", "Refresh Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Debug.WriteLine($"Refresh Exception: {ex}");
                ObjectExplorerTreeView.Items.Clear();
                var errorItem = new TreeViewItem { Header = "Error mirroring data" };
                ObjectExplorerTreeView.Items.Add(errorItem);
                ExecuteQueryButton.IsEnabled = false;
                StatusTextBlock.Text = $"Data refresh failed: {ex.Message}";
                StatusProgressBar.Visibility = Visibility.Collapsed;
            });
        }
        finally
        {
             // Ensure UI updates are on the correct thread
             Dispatcher.Invoke(() => {
                RefreshButton.IsEnabled = true;
                StatusProgressBar.Visibility = Visibility.Collapsed; // Ensure hidden even if error before UI update
            });
        }
    }

    private void PopulateObjectExplorer()
    {
        // Add null check for _dataService
        if (_dataService == null) return;

        try
        {
            var tableNames = _dataService.GetSQLiteTableNames();
            ObjectExplorerTreeView.Items.Clear();

            if (tableNames == null || tableNames.Count == 0)
            {
                var noTablesItem = new TreeViewItem { Header = "No tables mirrored" };
                ObjectExplorerTreeView.Items.Add(noTablesItem);
                return;
            }

            var tablesRootItem = new TreeViewItem { Header = "Tables" };
            foreach (var tableName in tableNames)
            {
                tablesRootItem.Items.Add(new TreeViewItem { Header = tableName });
            }
            ObjectExplorerTreeView.Items.Add(tablesRootItem);
            tablesRootItem.IsExpanded = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error populating object explorer: {ex.Message}", "UI Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Debug.WriteLine($"Populate TreeView Exception: {ex}");
            ObjectExplorerTreeView.Items.Clear();
            var errorItem = new TreeViewItem { Header = "Error loading tables" };
            ObjectExplorerTreeView.Items.Add(errorItem);
        }
    }

    private void ExecuteQueryButton_Click(object sender, RoutedEventArgs e)
    {
         // Add null check for _dataService
        if (_dataService == null)
        {
             MessageBox.Show("Data service is not initialized or data has not been refreshed.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
             return;
        }

        string query = QueryEditorTextBox.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            // Don't execute or add empty queries to history
            // MessageBox.Show("Please enter a query.", "No Query", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Add to history *before* execution
        AddToQueryHistory(query);

        Debug.WriteLine($"Execute Query Button Clicked. Query: {query}");
        ExecuteQueryButton.IsEnabled = false;
        ResultsDataGrid.ItemsSource = null;
        StatusTextBlock.Text = "Executing query...";
        StatusProgressBar.Visibility = Visibility.Visible;

        // Use Task.Run for potentially long queries, update UI back on dispatcher
        _ = Task.Run(() => _dataService.ExecuteSqlQuery(query))
            .ContinueWith(task =>
            {
                // This runs on the UI thread
                StatusProgressBar.Visibility = Visibility.Collapsed;
                if (task.IsFaulted)
                {
                    Exception? ex = task.Exception?.InnerException ?? task.Exception;
                    string errorMsg = ex?.Message ?? "Unknown error";
                    MessageBox.Show($"Error executing query: {errorMsg}", "Query Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Debug.WriteLine($"Query Execution Exception: {ex}");
                    ResultsDataGrid.ItemsSource = null;
                    StatusTextBlock.Text = $"Query failed: {errorMsg}";
                }
                else if (task.IsCanceled)
                {
                     StatusTextBlock.Text = "Query execution cancelled.";
                }
                else // Success
                {
                    DataTable results = task.Result;
                    BindDataTableWithRowNumbers(results);
                    Debug.WriteLine($"Query executed. Rows returned: {results.Rows.Count}");
                    StatusTextBlock.Text = $"Query executed successfully. Rows returned: {results.Rows.Count}";
                    
                    // Refresh the Object Explorer to show any new tables
                    PopulateObjectExplorer();
                }
                ExecuteQueryButton.IsEnabled = true; // Re-enable button
                 UpdateButtonStates(); // Update history buttons state
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void AddToQueryHistory(string query)
    {
        // Avoid adding consecutive duplicates
        if (_queryHistory.Count > 0 && _queryHistory[_queryHistory.Count - 1] == query)
        {
            // Reset index to the end if executing the last query again
             _queryHistoryIndex = _queryHistory.Count -1;
             UpdateButtonStates();
            return;
        }

        // If navigating history, new query overwrites future history
        if (_queryHistoryIndex < _queryHistory.Count - 1)
        {
             _queryHistory.RemoveRange(_queryHistoryIndex + 1, _queryHistory.Count - _queryHistoryIndex - 1);
        }

        _queryHistory.Add(query);
        _queryHistoryIndex = _queryHistory.Count - 1; // Point index to the new query
        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
         // Update history buttons
         PrevQueryButton.IsEnabled = _queryHistoryIndex > 0;
         NextQueryButton.IsEnabled = _queryHistoryIndex < _queryHistory.Count - 1;
         // Add logic for Save/Load buttons later
         SaveQueryButton.IsEnabled = !string.IsNullOrEmpty(QueryEditorTextBox.Text); // Enable save if there's text
         SavedQueriesComboBox.IsEnabled = !string.IsNullOrEmpty(_savedQueriesDirectory);
         SaveChainButton.IsEnabled = _queryHistory.Count > 0; // Enable if there's history
         LoadChainComboBox.IsEnabled = !string.IsNullOrEmpty(_queryChainsDirectory);
    }

    private void QueryEditorTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            ExecuteQueryButton_Click(ExecuteQueryButton, new RoutedEventArgs());
            e.Handled = true; // Prevent further handling of F5
        }
    }

    private void PrevQueryButton_Click(object sender, RoutedEventArgs e)
    {
         if (_queryHistoryIndex > 0)
         {
             _queryHistoryIndex--;
             QueryEditorTextBox.Text = _queryHistory[_queryHistoryIndex];
             UpdateButtonStates();
         }
    }

    private void NextQueryButton_Click(object sender, RoutedEventArgs e)
    {
         if (_queryHistoryIndex < _queryHistory.Count - 1)
         {
             _queryHistoryIndex++;
             QueryEditorTextBox.Text = _queryHistory[_queryHistoryIndex];
             UpdateButtonStates();
         }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        (_dataService as IDisposable)?.Dispose(); // Dispose if not null
        Debug.WriteLine("DataService disposed on window close.");
    }

    // --- Menu Item Handlers ---

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close(); // Close the main window
    }

    private async void AddTablesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_dataService == null)
        {
            MessageBox.Show("Data service is not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            StatusTextBlock.Text = "Getting table list...";
            StatusProgressBar.Visibility = Visibility.Visible;
            
            // Fetch table names
            List<string> allTableNames = _dataService.GetOdbcTableNames();
            
            // Create and show the table selection dialog with current row limit
            var selectionWindow = new TableSelectionWindow(allTableNames, _selectedRowLimit)
            {
                Owner = this
            };

            bool? dialogResult = selectionWindow.ShowDialog();
            
            if (dialogResult == true)
            {
                // Get the new selection
                List<string>? newSelection = selectionWindow.SelectedTables;
                int newRowLimit = selectionWindow.SelectedRowLimit;
                
                // Update the stored values
                _selectedTables = newSelection;
                _selectedRowLimit = newRowLimit;
                
                // Trigger refresh to mirror the selected tables
                RefreshButton_Click(RefreshButton, new RoutedEventArgs());
            }
            else
            {
                StatusTextBlock.Text = "Table refresh cancelled.";
                StatusProgressBar.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error getting tables: {ex.Message}", "ODBC Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusTextBlock.Text = "Failed to refresh tables.";
            StatusProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private void ManageQueriesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // Open the directory in Windows Explorer
        if (!string.IsNullOrEmpty(_savedQueriesDirectory) && Directory.Exists(_savedQueriesDirectory))
        {
             try
             {
                 // Use Process.Start with UseShellExecute = true
                 Process.Start(new ProcessStartInfo
                 {
                     FileName = _savedQueriesDirectory,
                     UseShellExecute = true,
                     Verb = "open"
                 });
             }
             catch (Exception ex)
             {
                 MessageBox.Show($"Could not open directory: {_savedQueriesDirectory}\nError: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
             }
        }
        else
        {
             MessageBox.Show("Saved queries directory not accessible.", "Directory Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ManageTableTemplatesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        OpenExplorerToDirectory(_savedTemplatesDirectory); // Use helper
    }

    private void ManageQueryChainsMenuItem_Click(object sender, RoutedEventArgs e)
    {
         OpenExplorerToDirectory(_queryChainsDirectory); // Use helper
    }

    // Helper to open directory
    private void OpenExplorerToDirectory(string directoryPath)
    {
         if (!string.IsNullOrEmpty(directoryPath) && Directory.Exists(directoryPath))
        {
             try
             {
                 Process.Start(new ProcessStartInfo
                 {
                     FileName = directoryPath,
                     UseShellExecute = true,
                     Verb = "open"
                 });
             }
             catch (Exception ex)
             {
                 MessageBox.Show($"Could not open directory: {directoryPath}\nError: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
             }
        }
        else
        {
             MessageBox.Show("Directory path is not configured or accessible.", "Directory Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // --- Context Menu Handlers ---

    private void ObjectExplorerContextMenu_Opening(object sender, ContextMenuEventArgs e)
    {
        // Enable/disable menu items based on what is selected in the TreeView
        bool isTableSelected = false;
        if (ObjectExplorerTreeView.SelectedItem is TreeViewItem selectedItem)
        {
            // Check if the selected item is a table (i.e., a child of the root "Tables" node)
            // This logic might need adjustment based on how PopulateObjectExplorer structures the TreeView
             if (selectedItem.DataContext is string || (selectedItem.Header is string && selectedItem.Parent is TreeViewItem parent && parent.Header as string == "Tables"))
             {
                 isTableSelected = true;
             }
        }

        // Find the menu items (assuming the ContextMenu is directly on the TreeView)
        if (sender is TreeView treeView && treeView.ContextMenu != null)
        {
             foreach (var item in treeView.ContextMenu.Items)
             {
                 if (item is MenuItem menuItem)
                 {
                     menuItem.IsEnabled = isTableSelected;
                 }
             }
        }
    }

     private void SelectTop100_Click(object sender, RoutedEventArgs e)
    {
        GenerateAndExecuteSelectQuery(100);
    }

    private void SelectTop1000_Click(object sender, RoutedEventArgs e)
    {
        GenerateAndExecuteSelectQuery(1000);
    }

    private void SelectAllRows_Click(object sender, RoutedEventArgs e)
    {
        GenerateAndExecuteSelectQuery(0); // 0 means no limit
    }

    private void GenerateAndExecuteSelectQuery(int topN)
    {
         if (ObjectExplorerTreeView.SelectedItem is TreeViewItem selectedItem)
        {
            // Get table name - adjust if DataContext is used differently
            string? tableName = selectedItem.Header as string;
            if (!string.IsNullOrEmpty(tableName))
            {
                // Correctly escape closing brackets ']' by doubling them ']]'
                string escapedTableName = tableName.Replace("]", "]]");
                string query = topN > 0
                    ? $"SELECT * FROM [{escapedTableName}] LIMIT {topN};" // Corrected format
                    : $"SELECT * FROM [{escapedTableName}];"; // Corrected format

                QueryEditorTextBox.Text = query;
                ExecuteQueryButton_Click(ExecuteQueryButton, new RoutedEventArgs()); // Simulate button click
            }
        }
    }

    private void InitializeSavedQueriesDirectory()
    {
        try
        {
            string myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _savedQueriesDirectory = System.IO.Path.Combine(myDocuments, "OdbcExplorerQueries");
            if (!Directory.Exists(_savedQueriesDirectory))
            {
                Directory.CreateDirectory(_savedQueriesDirectory);
                Debug.WriteLine($"Created saved queries directory: {_savedQueriesDirectory}");
            }
            else
            {
                Debug.WriteLine($"Using saved queries directory: {_savedQueriesDirectory}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error initializing saved queries directory: {ex}");
            MessageBox.Show($"Could not create or access the saved queries directory: {_savedQueriesDirectory}\nError: {ex.Message}", "Directory Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            // Disable save/load functionality if directory fails?
            SaveQueryButton.IsEnabled = false;
            SavedQueriesComboBox.IsEnabled = false;
            // Also disable chain controls
            SaveChainButton.IsEnabled = false;
            LoadChainComboBox.IsEnabled = false;
        }
    }

    private void LoadSavedQueries()
    {
        SavedQueriesComboBox.SelectionChanged -= SavedQueriesComboBox_SelectionChanged; // Prevent event firing during reload
        SavedQueriesComboBox.Items.Clear();
        SavedQueriesComboBox.Items.Add(new SavedQueryItem { Name = "<Load Saved Query>" }); // Placeholder
        SavedQueriesComboBox.SelectedIndex = 0;

        if (string.IsNullOrEmpty(_savedQueriesDirectory) || !Directory.Exists(_savedQueriesDirectory))
            return;

        try
        {
            var queryFiles = Directory.EnumerateFiles(_savedQueriesDirectory, "*.sql");
            foreach (var filePath in queryFiles.OrderBy(f => f))
            {
                SavedQueriesComboBox.Items.Add(new SavedQueryItem
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(filePath),
                    FilePath = filePath
                });
            }
        }
        catch (Exception ex)
        {
             Debug.WriteLine($"Error loading saved queries: {ex}");
             MessageBox.Show($"Could not load saved queries from directory: {_savedQueriesDirectory}\nError: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
             SavedQueriesComboBox.SelectionChanged += SavedQueriesComboBox_SelectionChanged; // Re-attach event handler
        }
    }

    private void InitializeQueryChainsDirectory()
    {
        try
        {
            string myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _queryChainsDirectory = System.IO.Path.Combine(myDocuments, "OdbcExplorerQueryChains");
            if (!Directory.Exists(_queryChainsDirectory))
            {
                Directory.CreateDirectory(_queryChainsDirectory);
                Debug.WriteLine($"Created query chains directory: {_queryChainsDirectory}");
            }
            else
            {
                Debug.WriteLine($"Using query chains directory: {_queryChainsDirectory}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error initializing query chains directory: {ex}");
            MessageBox.Show($"Could not create or access the query chains directory: {_queryChainsDirectory}\nError: {ex.Message}", "Directory Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            SaveChainButton.IsEnabled = false;
            LoadChainComboBox.IsEnabled = false;
        }
    }

    private void LoadQueryChains()
    {
        LoadChainComboBox.SelectionChanged -= LoadChainComboBox_SelectionChanged; // Prevent event firing
        LoadChainComboBox.Items.Clear();
        LoadChainComboBox.Items.Add(new QueryChainItem { Name = "<Load Chain>" }); // Placeholder
        LoadChainComboBox.SelectedIndex = 0;

        if (string.IsNullOrEmpty(_queryChainsDirectory) || !Directory.Exists(_queryChainsDirectory))
            return;

        try
        {
            // Use a simple extension like .chain.txt or just .txt
            var chainFiles = Directory.EnumerateFiles(_queryChainsDirectory, "*.chain.txt");
            foreach (var filePath in chainFiles.OrderBy(f => f))
            {
                LoadChainComboBox.Items.Add(new QueryChainItem
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(filePath).Replace(".chain",""), // Clean up name
                    FilePath = filePath
                });
            }
        }
        catch (Exception ex)
        {
             Debug.WriteLine($"Error loading query chains: {ex}");
             MessageBox.Show($"Could not load query chains from directory: {_queryChainsDirectory}\nError: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
             LoadChainComboBox.SelectionChanged += LoadChainComboBox_SelectionChanged; // Re-attach
        }
    }

    // --- Save/Load Query Handlers ---

    private void SaveQueryButton_Click(object sender, RoutedEventArgs e)
    {
        string queryToSave = QueryEditorTextBox.Text;
        if (string.IsNullOrWhiteSpace(queryToSave))
        {
            MessageBox.Show("There is no query to save.", "Save Query", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveFileDialog saveFileDialog = new SaveFileDialog
        {
            InitialDirectory = _savedQueriesDirectory,
            Filter = "SQL Query Files (*.sql)|*.sql|All files (*.*)|*.*",
            DefaultExt = ".sql",
            Title = "Save SQL Query"
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            try
            {
                File.WriteAllText(saveFileDialog.FileName, queryToSave);
                StatusTextBlock.Text = $"Query saved to {System.IO.Path.GetFileName(saveFileDialog.FileName)}";
                LoadSavedQueries(); // Refresh the dropdown
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving query to file: {saveFileDialog.FileName}\nError: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = "Failed to save query.";
            }
        }
    }

    private void SavedQueriesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SavedQueriesComboBox.SelectedIndex > 0 && // Index 0 is the placeholder
            SavedQueriesComboBox.SelectedItem is SavedQueryItem selectedQuery)
        {
            try
            {
                string queryContent = File.ReadAllText(selectedQuery.FilePath);
                QueryEditorTextBox.Text = queryContent;
                StatusTextBlock.Text = $"Loaded query: {selectedQuery.Name}";
                // Reset selection back to placeholder after loading
                SavedQueriesComboBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                 MessageBox.Show($"Error loading query from file: {selectedQuery.FilePath}\nError: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                 StatusTextBlock.Text = "Failed to load query.";
                 // Reset selection back to placeholder on error
                 SavedQueriesComboBox.SelectedIndex = 0;
            }
        }
    }

    // --- Query Chain Handlers ---

    private void SaveChainButton_Click(object sender, RoutedEventArgs e)
    {
        if (_queryHistory.Count == 0)
        {
             MessageBox.Show("No query history to save.", "Save Chain", MessageBoxButton.OK, MessageBoxImage.Information);
             return;
        }

        // Prompt for number of queries
        var inputDialog = new InputDialog($"How many recent queries to save (max {_queryHistory.Count})?", Math.Min(5, _queryHistory.Count).ToString()) { Owner = this };
        if (inputDialog.ShowDialog() != true)
        {
            return; // User cancelled
        }

        if (!int.TryParse(inputDialog.InputText, out int count) || count <= 0 || count > _queryHistory.Count)
        {
            MessageBox.Show($"Invalid number. Please enter a number between 1 and {_queryHistory.Count}.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Get the actual queries
        var queriesToSave = _queryHistory.TakeLast(count).ToList();
        // Join queries with a specific delimiter
        string fileContent = string.Join("\n--;;--;;--\n", queriesToSave);

        // Prompt for filename
        SaveFileDialog saveFileDialog = new SaveFileDialog
        {
            InitialDirectory = _queryChainsDirectory,
            Filter = "Query Chain Files (*.chain.txt)|*.chain.txt|All files (*.*)|*.*",
            DefaultExt = ".chain.txt",
            Title = "Save Query Chain"
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            try
            {
                // Write the combined string with delimiters
                File.WriteAllText(saveFileDialog.FileName, fileContent);
                StatusTextBlock.Text = $"Query chain saved to {System.IO.Path.GetFileName(saveFileDialog.FileName)}";
                LoadQueryChains(); // Refresh the dropdown
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving query chain to file: {saveFileDialog.FileName}\nError: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = "Failed to save query chain.";
            }
        }
    }

    private async void LoadChainComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LoadChainComboBox.SelectedIndex > 0 && // Index 0 is placeholder
            LoadChainComboBox.SelectedItem is QueryChainItem selectedChain)
        {
            string chainName = selectedChain.Name;
            string chainPath = selectedChain.FilePath;
            // Reset selection early to allow re-selection if execution fails
            LoadChainComboBox.SelectedIndex = 0;

            if (_dataService == null)
            {
                MessageBox.Show("Data service not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Read the entire file and split by the delimiter
                string fileContent = File.ReadAllText(chainPath);
                string[] delimiter = { "--;;--;;--" };
                List<string> queries = fileContent.Split(delimiter, StringSplitOptions.RemoveEmptyEntries)
                                                .Select(q => q.Trim()) // Trim whitespace from each query
                                                .Where(q => !string.IsNullOrWhiteSpace(q))
                                                .ToList();

                if (!queries.Any())
                {
                     MessageBox.Show($"Query chain file '{chainName}' is empty.", "Load Chain", MessageBoxButton.OK, MessageBoxImage.Information);
                     return;
                }

                await ExecuteQueryChainAsync(queries, chainName);
            }
            catch (Exception ex)
            {
                 MessageBox.Show($"Error reading query chain file: {chainPath}\nError: {ex.Message}", "Load Chain Error", MessageBoxButton.OK, MessageBoxImage.Error);
                 StatusTextBlock.Text = "Failed to load query chain.";
            }
        }
    }

    private async Task ExecuteQueryChainAsync(List<string> queries, string chainName)
    {
        if (_dataService == null) return;

        Debug.WriteLine($"Executing query chain: {chainName}");
        ExecuteQueryButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        SaveChainButton.IsEnabled = false;
        LoadChainComboBox.IsEnabled = false;
        StatusProgressBar.Visibility = Visibility.Visible;

        DataTable? lastResult = null;
        bool success = true;

        for (int i = 0; i < queries.Count; i++)
        {
            string query = queries[i];
            StatusTextBlock.Text = $"Executing chain '{chainName}' - Step {i + 1}/{queries.Count}: {query.Substring(0, Math.Min(query.Length, 50))}...";
            QueryEditorTextBox.Text = query; // Show current query
            AddToQueryHistory(query); // Add to history as it executes

            try
            {
                lastResult = await Task.Run(() => _dataService.ExecuteSqlQuery(query));
                // Optionally add a small delay if needed between steps?
                // await Task.Delay(100);
            }
            catch (Exception ex)
            {
                success = false;
                string errorMsg = ex.Message;
                MessageBox.Show($"Error executing query #{i + 1} in chain '{chainName}':\n{query}\n\nError: {errorMsg}",
                                  "Query Chain Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Debug.WriteLine($"Query Chain Execution Exception: {ex}");
                StatusTextBlock.Text = $"Query chain '{chainName}' failed at step {i + 1}.";
                ResultsDataGrid.ItemsSource = null; // Clear results on failure
                break; // Stop the chain on error
            }
        }

        // Update UI after chain completion or failure
        StatusProgressBar.Visibility = Visibility.Collapsed;
        ExecuteQueryButton.IsEnabled = true;
        RefreshButton.IsEnabled = true;
        SaveChainButton.IsEnabled = true;
        LoadChainComboBox.IsEnabled = true;
        UpdateButtonStates(); // Update history buttons

        if (success)
        {
             StatusTextBlock.Text = $"Query chain '{chainName}' executed successfully.";
             if (lastResult != null)
             {
                  BindDataTableWithRowNumbers(lastResult);
                  Debug.WriteLine($"Chain finished. Last query returned {lastResult.Rows.Count} rows.");
                  
                  // Refresh the Object Explorer to show any new tables
                  PopulateObjectExplorer();
             }
             else
             {
                  ResultsDataGrid.ItemsSource = null;
                  Debug.WriteLine("Chain finished. Last query returned no results table.");
             }
        }
        // else: Error message already shown, status text already set
    }

    private void InitializeTableTemplatesDirectory()
    {
         try
         {
             string myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
             _savedTemplatesDirectory = System.IO.Path.Combine(myDocuments, "OdbcExplorerTableTemplates");
             if (!Directory.Exists(_savedTemplatesDirectory))
             {
                 Directory.CreateDirectory(_savedTemplatesDirectory);
                 Debug.WriteLine($"Created table templates directory: {_savedTemplatesDirectory}");
             }
             else
             {
                 Debug.WriteLine($"Using table templates directory: {_savedTemplatesDirectory}");
             }
         }
         catch (Exception ex)
         {
             Debug.WriteLine($"Error initializing table templates directory: {ex}");
             MessageBox.Show($"Could not create or access the table templates directory: {_savedTemplatesDirectory}\nError: {ex.Message}", "Directory Error", MessageBoxButton.OK, MessageBoxImage.Warning);
             // Consider disabling related menu item if directory fails
         }
    }

    // Modify MouseTripleClick event handler to detect triple clicks
    private void QueryEditorTextBox_MouseTripleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            // Check if it's a triple click (clickCount is internal, so we have to use a workaround)
            if (e.ClickCount == 3)
            {
                textBox.SelectAll();
                e.Handled = true;
            }
        }
    }

    // Add this new method for binding with row numbers
    private void BindDataTableWithRowNumbers(DataTable dataTable)
    {
        ResultsDataGrid.ItemsSource = dataTable.DefaultView;
        
        // Store the query info for use in editing
        _lastQueryInfo = new LastQueryInfo
        {
            DataTable = dataTable,
            IsSelectQuery = IsSelectQuery(QueryEditorTextBox.Text),
            SelectQueryText = IsSelectQuery(QueryEditorTextBox.Text) ? QueryEditorTextBox.Text : _lastQueryInfo.SelectQueryText
        };
    }

    private void ResultsDataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        e.Row.Header = (e.Row.GetIndex() + 1).ToString();
    }

    private bool IsSelectQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;
        
        string trimmedQuery = query.Trim().ToUpperInvariant();
        return trimmedQuery.StartsWith("SELECT ");
    }

    private void ResultsDataGrid_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_lastQueryInfo.IsSelectQuery)
        {
            // Let the default behavior proceed (enables editing)
            _isUserEditingCell = true;
            ResultsDataGrid.IsReadOnly = false;
        }
        else
        {
            // Disable editing for non-SELECT queries
            ResultsDataGrid.IsReadOnly = true;
        }
    }

    private void ResultsDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (!_isUserEditingCell || !_lastQueryInfo.IsSelectQuery || _lastQueryInfo.DataTable == null)
            return;

        try
        {
            // Get the current values
            DataRowView rowView = (DataRowView)e.Row.Item;
            string? tableName = _lastQueryInfo.DataTable.TableName;
            
            // If no table name is set, try to extract it from the current query
            if (string.IsNullOrEmpty(tableName))
            {
                tableName = ExtractTableNameFromQuery(_lastQueryInfo.SelectQueryText ?? QueryEditorTextBox.Text);
                if (string.IsNullOrEmpty(tableName))
                {
                    MessageBox.Show("Cannot determine table name for UPDATE operation.", "Edit Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            
            // Get the column name and new value
            DataGridColumn column = e.Column;
            string columnName = column.Header.ToString();
            
            // Get the edited value from the TextBox (or other editor)
            FrameworkElement editingElement = e.EditingElement;
            string newValue = "";
            
            if (editingElement is TextBox textBox)
            {
                newValue = textBox.Text;
            }
            else
            {
                // Handle other editor types if needed
                Debug.WriteLine($"Unhandled editing element type: {editingElement.GetType().Name}");
                return;
            }
            
            // Get primary key or unique identifier for the row
            string whereClause = BuildWhereClauseForRow(rowView, _lastQueryInfo.DataTable);
            if (string.IsNullOrEmpty(whereClause))
            {
                MessageBox.Show("Cannot create a unique WHERE clause for this row. Update operation aborted.", 
                    "Edit Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            // Create the update query
            string updateQuery = $"UPDATE [{tableName}] SET [{columnName}] = '{EscapeSingleQuotes(newValue)}' WHERE {whereClause};";
            
            // Store the original SELECT query to re-run after update
            string selectQuery = _lastQueryInfo.SelectQueryText ?? "";
            
            // Show the query in the query editor
            QueryEditorTextBox.Text = updateQuery;
            
            // Execute the update query
            _ = Task.Run(() => _dataService.ExecuteSqlQuery(updateQuery))
                .ContinueWith(task =>
                {
                    // Back on UI thread
                    if (task.IsFaulted)
                    {
                        Exception? ex = task.Exception?.InnerException ?? task.Exception;
                        string errorMsg = ex?.Message ?? "Unknown error";
                        MessageBox.Show($"Error executing update: {errorMsg}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        Debug.WriteLine($"Update Query Exception: {ex}");
                        StatusTextBlock.Text = $"Update failed: {errorMsg}";
                    }
                    else
                    {
                        StatusTextBlock.Text = "Row updated successfully.";
                        AddToQueryHistory(updateQuery);
                        
                        // Re-run the original SELECT query to refresh the data
                        if (!string.IsNullOrEmpty(selectQuery))
                        {
                            QueryEditorTextBox.Text = selectQuery;
                            
                            // Run the SELECT query
                            _ = Task.Run(() => _dataService.ExecuteSqlQuery(selectQuery))
                                .ContinueWith(selectTask =>
                                {
                                    if (selectTask.IsFaulted)
                                    {
                                        Exception? ex = selectTask.Exception?.InnerException ?? selectTask.Exception;
                                        string errorMsg = ex?.Message ?? "Unknown error";
                                        StatusTextBlock.Text = $"Failed to refresh data: {errorMsg}";
                                        Debug.WriteLine($"SELECT refresh query failed: {ex}");
                                    }
                                    else
                                    {
                                        DataTable results = selectTask.Result;
                                        BindDataTableWithRowNumbers(results);
                                        AddToQueryHistory(selectQuery);
                                        StatusTextBlock.Text = "Data refreshed after update.";
                                    }
                                }, TaskScheduler.FromCurrentSynchronizationContext());
                        }
                    }
                    
                    // Reset editing state
                    _isUserEditingCell = false;
                    ResultsDataGrid.IsReadOnly = true;
                    
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error during update operation: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Debug.WriteLine($"Cell edit exception: {ex}");
            
            // Reset editing state
            _isUserEditingCell = false;
            ResultsDataGrid.IsReadOnly = true;
        }
    }
    
    private string EscapeSingleQuotes(string input)
    {
        return input.Replace("'", "''");
    }
    
    private string ExtractTableNameFromQuery(string query)
    {
        try
        {
            // Basic extraction of table name from a SELECT query
            // This is a simple implementation and might not work for complex queries
            string pattern = @"FROM\s+\[?(\w+)\]?";
            Match match = Regex.Match(query, pattern, RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1)
            {
                return match.Groups[1].Value;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error extracting table name: {ex}");
        }
        return string.Empty;
    }
    
    private string BuildWhereClauseForRow(DataRowView rowView, DataTable dataTable)
    {
        // Try to use all column values to uniquely identify the row
        var conditions = new List<string>();
        
        foreach (DataColumn column in dataTable.Columns)
        {
            object value = rowView[column.ColumnName];
            
            if (value == null || value == DBNull.Value)
            {
                conditions.Add($"[{column.ColumnName}] IS NULL");
            }
            else if (value is string)
            {
                conditions.Add($"[{column.ColumnName}] = '{EscapeSingleQuotes(value.ToString())}'");
            }
            else if (value is DateTime)
            {
                conditions.Add($"[{column.ColumnName}] = '{((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss")}'");
            }
            else
            {
                conditions.Add($"[{column.ColumnName}] = {value}");
            }
        }
        
        return string.Join(" AND ", conditions);
    }
    
    private void ResultsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _hasSelectedCells = ResultsDataGrid.SelectedCells.Count > 0 || ResultsDataGrid.SelectedItems.Count > 0;
        
        // Enable/disable "Copy With Headers" based on selection
        if (CopyWithHeadersMenuItem != null)
        {
            CopyWithHeadersMenuItem.IsEnabled = _hasSelectedCells;
        }
    }
    
    private void SaveResultsAs_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsDataGrid.ItemsSource == null)
        {
            MessageBox.Show("No results to save.", "Save Results", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        // Show delimiter selection dialog
        var delimiterWindow = new DelimiterSelectionWindow
        {
            Owner = this
        };
        
        if (delimiterWindow.ShowDialog() != true)
            return;
            
        string delimiter = delimiterWindow.SelectedDelimiter;
        
        // Show save file dialog
        var saveDialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = ".csv",
            FileName = "QueryResults.csv"
        };
        
        if (saveDialog.ShowDialog() != true)
            return;
            
        try
        {
            StatusTextBlock.Text = "Saving results...";
            StatusProgressBar.Visibility = Visibility.Visible;
            
            // Get the data from the DataGrid
            DataView dataView = (DataView)ResultsDataGrid.ItemsSource;
            DataTable dataTable = dataView.Table;
            
            // Run the save operation in the background
            _ = Task.Run(() => 
            {
                using (StreamWriter writer = new StreamWriter(saveDialog.FileName, false, Encoding.UTF8))
                {
                    // Write header row
                    var headers = new List<string>();
                    foreach (DataColumn column in dataTable.Columns)
                    {
                        headers.Add(column.ColumnName);
                    }
                    writer.WriteLine(string.Join(delimiter, headers));
                    
                    // Write data rows
                    foreach (DataRowView rowView in dataView)
                    {
                        var values = new List<string>();
                        foreach (DataColumn column in dataTable.Columns)
                        {
                            object value = rowView[column.ColumnName];
                            values.Add(value == DBNull.Value ? "" : value.ToString());
                        }
                        writer.WriteLine(string.Join(delimiter, values));
                    }
                }
                
                return true;
            }).ContinueWith(task => 
            {
                // Back on UI thread
                StatusProgressBar.Visibility = Visibility.Collapsed;
                
                if (task.IsFaulted)
                {
                    Exception? ex = task.Exception?.InnerException ?? task.Exception;
                    string errorMsg = ex?.Message ?? "Unknown error";
                    MessageBox.Show($"Error saving results: {errorMsg}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusTextBlock.Text = "Error saving results.";
                }
                else
                {
                    StatusTextBlock.Text = $"Results saved to {saveDialog.FileName}";
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        catch (Exception ex)
        {
            StatusProgressBar.Visibility = Visibility.Collapsed;
            MessageBox.Show($"Error saving results: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusTextBlock.Text = "Error saving results.";
        }
    }
    
    private void ResultsDataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Handle Enter key to commit edit and prevent crash
        if (e.Key == Key.Enter && _isUserEditingCell)
        {
            // Complete the editing operation
            var currentCell = ResultsDataGrid.CurrentCell;
            if (currentCell.Column != null)
            {
                // Commit the edit
                ResultsDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                e.Handled = true;
            }
        }
    }

    private void CopyWithHeaders_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsDataGrid.ItemsSource == null)
            return;
            
        try
        {
            // Preserve selection
            var selectedCells = new List<DataGridCellInfo>(ResultsDataGrid.SelectedCells);
            var selectedItems = ResultsDataGrid.SelectedItems.Cast<object>().ToList();
            
            // Get the data from the DataGrid
            DataView dataView = (DataView)ResultsDataGrid.ItemsSource;
            DataTable dataTable = dataView.Table;
            
            // Build the text to copy
            var stringBuilder = new StringBuilder();
            
            // If user has selected cells (not just rows)
            if (selectedCells.Count > 0)
            {
                // Group by row
                var rowGroups = selectedCells.GroupBy(cell => ResultsDataGrid.Items.IndexOf(cell.Item))
                                            .OrderBy(g => g.Key)
                                            .ToList();
                
                // Get only the selected columns in the order they appear
                var columns = selectedCells.Select(cell => cell.Column)
                                          .Distinct()
                                          .OrderBy(c => c.DisplayIndex)
                                          .ToList();
                
                // If we have columns and rows
                if (columns.Count > 0 && rowGroups.Count > 0)
                {
                    // Write headers for selected columns
                    var headers = columns.Select(c => c.Header.ToString()).ToList();
                    stringBuilder.AppendLine(string.Join("\t", headers));
                    
                    // Write each row's selected cells
                    foreach (var rowGroup in rowGroups)
                    {
                        var rowValues = new string[columns.Count];
                        for (int i = 0; i < columns.Count; i++)
                            rowValues[i] = "";
                            
                        foreach (var cell in rowGroup)
                        {
                            int columnIndex = columns.IndexOf(cell.Column);
                            if (columnIndex >= 0)
                            {
                                var content = cell.Item is DataRowView rowView ? 
                                    rowView[cell.Column.Header.ToString()] : null;
                                    
                                rowValues[columnIndex] = content == DBNull.Value ? "" : 
                                    content?.ToString() ?? "";
                            }
                        }
                        
                        stringBuilder.AppendLine(string.Join("\t", rowValues));
                    }
                }
            }
            // If entire rows are selected (but no individual cells)
            else if (selectedItems.Count > 0)
            {
                // Get all columns
                var columns = new List<DataGridColumn>();
                foreach (var column in ResultsDataGrid.Columns)
                {
                    columns.Add(column);
                }
                
                // Write headers
                var headers = columns.Select(c => c.Header.ToString()).ToList();
                stringBuilder.AppendLine(string.Join("\t", headers));
                
                // Write rows
                foreach (DataRowView rowView in selectedItems)
                {
                    var values = new List<string>();
                    foreach (DataColumn column in dataTable.Columns)
                    {
                        object value = rowView[column.ColumnName];
                        values.Add(value == DBNull.Value ? "" : value.ToString());
                    }
                    stringBuilder.AppendLine(string.Join("\t", values));
                }
            }
            
            // Copy to clipboard
            if (stringBuilder.Length > 0)
            {
                System.Windows.Clipboard.SetText(stringBuilder.ToString());
                StatusTextBlock.Text = "Selection copied to clipboard with headers.";
            }
            else
            {
                StatusTextBlock.Text = "No cells selected to copy.";
            }
            
            // Restore selection
            if (selectedCells.Any())
            {
                ResultsDataGrid.SelectedCells.Clear();
                foreach (var cell in selectedCells)
                {
                    ResultsDataGrid.SelectedCells.Add(cell);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error copying selection: {ex.Message}", "Copy Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusTextBlock.Text = "Error copying selection.";
        }
    }

    private void ResultsContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        // Store the current selection
        _storedCellSelection = new List<DataGridCellInfo>(ResultsDataGrid.SelectedCells);
        _storedItemSelection = ResultsDataGrid.SelectedItems.Cast<object>().ToList();
    }

    private void ResultsContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        // Restore the selection after a short delay to avoid interference with menu item clicks
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            // Only restore if there was a selection and it's now gone
            if (_storedCellSelection.Count > 0 && ResultsDataGrid.SelectedCells.Count == 0)
            {
                ResultsDataGrid.SelectedCells.Clear();
                foreach (var cell in _storedCellSelection)
                {
                    ResultsDataGrid.SelectedCells.Add(cell);
                }
            }
        }));
    }
}