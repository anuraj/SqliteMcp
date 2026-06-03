using System.Text.Json;
using Microsoft.Data.Sqlite;
using SqliteMcp;
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
    private readonly SqliteConnection _connection;
    private readonly Tools _tools;

    public ToolsTests()
    {
        var connStr = $"Data Source={_dbName};Mode=Memory;Cache=Shared";
        _keeper = new SqliteConnection(connStr);
        _keeper.Open();
        _connection = new SqliteConnection(connStr);
        _tools = new Tools(_connection);
    }

    public void Dispose()
    {
        _connection.Dispose();
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

    private void SeedProducts() =>
        Execute("INSERT INTO Products (Name, Price) VALUES ('Apple', 1.99), ('Banana', 0.99), ('Cherry', 3.49)");

    // ── GetDatabaseInfo ─────────────────────────────────────────────────────

    [Fact]
    public void GetDatabaseInfo_ReturnsFormattedOutput()
    {
        CreateProductsTable();
        var result = _tools.GetDatabaseInfo();
        Assert.Contains("Database Path:", result);
        Assert.Contains("Exists:", result);
        Assert.Contains("Size (bytes):", result);
        Assert.Contains("Table Count:", result);
    }

    [Fact]
    public void GetDatabaseInfo_ReturnsCorrectTableCount()
    {
        CreateProductsTable();
        Execute("CREATE TABLE IF NOT EXISTS Orders (Id INTEGER PRIMARY KEY)");
        var result = _tools.GetDatabaseInfo();
        Assert.Contains("Table Count: 2", result);
    }

    // ── ListTables ──────────────────────────────────────────────────────────

    [Fact]
    public void ListTables_EmptyDatabase_ReturnsNoTablesMessage()
    {
        var result = _tools.ListTables();
        Assert.Equal("No tables found in the database.", result);
    }

    [Fact]
    public void ListTables_WithTables_ReturnsTableNames()
    {
        CreateProductsTable();
        Execute("CREATE TABLE IF NOT EXISTS Orders (Id INTEGER PRIMARY KEY)");
        var result = _tools.ListTables();
        Assert.Contains("Products", result);
        Assert.Contains("Orders", result);
    }

    [Fact]
    public void ListTables_ExcludesSystemTables()
    {
        CreateProductsTable();
        var result = _tools.ListTables();
        Assert.DoesNotContain("sqlite_", result);
    }

    // ── GetTableSchema ──────────────────────────────────────────────────────

    [Fact]
    public void GetTableSchema_ValidTable_ReturnsSchemaWithColumns()
    {
        CreateProductsTable();
        var result = _tools.GetTableSchema("Products");
        Assert.Contains("Schema for table 'Products':", result);
        Assert.Contains("Name", result);
        Assert.Contains("Price", result);
    }

    [Fact]
    public void GetTableSchema_NonExistentTable_ReturnsError()
    {
        var result = _tools.GetTableSchema("NonExistent");
        Assert.Contains("does not exist", result);
    }

    // ── CreateRecord ────────────────────────────────────────────────────────

    [Fact]
    public void CreateRecord_ValidData_ReturnsSuccessMessage()
    {
        CreateProductsTable();
        var result = _tools.CreateRecord("Products", new Dictionary<string, object>
        {
            ["Name"] = "Mango",
            ["Price"] = 2.49
        });
        Assert.Contains("successfully created", result);
        Assert.Contains("Products", result);
    }

    [Fact]
    public void CreateRecord_NonExistentTable_ReturnsError()
    {
        var result = _tools.CreateRecord("Ghost", new Dictionary<string, object>
        {
            ["Name"] = "X"
        });
        Assert.Contains("Error", result);
    }

    // ── ReadRecords ─────────────────────────────────────────────────────────

    [Fact]
    public void ReadRecords_NoConditions_ReturnsAllRows()
    {
        CreateProductsTable();
        SeedProducts();
        var result = _tools.ReadRecords("Products");
        var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(result);
        Assert.NotNull(rows);
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void ReadRecords_WithConditions_ReturnsFilteredRows()
    {
        CreateProductsTable();
        SeedProducts();
        var result = _tools.ReadRecords("Products", new Dictionary<string, object> { ["Name"] = "Apple" });
        var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(result);
        Assert.NotNull(rows);
        Assert.Single(rows);
        Assert.Equal("Apple", rows[0]["Name"].GetString());
    }

    [Fact]
    public void ReadRecords_WithLimit_RespectsLimit()
    {
        CreateProductsTable();
        SeedProducts();
        var result = _tools.ReadRecords("Products", limit: 2);
        var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(result);
        Assert.NotNull(rows);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void ReadRecords_WithOffset_RespectsOffset()
    {
        CreateProductsTable();
        SeedProducts();
        var all = _tools.ReadRecords("Products");
        var allRows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(all)!;

        var result = _tools.ReadRecords("Products", offset: 1);
        var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(result);
        Assert.NotNull(rows);
        Assert.Equal(2, rows.Count);
        Assert.Equal(allRows[1]["Name"].GetString(), rows[0]["Name"].GetString());
    }

    [Fact]
    public void ReadRecords_NonExistentTable_ReturnsError()
    {
        var result = _tools.ReadRecords("Ghost");
        Assert.Contains("Error", result);
    }

    // ── UpdateRecords ───────────────────────────────────────────────────────

    [Fact]
    public void UpdateRecords_MatchingRows_ReturnsUpdatedCount()
    {
        CreateProductsTable();
        SeedProducts();
        var result = _tools.UpdateRecords(
            "Products",
            new Dictionary<string, object> { ["Price"] = 9.99 },
            new Dictionary<string, object> { ["Name"] = "Apple" });
        Assert.Contains("1 record(s) successfully updated", result);
    }

    [Fact]
    public void UpdateRecords_NoMatchingRows_ReturnsNoRecordsMessage()
    {
        CreateProductsTable();
        SeedProducts();
        var result = _tools.UpdateRecords(
            "Products",
            new Dictionary<string, object> { ["Price"] = 0.0 },
            new Dictionary<string, object> { ["Name"] = "Durian" });
        Assert.Contains("No records updated", result);
    }

    [Fact]
    public void UpdateRecords_NonExistentTable_ReturnsError()
    {
        var result = _tools.UpdateRecords(
            "Ghost",
            new Dictionary<string, object> { ["Name"] = "X" },
            new Dictionary<string, object> { ["Id"] = 1 });
        Assert.Contains("Error", result);
    }

    // ── DeleteRecords ───────────────────────────────────────────────────────

    [Fact]
    public void DeleteRecords_MatchingRows_ReturnsDeletedCount()
    {
        CreateProductsTable();
        SeedProducts();
        var result = _tools.DeleteRecords(
            "Products",
            new Dictionary<string, object> { ["Name"] = "Banana" });
        Assert.Contains("1 record(s) successfully deleted", result);
    }

    [Fact]
    public void DeleteRecords_NoMatchingRows_ReturnsNoRecordsMessage()
    {
        CreateProductsTable();
        SeedProducts();
        var result = _tools.DeleteRecords(
            "Products",
            new Dictionary<string, object> { ["Name"] = "Papaya" });
        Assert.Contains("No records deleted", result);
    }

    [Fact]
    public void DeleteRecords_NonExistentTable_ReturnsError()
    {
        var result = _tools.DeleteRecords(
            "Ghost",
            new Dictionary<string, object> { ["Id"] = 1 });
        Assert.Contains("Error", result);
    }

    // ── ExecuteQuery ────────────────────────────────────────────────────────

    [Fact]
    public void ExecuteQuery_SelectWithResults_ReturnsJsonArray()
    {
        CreateProductsTable();
        SeedProducts();
        var result = _tools.ExecuteQuery("SELECT * FROM Products");
        var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(result);
        Assert.NotNull(rows);
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void ExecuteQuery_SelectWithNoResults_ReturnsNoResultsMessage()
    {
        CreateProductsTable();
        var result = _tools.ExecuteQuery("SELECT * FROM Products WHERE Id = -1");
        Assert.Equal("Query executed successfully with no results.", result);
    }

    [Fact]
    public void ExecuteQuery_NonSelectStatement_ReturnsRowsAffected()
    {
        CreateProductsTable();
        SeedProducts();
        var result = _tools.ExecuteQuery("DELETE FROM Products WHERE Name = 'Apple'");
        Assert.Contains("row(s) affected", result);
    }

    [Fact]
    public void ExecuteQuery_SelectWithParameters_SubstitutesCorrectly()
    {
        CreateProductsTable();
        SeedProducts();
        var result = _tools.ExecuteQuery(
            "SELECT * FROM Products WHERE Name = @name",
            new Dictionary<string, object> { ["name"] = "Cherry" });
        var rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(result);
        Assert.NotNull(rows);
        Assert.Single(rows);
        Assert.Equal("Cherry", rows[0]["Name"].GetString());
    }
}
