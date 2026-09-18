using System.Text.Json;
using System.Threading;
using Microsoft.Data.Sqlite;
using Xunit;

namespace SqliteMcp.Tests;

/// <summary>
/// Uses a named shared-cache in-memory SQLite database so that data survives
/// the Close() calls inside Tools methods without being destroyed.
/// A "keeper" connection holds the database alive for the lifetime of each test.
/// </summary>
public class ToolsTests : IDisposable
{
    private readonly string _dbName = Guid.NewGuid().ToString("N");
    private readonly SqliteConnection _keeper;
    private readonly Tools.SqliteMcpTools _tools;

    public ToolsTests()
    {
        var connStr = $"Data Source={_dbName};Mode=Memory;Cache=Shared";
        _keeper = new SqliteConnection(connStr);
        _keeper.Open();
        _tools = new Tools.SqliteMcpTools(() => new SqliteConnection(connStr));
    }

    public void Dispose()
    {
        _keeper.Dispose();
    }

    private void Execute(string sql)
    {
        using var cmd = _keeper.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void CreateProductsTable() =>
        Execute("CREATE TABLE IF NOT EXISTS Products (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Price REAL)");

    private void CreateWeirdProductsTable() =>
        Execute("CREATE TABLE IF NOT EXISTS WeirdProducts (Id INTEGER PRIMARY KEY, [Display Name] TEXT NOT NULL, [Unit Price] REAL)");

    private void SeedProducts() =>
        Execute("INSERT INTO Products (Name, Price) VALUES ('Apple', 1.99), ('Banana', 0.99), ('Cherry', 3.49)");

    private void SeedWeirdProducts() =>
        Execute("INSERT INTO WeirdProducts ([Display Name], [Unit Price]) VALUES ('Dragon Fruit', 5.99), ('Star Fruit', 4.49)");

    // ── GetDatabaseInfo ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetDatabaseInfo_ReturnsFormattedOutput()
    {
        CreateProductsTable();
        var result = await _tools.GetDatabaseInfo(CancellationToken.None);
        Assert.Contains("Database Path:", result);
        Assert.Contains("Exists:", result);
        Assert.Contains("Size (bytes):", result);
        Assert.Contains("Table Count:", result);
    }

    [Fact]
    public async Task GetDatabaseInfo_ReturnsCorrectTableCount()
    {
        CreateProductsTable();
        Execute("CREATE TABLE IF NOT EXISTS Orders (Id INTEGER PRIMARY KEY)");
        var result = await _tools.GetDatabaseInfo(CancellationToken.None);
        Assert.Contains("Table Count: 2", result);
    }

    // ── ListTables ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ListTables_EmptyDatabase_ReturnsNoTablesMessage()
    {
        var result = await _tools.ListTables(CancellationToken.None);
        Assert.Equal("No tables found in the database.", result);
    }

    [Fact]
    public async Task ListTables_WithTables_ReturnsTableNames()
    {
        CreateProductsTable();
        Execute("CREATE TABLE IF NOT EXISTS Orders (Id INTEGER PRIMARY KEY)");
        var result = await _tools.ListTables(CancellationToken.None);
        Assert.Contains("Products", result);
        Assert.Contains("Orders", result);
    }

    [Fact]
    public async Task ListTables_ExcludesSystemTables()
    {
        CreateProductsTable();
        var result = await _tools.ListTables(CancellationToken.None);
        Assert.DoesNotContain("sqlite_", result);
    }

    // ── GetTableSchema ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetTableSchema_ValidTable_ReturnsSchemaWithColumns()
    {
        CreateProductsTable();
        var result = await _tools.GetTableSchema("Products", CancellationToken.None);
        Assert.Contains("Schema for table 'Products':", result);
        Assert.Contains("Name", result);
        Assert.Contains("Price", result);
    }

    [Fact]
    public async Task GetTableSchema_NonExistentTable_ReturnsError()
    {
        var result = await _tools.GetTableSchema("NonExistent", CancellationToken.None);
        Assert.Contains("does not exist", result);
    }

    [Fact]
    public async Task GetTableSchema_SystemTable_ReturnsError()
    {
        CreateProductsTable();
        var result = await _tools.GetTableSchema("sqlite_master", CancellationToken.None);
        Assert.Contains("system table", result);
    }

    // ── CreateRecord ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRecord_ValidData_ReturnsSuccessMessage()
    {
        CreateProductsTable();
        var result = await _tools.CreateRecord("Products", new Dictionary<string, object>
        {
            ["Name"] = "Mango",
            ["Price"] = 2.49
        }, CancellationToken.None);
        Assert.Contains("successfully created", result);
        Assert.Contains("Products", result);
    }

    [Fact]
    public async Task CreateRecord_NonExistentTable_ReturnsError()
    {
        var result = await _tools.CreateRecord("Ghost", new Dictionary<string, object>
        {
            ["Name"] = "X"
        }, CancellationToken.None);
        Assert.Contains("Error", result);
    }

    [Fact]
    public async Task CreateRecord_EmptyValues_ReturnsError()
    {
        CreateProductsTable();

        var result = await _tools.CreateRecord("Products", [], CancellationToken.None);

        Assert.Contains("At least one column value must be provided", result);
    }

    [Fact]
    public async Task CreateRecord_WithSpacedColumnName_ReturnsSuccessMessage()
    {
        CreateWeirdProductsTable();
        var result = await _tools.CreateRecord("WeirdProducts", new Dictionary<string, object>
        {
            ["Display Name"] = "Passion Fruit",
            ["Unit Price"] = 6.79
        }, CancellationToken.None);
        Assert.Contains("successfully created", result);
    }

    // ── ReadRecords ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadRecords_NoConditions_ReturnsAllRows()
    {
        CreateProductsTable();
        SeedProducts();
        var result = await _tools.ReadRecords("Products", CancellationToken.None);
        var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(result);
        Assert.NotNull(rows);
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public async Task ReadRecords_WithConditions_ReturnsFilteredRows()
    {
        CreateProductsTable();
        SeedProducts();
        var result = await _tools.ReadRecords("Products", CancellationToken.None, new Dictionary<string, object> { ["Name"] = "Apple" });
        var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(result);
        Assert.NotNull(rows);
        Assert.Single(rows);
        Assert.Equal("Apple", rows[0]["Name"].GetString());
    }

    [Fact]
    public async Task ReadRecords_WithLimit_RespectsLimit()
    {
        CreateProductsTable();
        SeedProducts();
        var result = await _tools.ReadRecords("Products", CancellationToken.None, limit: 2);
        var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(result);
        Assert.NotNull(rows);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task ReadRecords_WithOffset_RespectsOffset()
    {
        CreateProductsTable();
        SeedProducts();
        var all = await _tools.ReadRecords("Products", CancellationToken.None);
        var allRows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(all)!;

        var result = await _tools.ReadRecords("Products", CancellationToken.None, offset: 1);
        var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(result);
        Assert.NotNull(rows);
        Assert.Equal(2, rows.Count);
        Assert.Equal(allRows[1]["Name"].GetString(), rows[0]["Name"].GetString());
    }

    [Fact]
    public async Task ReadRecords_NonExistentTable_ReturnsError()
    {
        var result = await _tools.ReadRecords("Ghost", CancellationToken.None);
        Assert.Contains("Error", result);
    }

    [Fact]
    public async Task ReadRecords_WithSpacedConditionColumn_ReturnsFilteredRows()
    {
        CreateWeirdProductsTable();
        SeedWeirdProducts();
        var result = await _tools.ReadRecords("WeirdProducts", CancellationToken.None, new Dictionary<string, object> { ["Display Name"] = "Dragon Fruit" });
        var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(result);
        Assert.NotNull(rows);
        Assert.Single(rows);
        Assert.Equal("Dragon Fruit", rows[0]["Display Name"].GetString());
    }

    [Fact]
    public async Task ReadRecords_NegativeLimit_ReturnsError()
    {
        CreateProductsTable();
        var result = await _tools.ReadRecords("Products", CancellationToken.None, limit: -1);
        Assert.Contains("Limit must be non-negative", result);
    }

    [Fact]
    public async Task ReadRecords_NegativeOffset_ReturnsError()
    {
        CreateProductsTable();
        var result = await _tools.ReadRecords("Products", CancellationToken.None, offset: -1);
        Assert.Contains("Offset must be non-negative", result);
    }

    // ── UpdateRecords ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateRecords_MatchingRows_ReturnsUpdatedCount()
    {
        CreateProductsTable();
        SeedProducts();
        var result = await _tools.UpdateRecords(
            "Products",
            new Dictionary<string, object> { ["Price"] = 9.99 },
            new Dictionary<string, object> { ["Name"] = "Apple" },
            CancellationToken.None);
        Assert.Contains("1 record(s) successfully updated", result);
    }

    [Fact]
    public async Task UpdateRecords_NoMatchingRows_ReturnsNoRecordsMessage()
    {
        CreateProductsTable();
        SeedProducts();
        var result = await _tools.UpdateRecords(
            "Products",
            new Dictionary<string, object> { ["Price"] = 0.0 },
            new Dictionary<string, object> { ["Name"] = "Durian" },
            CancellationToken.None);
        Assert.Contains("No records updated", result);
    }

    [Fact]
    public async Task UpdateRecords_NonExistentTable_ReturnsError()
    {
        var result = await _tools.UpdateRecords(
            "Ghost",
            new Dictionary<string, object> { ["Name"] = "X" },
            new Dictionary<string, object> { ["Id"] = 1 },
            CancellationToken.None);
        Assert.Contains("Error", result);
    }

    [Fact]
    public async Task UpdateRecords_EmptyValuesOrConditions_ReturnsError()
    {
        CreateProductsTable();

        var emptyValuesResult = await _tools.UpdateRecords(
            "Products",
            [],
            new Dictionary<string, object> { ["Id"] = 1 },
            CancellationToken.None);
        var emptyConditionsResult = await _tools.UpdateRecords(
            "Products",
            new Dictionary<string, object> { ["Name"] = "Mango" },
            [],
            CancellationToken.None);

        Assert.Contains("At least one column value must be provided", emptyValuesResult);
        Assert.Contains("At least one condition must be provided", emptyConditionsResult);
    }

    [Fact]
    public async Task UpdateRecords_WithSpacedColumnNames_ReturnsUpdatedCount()
    {
        CreateWeirdProductsTable();
        SeedWeirdProducts();
        var result = await _tools.UpdateRecords(
            "WeirdProducts",
            new Dictionary<string, object> { ["Unit Price"] = 7.25 },
            new Dictionary<string, object> { ["Display Name"] = "Dragon Fruit" },
            CancellationToken.None);
        Assert.Contains("1 record(s) successfully updated", result);
    }

    // ── DeleteRecords ───────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteRecords_MatchingRows_ReturnsDeletedCount()
    {
        CreateProductsTable();
        SeedProducts();
        var result = await _tools.DeleteRecords(
            "Products",
            new Dictionary<string, object> { ["Name"] = "Banana" },
            CancellationToken.None);
        Assert.Contains("1 record(s) successfully deleted", result);
    }

    [Fact]
    public async Task DeleteRecords_NoMatchingRows_ReturnsNoRecordsMessage()
    {
        CreateProductsTable();
        SeedProducts();
        var result = await _tools.DeleteRecords(
            "Products",
            new Dictionary<string, object> { ["Name"] = "Papaya" },
            CancellationToken.None);
        Assert.Contains("No records deleted", result);
    }

    [Fact]
    public async Task DeleteRecords_NonExistentTable_ReturnsError()
    {
        var result = await _tools.DeleteRecords(
            "Ghost",
            new Dictionary<string, object> { ["Id"] = 1 },
            CancellationToken.None);
        Assert.Contains("Error", result);
    }

    [Fact]
    public async Task DeleteRecords_EmptyConditions_ReturnsErrorWithoutDeletingRecords()
    {
        CreateProductsTable();
        SeedProducts();

        var result = await _tools.DeleteRecords("Products", [], CancellationToken.None);
        var records = await _tools.ReadRecords("Products", CancellationToken.None);
        var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(records);

        Assert.Contains("At least one condition must be provided", result);
        Assert.NotNull(rows);
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public async Task DeleteRecords_WithSpacedConditionColumn_ReturnsDeletedCount()
    {
        CreateWeirdProductsTable();
        SeedWeirdProducts();
        var result = await _tools.DeleteRecords(
            "WeirdProducts",
            new Dictionary<string, object> { ["Display Name"] = "Star Fruit" },
            CancellationToken.None);
        Assert.Contains("1 record(s) successfully deleted", result);
    }

    // ── ExecuteQuery ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteQuery_SelectWithResults_ReturnsJsonArray()
    {
        CreateProductsTable();
        SeedProducts();
        var result = await _tools.ExecuteQuery("SELECT * FROM Products", CancellationToken.None);
        var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(result);
        Assert.NotNull(rows);
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public async Task ExecuteQuery_SelectWithNoResults_ReturnsNoResultsMessage()
    {
        CreateProductsTable();
        var result = await _tools.ExecuteQuery("SELECT * FROM Products WHERE Id = -1", CancellationToken.None);
        Assert.Equal("Query executed successfully with no results.", result);
    }

    [Fact]
    public async Task ExecuteQuery_NonSelectStatement_ReturnsRowsAffected()
    {
        CreateProductsTable();
        SeedProducts();
        var result = await _tools.ExecuteQuery("DELETE FROM Products WHERE Name = 'Apple'", CancellationToken.None);
        Assert.Contains("row(s) affected", result);
    }

    [Fact]
    public async Task ExecuteQuery_SelectWithParameters_SubstitutesCorrectly()
    {
        CreateProductsTable();
        SeedProducts();
        var result = await _tools.ExecuteQuery(
            "SELECT * FROM Products WHERE Name = @name",
            CancellationToken.None,
            new Dictionary<string, object> { ["name"] = "Cherry" });
        var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(result);
        Assert.NotNull(rows);
        Assert.Single(rows);
        Assert.Equal("Cherry", rows[0]["Name"].GetString());
    }

    // ── ExportTablesToExcel ────────────────────────────────────────────────

    [Fact]
    public async Task ExportTablesToExcel_CreatesDistinctFilesInIsolatedDirectories()
    {
        CreateProductsTable();
        SeedProducts();

        var firstPath = await _tools.ExportTablesToExcel("Products", CancellationToken.None);
        var secondPath = await _tools.ExportTablesToExcel("Products", CancellationToken.None);

        try
        {
            Assert.True(File.Exists(firstPath));
            Assert.True(File.Exists(secondPath));
            Assert.NotEqual(Path.GetDirectoryName(firstPath), Path.GetDirectoryName(secondPath));
            Assert.NotEqual(firstPath, secondPath);
            Assert.True(new FileInfo(firstPath).Length > 0);
            Assert.True(new FileInfo(secondPath).Length > 0);
        }
        finally
        {
            if (File.Exists(firstPath))
            {
                Directory.Delete(Path.GetDirectoryName(firstPath)!, recursive: true);
            }

            if (File.Exists(secondPath))
            {
                Directory.Delete(Path.GetDirectoryName(secondPath)!, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExportTablesToExcel_RemovesExportDirectoryWhenExportFails()
    {
        CreateProductsTable();
        var existingExportDirectories = Directory
            .EnumerateDirectories(Path.GetTempPath(), "SqliteMcp-*")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> newExportDirectories = [];

        try
        {
            var result = await _tools.ExportTablesToExcel("Products,MissingTable", CancellationToken.None);

            Assert.StartsWith("Error exporting tables to Excel:", result);
            newExportDirectories = Directory
                .EnumerateDirectories(Path.GetTempPath(), "SqliteMcp-*")
                .Where(path => !existingExportDirectories.Contains(path))
                .ToList();
            Assert.Empty(newExportDirectories);
        }
        finally
        {
            foreach (var directory in newExportDirectories)
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }
    }

    // ── ExecutionPlan ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExecutionPlan_SelectQuery_ReturnsTextAndStructuredPlan()
    {
        CreateProductsTable();

        var result = await _tools.ExecutionPlan("SELECT * FROM Products WHERE Id = 1", CancellationToken.None);

        Assert.NotNull(result.Content);
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(result.Content[0]).Text;
        Assert.Contains("Execution plan for query: SELECT * FROM Products WHERE Id = 1", text);

        Assert.True(result.StructuredContent.HasValue);
        var structured = result.StructuredContent.Value;
        Assert.Equal("SELECT * FROM Products WHERE Id = 1", structured.GetProperty("query").GetString());
        var planRows = structured.GetProperty("results").EnumerateArray().ToList();
        Assert.NotEmpty(planRows);
        Assert.Contains("Products", planRows[0].GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ExecutionPlan_InvalidQuery_ReturnsErrorContent()
    {
        var result = await _tools.ExecutionPlan("SELECT * FROM MissingTable", CancellationToken.None);

        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(result.Content[0]).Text;
        Assert.StartsWith("Error getting execution plan:", text);
        Assert.False(result.StructuredContent.HasValue);
    }
}
