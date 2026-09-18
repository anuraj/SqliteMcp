using System.ComponentModel;
using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace SqliteMcp.Tools
{
    [McpServerToolType]
    public sealed class SqliteMcpTools(Func<SqliteConnection> connectionFactory)
    {
        private readonly Func<SqliteConnection> _connectionFactory = connectionFactory;

        private SqliteConnection CreateOpenConnection()
        {
            var connection = _connectionFactory();
            connection.Open();
            return connection;
        }

        // Validates that tableName exists in sqlite_master to prevent SQL injection via table names.
        private async Task ValidateTableNameAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
        {
            if (tableName.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Table '{tableName}' is a SQLite system table and cannot be accessed.");
            }

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@tableName";
            command.Parameters.AddWithValue("@tableName", tableName);
            var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            if (count == 0)
            {
                throw new ArgumentException($"Table '{tableName}' does not exist.");
            }
        }

        private string QuoteIdentifier(string name) =>
            $"[{name.Replace("]", "]]")}]";

        private string QuoteStringLiteral(string value) =>
            $"'{value.Replace("'", "''")}'";

        private string CreateParameterName(string prefix, int index) =>
            $"@{prefix}{index}";

        private static void EnsureNotEmpty(IReadOnlyDictionary<string, object> values, string parameterName, string itemDescription)
        {
            if (values.Count == 0)
            {
                throw new ArgumentException($"At least one {itemDescription} must be provided.", parameterName);
            }
        }

        [McpServerTool(Destructive = false, ReadOnly = true, Name = "db_info")]
        [Description("Get information about the SQLite database including path, existence, size, and table count")]
        public async Task<string> GetDatabaseInfo(CancellationToken cancellationToken)
        {
            try
            {
                using var connection = CreateOpenConnection();
                var databasePath = connection.DataSource;
                bool exists = File.Exists(databasePath);
                long sizeInBytes = exists ? new FileInfo(databasePath).Length : 0;

                int tableCount;
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT count(name) FROM sqlite_master WHERE type='table';";
                    tableCount = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
                }

                return $"Database Path: {databasePath}\n" +
                    $"Exists: {exists}\n" +
                    $"Size (bytes): {sizeInBytes}\n" +
                    $"Table Count: {tableCount}";
            }
            catch (Exception ex)
            {
                return $"Error retrieving database info: {ex.Message}";
            }
        }

        [McpServerTool(Destructive = false, ReadOnly = true, Name = "list_tables")]
        [Description("List all user tables in the SQLite database (excludes system tables)")]
        public async Task<string> ListTables(CancellationToken cancellationToken)
        {
            try
            {
                using var connection = CreateOpenConnection();

                List<string> tables = [];
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
                    using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        tables.Add(reader.GetString(0));
                    }
                }

                return tables.Count > 0
                    ? string.Join("\n", tables)
                    : "No tables found in the database.";
            }
            catch (Exception ex)
            {
                return $"Error listing tables: {ex.Message}";
            }
        }

        [McpServerTool(Destructive = false, ReadOnly = true, Name = "get_table_schema")]
        [Description("Get the schema of a specified table in the SQLite database")]
        public async Task<string> GetTableSchema(string tableName, CancellationToken cancellationToken)
        {
            try
            {
                using var connection = CreateOpenConnection();
                await ValidateTableNameAsync(connection, tableName, cancellationToken);

                string schema = string.Empty;
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = $"SELECT * FROM pragma_table_info({QuoteStringLiteral(tableName)});";
                    using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    if (!reader.HasRows)
                    {
                        return $"Table '{tableName}' does not exist.";
                    }

                    schema += $"Schema for table '{tableName}':\n";
                    schema += "CID | Name | Type | NotNull | DefaultValue | PK\n";
                    schema += "-----------------------------------------------\n";
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        schema += $"{reader.GetInt32(0)} | {reader.GetString(1)} | {reader.GetString(2)} | " +
                                  $"{reader.GetInt32(3)} | {(reader.IsDBNull(4) ? "NULL" : reader.GetString(4))} | " +
                                  $"{reader.GetInt32(5)}\n";
                    }
                }

                return schema;
            }
            catch (Exception ex)
            {
                return $"Error retrieving schema for table '{tableName}': {ex.Message}";
            }
        }

        [McpServerTool(Destructive = true, ReadOnly = false, Name = "create_record")]
        [Description("Create a new record in the specified table with given column values")]
        public async Task<string> CreateRecord(string tableName, Dictionary<string, object> columnValues, CancellationToken cancellationToken)
        {
            try
            {
                EnsureNotEmpty(columnValues, nameof(columnValues), "column value");
                using var connection = CreateOpenConnection();
                await ValidateTableNameAsync(connection, tableName, cancellationToken);

                var entries = columnValues.ToList();
                var columns = string.Join(", ", entries.Select(entry => QuoteIdentifier(entry.Key)));
                var parameters = string.Join(", ", entries.Select((_, index) => CreateParameterName("p", index)));

                using var command = connection.CreateCommand();
                command.CommandText = $"INSERT INTO {QuoteIdentifier(tableName)} ({columns}) VALUES ({parameters});";
                foreach (var (kvp, index) in entries.Select((entry, index) => (entry, index)))
                {
                    command.Parameters.AddWithValue(CreateParameterName("p", index), kvp.Value);
                }

                int rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);

                return rowsAffected > 0
                    ? $"Record successfully created in table '{tableName}'."
                    : $"Failed to create record in table '{tableName}'.";
            }
            catch (Exception ex)
            {
                return $"Error creating record in table '{tableName}': {ex.Message}";
            }
        }

        [McpServerTool(Destructive = false, ReadOnly = true, Name = "read_records")]
        [Description("Read records from a table with optional conditions (column/value pairs matched with AND), limit, and offset")]
        public async Task<string> ReadRecords(string tableName, CancellationToken cancellationToken, Dictionary<string, object>? conditions = null, int limit = 100, int offset = 0)
        {
            try
            {
                using var connection = CreateOpenConnection();
                await ValidateTableNameAsync(connection, tableName, cancellationToken);

                if (limit < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be non-negative.");
                }
                if (offset < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be non-negative.");
                }

                string query = $"SELECT * FROM {QuoteIdentifier(tableName)}";

                using var command = connection.CreateCommand();
                if (conditions != null && conditions.Count > 0)
                {
                    var conditionEntries = conditions.ToList();
                    var whereClauses = conditionEntries.Select((entry, index) => $"{QuoteIdentifier(entry.Key)} = {CreateParameterName("c", index)}");
                    query += " WHERE " + string.Join(" AND ", whereClauses);
                    foreach (var (kvp, index) in conditionEntries.Select((entry, index) => (entry, index)))
                    {
                        command.Parameters.AddWithValue(CreateParameterName("c", index), kvp.Value);
                    }
                }
                query += $" LIMIT {limit} OFFSET {offset};";
                command.CommandText = query;

                using var reader = await command.ExecuteReaderAsync(cancellationToken);
                var results = new List<Dictionary<string, object>>();
                while (await reader.ReadAsync(cancellationToken))
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[reader.GetName(i)] = reader.GetValue(i);
                    }
                    results.Add(row);
                }

                return JsonSerializer.Serialize(results);
            }
            catch (Exception ex)
            {
                return $"Error reading records from table '{tableName}': {ex.Message}";
            }
        }

        [McpServerTool(Destructive = true, ReadOnly = false, Name = "update_records")]
        [Description("Update records in a table matching given conditions (column/value pairs) with new column values")]
        public async Task<string> UpdateRecords(string tableName, Dictionary<string, object> columnValues, Dictionary<string, object> conditions, CancellationToken cancellationToken)
        {
            try
            {
                EnsureNotEmpty(columnValues, nameof(columnValues), "column value");
                EnsureNotEmpty(conditions, nameof(conditions), "condition");
                using var connection = CreateOpenConnection();
                await ValidateTableNameAsync(connection, tableName, cancellationToken);

                var setEntries = columnValues.ToList();
                var conditionEntries = conditions.ToList();
                var setClauses = string.Join(", ", setEntries.Select((entry, index) => $"{QuoteIdentifier(entry.Key)} = {CreateParameterName("s", index)}"));
                var whereClauses = conditionEntries.Select((entry, index) => $"{QuoteIdentifier(entry.Key)} = {CreateParameterName("c", index)}");

                using var command = connection.CreateCommand();
                command.CommandText = $"UPDATE {QuoteIdentifier(tableName)} SET {setClauses} WHERE {string.Join(" AND ", whereClauses)};";

                foreach (var (kvp, index) in setEntries.Select((entry, index) => (entry, index)))
                {
                    command.Parameters.AddWithValue(CreateParameterName("s", index), kvp.Value);
                }
                foreach (var (kvp, index) in conditionEntries.Select((entry, index) => (entry, index)))
                {
                    command.Parameters.AddWithValue(CreateParameterName("c", index), kvp.Value);
                }

                int rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);

                return rowsAffected > 0
                    ? $"{rowsAffected} record(s) successfully updated in table '{tableName}'."
                    : $"No records updated in table '{tableName}'.";
            }
            catch (Exception ex)
            {
                return $"Error updating records in table '{tableName}': {ex.Message}";
            }
        }

        [McpServerTool(Destructive = true, ReadOnly = false, Name = "delete_records")]
        [Description("Delete records from a table matching given conditions (column/value pairs)")]
        public async Task<string> DeleteRecords(string tableName, Dictionary<string, object> conditions, CancellationToken cancellationToken)
        {
            try
            {
                EnsureNotEmpty(conditions, nameof(conditions), "condition");
                using var connection = CreateOpenConnection();
                await ValidateTableNameAsync(connection, tableName, cancellationToken);

                var conditionEntries = conditions.ToList();
                var whereClauses = conditionEntries.Select((entry, index) => $"{QuoteIdentifier(entry.Key)} = {CreateParameterName("c", index)}");

                using var command = connection.CreateCommand();
                command.CommandText = $"DELETE FROM {QuoteIdentifier(tableName)} WHERE {string.Join(" AND ", whereClauses)};";

                foreach (var (kvp, index) in conditionEntries.Select((entry, index) => (entry, index)))
                {
                    command.Parameters.AddWithValue(CreateParameterName("c", index), kvp.Value);
                }

                int rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);

                return rowsAffected > 0
                    ? $"{rowsAffected} record(s) successfully deleted from table '{tableName}'."
                    : $"No records deleted from table '{tableName}'.";
            }
            catch (Exception ex)
            {
                return $"Error deleting records from table '{tableName}': {ex.Message}";
            }
        }

        [McpServerTool(Destructive = true, ReadOnly = false, Name = "execute_query")]
        [Description("Execute a SQL query against the database with optional parameter values and return the results")]
        public async Task<string> ExecuteQuery(string sqlQuery, CancellationToken cancellationToken, Dictionary<string, object>? parameters = null)
        {
            try
            {
                using var connection = CreateOpenConnection();

                using var command = connection.CreateCommand();
                command.CommandText = sqlQuery;

                if (parameters != null)
                {
                    foreach (var kvp in parameters)
                    {
                        command.Parameters.AddWithValue("@" + kvp.Key, kvp.Value);
                    }
                }

                if (!sqlQuery.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                {
                    int rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
                    return $"{rowsAffected} row(s) affected.";
                }

                using var reader = await command.ExecuteReaderAsync(cancellationToken);
                var results = new List<Dictionary<string, object>>();
                while (await reader.ReadAsync(cancellationToken))
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[reader.GetName(i)] = reader.GetValue(i);
                    }
                    results.Add(row);
                }

                return results.Count > 0
                    ? JsonSerializer.Serialize(results)
                    : "Query executed successfully with no results.";
            }
            catch (Exception ex)
            {
                return $"Error executing query: {ex.Message}";
            }
        }

        [McpServerTool(Destructive = false, ReadOnly = true, Name = "execution_plan")]
        [McpAppUi(ResourceUri = "ui://sqlite-app/execution-plan")]
        [Description("Get the execution plan for a SQL query.")]
        public async Task<CallToolResult> ExecutionPlan([Description("SQL query to get the execution plan for")] string sqlQuery, CancellationToken cancellationToken)
        {
            try
            {
                using var connection = CreateOpenConnection();

                using var command = connection.CreateCommand();
                command.CommandText = $"EXPLAIN QUERY PLAN {sqlQuery}";

                using var reader = await command.ExecuteReaderAsync(cancellationToken);
                var results = new List<Dictionary<string, object>>();
                while (await reader.ReadAsync(cancellationToken))
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[reader.GetName(i)] = reader.GetValue(i);
                    }
                    results.Add(row);
                }

                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = $"Execution plan for query: {sqlQuery}" }],
                    StructuredContent = JsonSerializer.SerializeToElement(new
                    {
                        query = sqlQuery,
                        columns = results.FirstOrDefault()?.Keys ?? default,
                        results
                    })
                };
            }
            catch (Exception ex)
            {
                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = $"Error getting execution plan: {ex.Message}" }],
                };
            }
        }

        [McpServerTool(Destructive = false, ReadOnly = true, Name = "export_tables_to_excel")]
        [Description("Export specified tables to an Excel file. Returns the path to the generated Excel file.")]
        public async Task<string> ExportTablesToExcel([Description("List of table names to export, comma or semicolon separated")] string tables, CancellationToken cancellationToken)
        {
            var tableNames = string.IsNullOrWhiteSpace(tables) ? null : 
                tables.Split([',', ';']).Select(t => t.Trim()).ToList();
            try
            {
                using var connection = CreateOpenConnection();
                if (tableNames == null || tableNames.Count == 0)
                {
                    //Select all user tables if no specific tables are provided
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
                    using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    tableNames = [];
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        tableNames.Add(reader.GetString(0));
                    }
                }

                var excelFilePath = Path.Combine(Path.GetTempPath(), $"SqliteExport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                using var workbook = new ClosedXML.Excel.XLWorkbook();
                foreach (var tableName in tableNames)
                {
                    await ValidateTableNameAsync(connection, tableName, cancellationToken);
                    using var command = connection.CreateCommand();
                    command.CommandText = $"SELECT * FROM {QuoteIdentifier(tableName)}";
                    using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    var dataTable = new System.Data.DataTable();
                    dataTable.Load(reader);
                    workbook.Worksheets.Add(dataTable, tableName);
                }

                workbook.SaveAs(excelFilePath);
                return excelFilePath;
            }
            catch (Exception ex)
            {
                return $"Error exporting tables to Excel: {ex.Message}";
            }
        }
    }
}