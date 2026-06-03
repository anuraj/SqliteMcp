using System.ComponentModel;
using System.Data;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Server;

namespace SqliteMcp
{
    [McpServerToolType]
    public class Tools(SqliteConnection sqliteConnection)
    {
        private readonly SqliteConnection _sqliteConnection = sqliteConnection;

        private void EnsureOpen()
        {
            if (_sqliteConnection.State == ConnectionState.Closed)
                _sqliteConnection.Open();
        }

        // Validates that tableName exists in sqlite_master to prevent SQL injection via table names.
        private void ValidateTableName(string tableName)
        {
            using var command = _sqliteConnection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@tableName";
            command.Parameters.AddWithValue("@tableName", tableName);
            var count = Convert.ToInt32(command.ExecuteScalar());
            if (count == 0)
                throw new ArgumentException($"Table '{tableName}' does not exist.");
        }

        private static string QuoteIdentifier(string name) =>
            $"[{name.Replace("]", "]]")}]";

        [McpServerTool(Destructive = false, ReadOnly = true, Name = "db_info")]
        [Description("Get information about the SQLite database including path, existence, size, and table count")]
        public string GetDatabaseInfo()
        {
            try
            {
                var databasePath = _sqliteConnection.DataSource;
                bool exists = File.Exists(databasePath);
                long sizeInBytes = exists ? new FileInfo(databasePath).Length : 0;

                EnsureOpen();

                int tableCount = 0;
                using (var command = _sqliteConnection.CreateCommand())
                {
                    command.CommandText = "SELECT count(name) FROM sqlite_master WHERE type='table';";
                    tableCount = Convert.ToInt32(command.ExecuteScalar());
                }
                _sqliteConnection.Close();

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
        public string ListTables()
        {
            try
            {
                EnsureOpen();

                List<string> tables = [];
                using (var command = _sqliteConnection.CreateCommand())
                {
                    command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        tables.Add(reader.GetString(0));
                    }
                }
                _sqliteConnection.Close();

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
        public string GetTableSchema(string tableName)
        {
            try
            {
                EnsureOpen();
                ValidateTableName(tableName);

                string schema = string.Empty;
                using (var command = _sqliteConnection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM pragma_table_info(@tableName);";
                    command.Parameters.AddWithValue("@tableName", tableName);
                    using var reader = command.ExecuteReader();
                    if (!reader.HasRows)
                    {
                        return $"Table '{tableName}' does not exist.";
                    }

                    schema += $"Schema for table '{tableName}':\n";
                    schema += "CID | Name | Type | NotNull | DefaultValue | PK\n";
                    schema += "-----------------------------------------------\n";
                    while (reader.Read())
                    {
                        schema += $"{reader.GetInt32(0)} | {reader.GetString(1)} | {reader.GetString(2)} | " +
                                  $"{reader.GetInt32(3)} | {(reader.IsDBNull(4) ? "NULL" : reader.GetString(4))} | " +
                                  $"{reader.GetInt32(5)}\n";
                    }
                }
                _sqliteConnection.Close();

                return schema;
            }
            catch (Exception ex)
            {
                return $"Error retrieving schema for table '{tableName}': {ex.Message}";
            }
        }

        [McpServerTool(Destructive = true, ReadOnly = false, Name = "create_record")]
        [Description("Create a new record in the specified table with given column values")]
        public string CreateRecord(string tableName, Dictionary<string, object> columnValues)
        {
            try
            {
                EnsureOpen();
                ValidateTableName(tableName);

                var columns = string.Join(", ", columnValues.Keys.Select(QuoteIdentifier));
                var parameters = string.Join(", ", columnValues.Keys.Select(k => "@" + k));

                using var command = _sqliteConnection.CreateCommand();
                command.CommandText = $"INSERT INTO {QuoteIdentifier(tableName)} ({columns}) VALUES ({parameters});";
                foreach (var kvp in columnValues)
                {
                    command.Parameters.AddWithValue("@" + kvp.Key, kvp.Value);
                }

                int rowsAffected = command.ExecuteNonQuery();
                _sqliteConnection.Close();

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
        public string ReadRecords(string tableName, Dictionary<string, object>? conditions = null, int limit = 100, int offset = 0)
        {
            try
            {
                EnsureOpen();
                ValidateTableName(tableName);

                string query = $"SELECT * FROM {QuoteIdentifier(tableName)}";

                using var command = _sqliteConnection.CreateCommand();
                if (conditions != null && conditions.Count > 0)
                {
                    var whereClauses = conditions.Keys.Select(k => $"{QuoteIdentifier(k)} = @cond_{k}");
                    query += " WHERE " + string.Join(" AND ", whereClauses);
                    foreach (var kvp in conditions)
                    {
                        command.Parameters.AddWithValue("@cond_" + kvp.Key, kvp.Value);
                    }
                }
                query += $" LIMIT {limit} OFFSET {offset};";
                command.CommandText = query;

                using var reader = command.ExecuteReader();
                var results = new List<Dictionary<string, object>>();
                while (reader.Read())
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[reader.GetName(i)] = reader.GetValue(i);
                    }
                    results.Add(row);
                }
                _sqliteConnection.Close();

                return JsonSerializer.Serialize(results);
            }
            catch (Exception ex)
            {
                return $"Error reading records from table '{tableName}': {ex.Message}";
            }
        }

        [McpServerTool(Destructive = true, ReadOnly = false, Name = "update_records")]
        [Description("Update records in a table matching given conditions (column/value pairs) with new column values")]
        public string UpdateRecords(string tableName, Dictionary<string, object> columnValues, Dictionary<string, object> conditions)
        {
            try
            {
                EnsureOpen();
                ValidateTableName(tableName);

                var setClauses = string.Join(", ", columnValues.Keys.Select(k => $"{QuoteIdentifier(k)} = @set_{k}"));
                var whereClauses = conditions.Keys.Select(k => $"{QuoteIdentifier(k)} = @cond_{k}");

                using var command = _sqliteConnection.CreateCommand();
                command.CommandText = $"UPDATE {QuoteIdentifier(tableName)} SET {setClauses} WHERE {string.Join(" AND ", whereClauses)};";

                foreach (var kvp in columnValues)
                    command.Parameters.AddWithValue("@set_" + kvp.Key, kvp.Value);
                foreach (var kvp in conditions)
                    command.Parameters.AddWithValue("@cond_" + kvp.Key, kvp.Value);

                int rowsAffected = command.ExecuteNonQuery();
                _sqliteConnection.Close();

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
        public string DeleteRecords(string tableName, Dictionary<string, object> conditions)
        {
            try
            {
                EnsureOpen();
                ValidateTableName(tableName);

                var whereClauses = conditions.Keys.Select(k => $"{QuoteIdentifier(k)} = @cond_{k}");

                using var command = _sqliteConnection.CreateCommand();
                command.CommandText = $"DELETE FROM {QuoteIdentifier(tableName)} WHERE {string.Join(" AND ", whereClauses)};";

                foreach (var kvp in conditions)
                    command.Parameters.AddWithValue("@cond_" + kvp.Key, kvp.Value);

                int rowsAffected = command.ExecuteNonQuery();
                _sqliteConnection.Close();

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
        public string ExecuteQuery(string sqlQuery, Dictionary<string, object>? parameters = null)
        {
            try
            {
                EnsureOpen();

                using var command = _sqliteConnection.CreateCommand();
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
                    int rowsAffected = command.ExecuteNonQuery();
                    _sqliteConnection.Close();
                    return $"{rowsAffected} row(s) affected.";
                }

                using var reader = command.ExecuteReader();
                var results = new List<Dictionary<string, object>>();
                while (reader.Read())
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[reader.GetName(i)] = reader.GetValue(i);
                    }
                    results.Add(row);
                }
                _sqliteConnection.Close();

                return results.Count > 0
                    ? JsonSerializer.Serialize(results)
                    : "Query executed successfully with no results.";
            }
            catch (Exception ex)
            {
                return $"Error executing query: {ex.Message}";
            }
        }
    }
}