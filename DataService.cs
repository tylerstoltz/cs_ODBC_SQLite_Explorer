using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common; // For DbType
using System.Data.Odbc;
using System.Data.SQLite;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace cs_ODBC_SQLite_Explorer
{
    public class DataService : IDisposable
    {
        private readonly string _odbcDsnString; // Changed from full connection string
        private readonly string _sqliteConnectionString = "Data Source=:memory:;Version=3;New=True;";
        private SQLiteConnection? _sqliteConnection; // Make nullable
        private OdbcConnection? _odbcConnection; // Shared ODBC connection

        // Constructor now only takes the DSN
        public DataService(string odbcDsnString)
        {
            _odbcDsnString = odbcDsnString ?? throw new ArgumentNullException(nameof(odbcDsnString));
            InitializeSqliteConnection();
        }

        // New method to establish and open the shared ODBC connection
        public async Task ConnectOdbcAsync()
        {
            if (_odbcConnection != null && _odbcConnection.State == ConnectionState.Open)
            {
                 Debug.WriteLine("ODBC connection already open.");
                 return; // Already connected
            }

            try
            {
                 _odbcConnection = new OdbcConnection(_odbcDsnString);
                 Debug.WriteLine("Attempting to open ODBC connection (login prompt may appear)... ");
                 await _odbcConnection.OpenAsync();
                 Debug.WriteLine("ODBC Connection Opened Successfully.");
            }
            catch (Exception ex)
            {
                 Debug.WriteLine($"!!! FAILED to open ODBC connection: {ex}");
                 _odbcConnection?.Dispose(); // Clean up if open failed
                 _odbcConnection = null;
                 throw; // Re-throw to signal failure
            }
        }

        private void InitializeSqliteConnection()
        {
            try
            {
                _sqliteConnection = new SQLiteConnection(_sqliteConnectionString);
                _sqliteConnection.Open();
                Debug.WriteLine("In-memory SQLite database initialized.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing SQLite connection: {ex}");
                _sqliteConnection = null; // Ensure it's null on failure
                throw;
            }
        }

        // Updated MirrorDataAsync to accept row limit and tables
        public async Task MirrorDataAsync(int rowLimit, List<string>? tablesToMirror = null)
        {
            if (_sqliteConnection == null || _sqliteConnection.State != ConnectionState.Open)
            {
                throw new InvalidOperationException("SQLite connection is not open.");
            }
             if (_odbcConnection == null || _odbcConnection.State != ConnectionState.Open)
            {
                // This shouldn't happen if ConnectOdbcAsync was called, but check anyway
                throw new InvalidOperationException("ODBC connection is not open. Call ConnectOdbcAsync first.");
            }

            await ClearSqliteDatabaseAsync();

            // No longer need using block for odbcConn, use the class field _odbcConnection
            try
            {
                List<string> tableNames;
                if (tablesToMirror != null)
                {
                    Debug.WriteLine($"Mirroring selected tables: {string.Join(", ", tablesToMirror)}");
                    tableNames = tablesToMirror;
                }
                else
                {
                    // Fetch all tables using the shared connection
                    tableNames = GetOdbcTableNames(); // Pass shared connection implicitly
                    Debug.WriteLine($"Found {tableNames.Count} tables in ODBC source.");
                }

                foreach (var tableName in tableNames)
                {
                    Debug.WriteLine($"Processing table: {tableName}");
                    try
                    {
                        DataTable? schema = GetOdbcTableSchema(tableName); // Use shared connection
                        if (schema == null || schema.Rows.Count == 0)
                        {
                            Debug.WriteLine($"Could not retrieve schema for table {tableName}, skipping.");
                            continue;
                        }

                        string? createTableSql = GenerateSQLiteCreateTableStatement(tableName, schema);
                        if (string.IsNullOrEmpty(createTableSql))
                        {
                            Debug.WriteLine($"Could not generate CREATE statement for table {tableName}, skipping.");
                            continue;
                        }

                        // Ensure _sqliteConnection is not null before using
                        if (_sqliteConnection != null)
                        {
                             using (var cmd = new SQLiteCommand(createTableSql, _sqliteConnection))
                             {
                                 await cmd.ExecuteNonQueryAsync();
                                 Debug.WriteLine($"Created SQLite table for: {tableName}");
                             }
                             await TransferTableDataAsync(tableName, schema, rowLimit); // Pass row limit
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing table {tableName}: {ex}");
                        // Continue with the next table
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during table processing: {ex}");
                throw; // Re-throw critical errors
            }
            // No finally block needed here to close _odbcConnection, it stays open

            Debug.WriteLine("Data mirroring process completed.");
        }

        private async Task ClearSqliteDatabaseAsync()
        {
             if (_sqliteConnection == null || _sqliteConnection.State != ConnectionState.Open)
             {
                 Debug.WriteLine("Cannot clear SQLite DB: Connection is not open.");
                 return;
             }

             var tables = GetSQLiteTableNames(); // Get existing tables
             using (var transaction = _sqliteConnection.BeginTransaction())
             {
                 try
                 {
                     foreach (var tableName in tables)
                     {
                         // Don't use parameters for table names in SQLite - this causes the syntax error
                         string escapedTableName = $"[{tableName.Replace("]", "]]")}]";
                         string dropSql = $"DROP TABLE IF EXISTS {escapedTableName};";
                         
                         using (var cmd = new SQLiteCommand(dropSql, _sqliteConnection, transaction))
                         {
                             await cmd.ExecuteNonQueryAsync();
                             Debug.WriteLine($"Dropped existing SQLite table: {tableName}");
                         }
                     }
                     transaction.Commit();
                 }
                 catch (Exception ex)
                 {
                     Debug.WriteLine($"Error clearing SQLite database: {ex}");
                     try { transaction.Rollback(); } catch { /* Ignore rollback error */ }
                     throw; // Re-throw error after attempting rollback
                 }
             }
        }

        // Method now uses the shared _odbcConnection field
        public List<string> GetOdbcTableNames()
        {
            if (_odbcConnection == null || _odbcConnection.State != ConnectionState.Open)
            {
                throw new InvalidOperationException("ODBC connection is not open. Call ConnectOdbcAsync first.");
            }

            var tableNames = new List<string>();
            try
            {
                DataTable? schema = _odbcConnection.GetSchema("Tables"); // Use field
                if (schema != null)
                {
                    foreach (DataRow row in schema.Rows)
                    {
                        string? tableType = row["TABLE_TYPE"]?.ToString();
                        string? tableName = row["TABLE_NAME"]?.ToString();

                        if (!string.IsNullOrEmpty(tableName) &&
                            (string.IsNullOrEmpty(tableType) || tableType.Equals("TABLE", StringComparison.OrdinalIgnoreCase)))
                        {
                            tableNames.Add(tableName);
                        }
                    }
                }
                else
                {
                    Debug.WriteLine("Warning: GetSchema(\"Tables\") returned null.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting ODBC table names: {ex}");
                throw; // Re-throw as this is critical for initialization
            }
            return tableNames;
        }

        // Method now uses the shared _odbcConnection field
        private DataTable? GetOdbcTableSchema(string tableName)
        {
             if (_odbcConnection == null || _odbcConnection.State != ConnectionState.Open)
            {
                throw new InvalidOperationException("ODBC connection is not open.");
            }
            try
            {
                return _odbcConnection.GetSchema("Columns", new string?[] { null, null, tableName, null }); // Use field
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting schema for table {tableName}: {ex}");
                return null;
            }
        }

        private string? GenerateSQLiteCreateTableStatement(string tableName, DataTable schema)
        {
            var sb = new StringBuilder();
            // Basic sanitization/escaping for table name in CREATE statement
            var escapedTableName = $"[{tableName.Replace("]","\\")}]";
            sb.Append($"CREATE TABLE {escapedTableName} (\n");

            var columnDefinitions = new List<string>();
            foreach (DataRow row in schema.Rows)
            {
                string? columnName = row["COLUMN_NAME"]?.ToString();
                string? odbcType = row["TYPE_NAME"]?.ToString(); // Or "DATA_TYPE" and map based on numeric code

                if (string.IsNullOrEmpty(columnName) || string.IsNullOrEmpty(odbcType))
                {
                    Debug.WriteLine($"Warning: Skipping column with missing name or type in table {tableName}.");
                    continue;
                }

                string sqliteType = MapOdbcTypeToSQLiteType(odbcType); // odbcType is non-null here

                // Basic sanitization/escaping for column name
                var escapedColumnName = $"[{columnName.Replace("]","\\")}]";
                columnDefinitions.Add($"  {escapedColumnName} {sqliteType} NULL");
            }

            if (!columnDefinitions.Any())
            {
                Debug.WriteLine($"Warning: No valid columns found for table {tableName}. Skipping CREATE statement.");
                return null; // Cannot create table with no columns
            }

            sb.Append(string.Join(",\n", columnDefinitions));
            sb.Append("\n);");

            return sb.ToString();
        }

        private string MapOdbcTypeToSQLiteType(string odbcType)
        {
            odbcType = odbcType.ToUpperInvariant();
            switch (odbcType)
            {
                // Numeric Types
                case "INTEGER":
                case "INT":
                case "SMALLINT":
                case "TINYINT":
                case "BIGINT":
                case "COUNTER": // Access specific?
                    return "INTEGER";

                case "DECIMAL":
                case "NUMERIC":
                case "DOUBLE":
                case "FLOAT":
                case "REAL":
                case "MONEY": // Access specific?
                case "CURRENCY":
                    return "REAL";

                // String Types
                case "VARCHAR":
                case "NVARCHAR":
                case "CHAR":
                case "NCHAR":
                case "TEXT":
                case "NTEXT":
                case "MEMO": // Access specific?
                case "STRING": // Generic?
                    return "TEXT";

                // Date/Time Types
                case "DATE":
                case "TIME":
                case "DATETIME":
                case "TIMESTAMP":
                    return "TEXT"; // Store dates/times as ISO8601 strings for simplicity

                // Binary Types
                case "BINARY":
                case "VARBINARY":
                case "LONGVARBINARY":
                case "IMAGE": // SQL Server specific?
                case "BLOB":
                    return "BLOB";

                // Other Types
                case "BIT":
                case "BOOLEAN":
                    return "INTEGER"; // Store booleans as 0 or 1

                default:
                    Debug.WriteLine($"Warning: Unmapped ODBC type '{odbcType}'. Defaulting to TEXT.");
                    return "TEXT"; // Default fallback
            }
        }

        // Method uses shared _odbcConnection and _sqliteConnection, accepts row limit
        private async Task TransferTableDataAsync(string tableName, DataTable schema, int rowLimit)
        {
            if (_odbcConnection == null || _odbcConnection.State != ConnectionState.Open)
            {
                 throw new InvalidOperationException("ODBC connection is not open.");
            }
            if (_sqliteConnection == null || _sqliteConnection.State != ConnectionState.Open)
            {
                 throw new InvalidOperationException("SQLite connection is not open.");
            }

            var columnNames = new List<string>();
            var columnDbTypes = new List<DbType>();
            foreach (DataRow r in schema.Rows)
            {
                string? colName = r["COLUMN_NAME"]?.ToString();
                if (!string.IsNullOrEmpty(colName))
                {
                    columnNames.Add(colName);
                    // Attempt to get DbType, default if fails
                    DbType dbType = DbType.String;
                    try
                    {
                         // PROVIDER_TYPE seems more reliable if available
                         // Use Convert.ToInt32 if PROVIDER_TYPE is not already an int
                         object? providerTypeObj = r["PROVIDER_TYPE"];
                         if (providerTypeObj != null && providerTypeObj != DBNull.Value)
                         {
                              // Common OdbcType enum values map reasonably well to DbType
                              if (Enum.IsDefined(typeof(OdbcType), providerTypeObj))
                              {
                                    dbType = OdbcTypeConverter.ToDbType((OdbcType)providerTypeObj);
                              }
                              else // Fallback if it's just a number code
                              {
                                   int providerTypeCode = Convert.ToInt32(providerTypeObj);
                                   // Add specific mappings if needed based on ProvideX documentation
                                   // dbType = MapProvideXCodeToDbType(providerTypeCode);
                                   Debug.WriteLine($"Using default DbType for unknown PROVIDER_TYPE code {providerTypeCode} in {tableName}.{colName}");
                              }
                         }
                    }
                    catch (Exception ex) { Debug.WriteLine($"Warning: Could not determine DbType for {tableName}.{colName}. Defaulting to String. Error: {ex.Message}"); }
                    columnDbTypes.Add(dbType);
                }
            }

            if (!columnNames.Any())
            {
                Debug.WriteLine($"Warning: No valid columns identified for transfer in table {tableName}.");
                return; // Cannot transfer if no columns
            }

            // Escape names for use in SQL strings
            var escapedTableName = $"[{tableName.Replace("]","\\")}]";
            var columnsForSelect = string.Join(", ", columnNames.Select(c => $"\"{c}\"")); // Quote for ODBC source
            var columnsForInsert = string.Join(", ", columnNames.Select(c => $"[{c.Replace("]","\\")}]")); // Quote for SQLite target
            var parameters = string.Join(", ", columnNames.Select(c => "@" + c.Replace(" ", "_").Replace("-","_").Replace(".","_"))); // Parameter names for SQLite, replace potentially invalid chars

            string selectSql = $"SELECT {columnsForSelect} FROM \"{tableName}\"";

            string insertSql = $"INSERT INTO {escapedTableName} ({columnsForInsert}) VALUES ({parameters});";

            int rowsProcessed = 0;

            try
            {
                // Use shared _odbcConnection
                using (var odbcCmd = new OdbcCommand(selectSql, _odbcConnection))
                using (var reader = await odbcCmd.ExecuteReaderAsync())
                {
                    if (!reader.HasRows)
                    {
                        Debug.WriteLine($"Table {tableName} has no rows to transfer.");
                        return;
                    }

                    using (var transaction = _sqliteConnection.BeginTransaction())
                    {
                        try
                        {
                            using (var sqliteCmd = new SQLiteCommand(insertSql, _sqliteConnection, transaction))
                            {
                                // Prepare parameters once using determined DbTypes
                                for(int i = 0; i < columnNames.Count; i++)
                                {
                                     string colName = columnNames[i];
                                     string paramName = "@" + colName.Replace(" ", "_").Replace("-","_").Replace(".","_");
                                     sqliteCmd.Parameters.Add(paramName, columnDbTypes[i]);
                                }

                                while (await reader.ReadAsync())
                                {
                                    // Use passed rowLimit
                                    if (rowLimit > 0 && rowsProcessed >= rowLimit)
                                    {
                                        Debug.WriteLine($"Row limit ({rowLimit}) reached for table {tableName}.");
                                        break; // Stop reading if limit reached
                                    }

                                    for (int i = 0; i < columnNames.Count; i++)
                                    {
                                        string paramName = "@" + columnNames[i].Replace(" ", "_").Replace("-","_").Replace(".","_");
                                        object value = reader.GetValue(i);

                                        sqliteCmd.Parameters[paramName].Value = (value == DBNull.Value) ? null : value;
                                    }

                                    await sqliteCmd.ExecuteNonQueryAsync();
                                    rowsProcessed++;
                                }
                            }
                            transaction.Commit();
                            Debug.WriteLine($"Transferred {rowsProcessed} rows for table {tableName}.");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error during SQLite batch insert for {tableName}: {ex}");
                            try { transaction.Rollback(); } catch { /* Ignore rollback error */ }
                            throw; // Propagate error
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading data from ODBC table {tableName}: {ex}");
                throw;
            }
        }

        public DataTable ExecuteSqlQuery(string query)
        {
            if (_sqliteConnection == null || _sqliteConnection.State != ConnectionState.Open)
            {
                throw new InvalidOperationException("SQLite connection is not open.");
            }

            var dataTable = new DataTable();
            try
            {
                using (var cmd = new SQLiteCommand(query, _sqliteConnection))
                using (var adapter = new SQLiteDataAdapter(cmd))
                {
                    adapter.Fill(dataTable);
                }
                Debug.WriteLine($"Executed SQL query successfully. Rows returned: {dataTable.Rows.Count}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error executing SQLite query: {ex}");
                throw; // Re-throw for UI to handle
            }
            return dataTable;
        }

        public List<string> GetSQLiteTableNames()
        {
            var tableNames = new List<string>();
            if (_sqliteConnection == null || _sqliteConnection.State != ConnectionState.Open)
            {
                Debug.WriteLine("Cannot get SQLite table names: Connection is not open.");
                return tableNames; // Return empty list
            }

            try
            {
                // Use sqlite_master which is standard
                using (var cmd = new SQLiteCommand("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;", _sqliteConnection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        tableNames.Add(reader.GetString(0));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error retrieving SQLite table names: {ex}");
            }
            return tableNames;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Dispose shared ODBC connection
                if (_odbcConnection != null)
                {
                    if (_odbcConnection.State == ConnectionState.Open)
                    {
                         _odbcConnection.Close();
                    }
                     _odbcConnection.Dispose();
                     _odbcConnection = null;
                     Debug.WriteLine("ODBC connection disposed.");
                }

                if (_sqliteConnection != null)
                {
                    if (_sqliteConnection.State == ConnectionState.Open)
                    {
                        _sqliteConnection.Close();
                    }
                    _sqliteConnection.Dispose();
                    _sqliteConnection = null;
                    Debug.WriteLine("SQLite connection disposed.");
                }
            }
        }

         ~DataService() {
             Dispose(false);
         }
    }

    // Helper class for DbType mapping (basic)
    public static class OdbcTypeConverter
    {
        public static DbType ToDbType(OdbcType odbcType)
        {
            // This is a simplified mapping. More specific mappings might be needed.
            // See: https://docs.microsoft.com/en-us/dotnet/framework/data/adonet/odbc-data-type-mappings
            switch (odbcType)
            {
                case OdbcType.BigInt: return DbType.Int64;
                case OdbcType.Binary: return DbType.Binary;
                case OdbcType.Bit: return DbType.Boolean;
                case OdbcType.Char: return DbType.AnsiStringFixedLength;
                case OdbcType.Date: return DbType.Date;
                case OdbcType.DateTime: return DbType.DateTime;
                case OdbcType.Decimal: return DbType.Decimal;
                case OdbcType.Double: return DbType.Double;
                case OdbcType.Image: return DbType.Binary;
                case OdbcType.Int: return DbType.Int32;
                case OdbcType.NChar: return DbType.StringFixedLength;
                case OdbcType.NText: return DbType.String;
                case OdbcType.Numeric: return DbType.Decimal;
                case OdbcType.NVarChar: return DbType.String;
                case OdbcType.Real: return DbType.Single;
                case OdbcType.SmallInt: return DbType.Int16;
                case OdbcType.Text: return DbType.AnsiString;
                case OdbcType.Time: return DbType.Time;
                case OdbcType.Timestamp: return DbType.DateTime; // Or DateTime2 if precision matters
                case OdbcType.TinyInt: return DbType.Byte; // Or SByte if signed
                case OdbcType.UniqueIdentifier: return DbType.Guid;
                case OdbcType.VarBinary: return DbType.Binary;
                case OdbcType.VarChar: return DbType.AnsiString;
                default: return DbType.Object; // Fallback
            }
        }
    }
} 