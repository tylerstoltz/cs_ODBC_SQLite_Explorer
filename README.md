# ODBC Explorer

## Overview
ODBC Explorer is a Windows desktop application built in C# WPF that enables you to connect to ODBC data sources, particularly ProvideX databases, and work with the data through an intuitive, SQL Server Management Studio-like interface. The application mirrors selected tables from the ODBC data source into an in-memory SQLite database, allowing you to execute SQLite queries against the mirrored data.

## Purpose and Goals
- Provide a simple, lightweight alternative to more complex database management tools
- Allow easy exploration and querying of ODBC data sources, especially ProvideX databases
- Eliminate the need for direct writes to production databases by using an in-memory mirror
- Support efficient workflow with features like saved queries, query history, and query chains

## Current Features
- **ODBC Connectivity**: Connect to any ODBC data source with proper DSN configuration
- **Table Selection**: Choose which tables to mirror from the source database
- **Data Mirroring**: Mirror selected tables into an in-memory SQLite database
- **Query Editor**: Write and execute SQLite queries with syntax highlighting
- **Query History**: Navigate through query history with back/forward buttons
- **Saved Queries**: Save and load frequently used queries
- **Query Chains**: Create and execute sequences of queries
- **Table Templates**: Save and load table selection templates
- **Object Explorer**: Browse mirrored tables in a tree view
- **Context Menu**: Right-click tables to generate SELECT queries
- **Row Numbering**: Display row numbers in query results
- **Data Editing**: Double-click fields in results grid to edit values and automatically generate UPDATE queries
- **Results Export**: Save query results as CSV with custom delimiter options
- **Copy with Headers**: Copy selected cells or rows from the results grid with column headers

## How to Use

### Initial Setup
1. Ensure you have a properly configured ODBC DSN for your data source
2. Launch the application
3. When prompted, select the tables you want to mirror and set a row limit (use 0 for all rows)
4. Click "OK" to begin the mirroring process

### Basic Usage
- **Object Explorer**: Navigate through mirrored tables in the left panel
- **Query Editor**: Write SQLite queries in the central text area
- **Execute Query**: Click the "Execute Query" button or press F5
- **Results Grid**: View query results in the bottom grid
- **Status Bar**: Check operation status and row counts at the bottom of the window

### Advanced Features
- **Table Selection Templates**:
  - Save current table selections for future use
  - Load saved templates from the dropdown menu
  - Manage templates from Database → Manage Table Templates

- **Saved Queries**:
  - Save queries using the "Save Query" button
  - Load saved queries from the dropdown menu
  - Manage saved queries from Query → Manage Saved Queries

- **Query Chains**:
  - Save sequences of queries using the "Save Chain" button
  - Load and execute query chains from the dropdown menu
  - Manage query chains from Query → Manage Query Chains

- **Context Menu Actions**:
  - Right-click a table to generate SELECT queries with different row limits
  - Select "Top 100", "Top 1000", or "All Rows" options

- **Data Editing**:
  - Double-click a field in the results grid to edit its value
  - Press Enter or click elsewhere to apply the change
  - The generated UPDATE query will appear in the query editor and execute automatically

- **Results Export and Copying**:
  - Right-click in the results grid to save all results as CSV (with custom delimiter)
  - Select cells or rows and right-click to copy the selection with column headers

## Planned Features

### Multiple Data Source Connections
- **Multiple ODBC Connections**: Connect to multiple ODBC data sources simultaneously
- **Cross-Database Queries**: Execute queries that join data across different sources
- **Additional Source Types**:
  - Excel spreadsheets via ODBC drivers
  - SQLite databases through SQLite ODBC DSN
  - Direct connection to SQLite .db files
  - CSV files through ODBC or direct import
- **Connection Manager**: UI for managing multiple connections
- **Source Grouping**: Organize connections by project or type
- **Cross-Source Object Explorer**: Navigate all connected sources in a unified tree view

## Fixes Needed
- **Refresh Data**
    - Clicking "Refresh Data" button refreshes previously loaded tables, but if user added tables are present something gets broken.

## Building and Installation

### Prerequisites
- .NET 9.0 SDK or later
- Windows 10/11
- Configured ODBC DSN for your data source

### Building from Source
1. Clone the repository
2. Open a command prompt in the project directory
3. Run the following commands:
   ```
   dotnet restore
   dotnet build
   ```
4. To run the application:
   ```
   dotnet run
   ```

### Creating a Standalone Executable
To create a standalone executable:
```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Distribution

To distribute the application, include the following files from the `bin\Debug\net9.0-windows` directory:
- cs_ODBC_SQLite_Explorer.exe (main executable)
- cs_ODBC_SQLite_Explorer.dll
- cs_ODBC_SQLite_Explorer.deps.json
- cs_ODBC_SQLite_Explorer.runtimeconfig.json
- System.Data.Odbc.dll
- System.Data.SQLite.dll
- SQLite.Interop.dll (x86 and x64 versions)

These files can be placed in any directory on the target machine. The application will create the following directories in the user's Documents folder:
- OdbcExplorerQueries (for saved queries)
- OdbcExplorerQueryChains (for saved query chains)
- OdbcExplorerTableTemplates (for saved table templates)

## Requirements
- .NET 9.0 Runtime
- System.Data.Odbc (9.0.3)
- System.Data.SQLite.Core (1.0.119)
- ### Valid ODBC DSN configuration for your data source - update this in `MainWindow.xaml.cs`
```cs
private const string OdbcDsn = "DSN=SOTAMAS90;"; // Define DSN constant
```

## Project Information
- **Project Name**: ODBC Explorer (cs_ODBC_SQLite_Explorer)
- **Framework**: .NET 9.0, WPF
- **Languages**: C#, XAML, SQL 