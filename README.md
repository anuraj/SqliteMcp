# SqliteMcp

A .NET Model Context Protocol (MCP) server for SQLite database operations. Enables AI agents and LLMs to interact with SQLite databases through a standardized MCP interface.

## Tech Stack

- **.NET 10.0**: Latest .NET runtime
- **Microsoft.Data.Sqlite**: Native SQLite provider for .NET
- **ModelContextProtocol**: MCP server implementation
- **Microsoft.Extensions.Hosting**: Host builder and DI container

## Project Structure

```
SqliteMcp/
├── Program.cs          # Application entry point, MCP server setup
├── Tools.cs            # MCP tool definitions for database operations
├── SqliteMcp.csproj    # Project configuration
└── README.md           # This file
```

## Getting Started

### Prerequisites

- .NET 10.0 SDK or later
- A SQLite database file

### Installation

1. Clone the repository:
   ```bash
   git clone <repository-url>
   cd SqliteMcp
   ```

2. Restore dependencies:
   ```bash
   dotnet restore
   ```

3. Build the project:
   ```bash
   dotnet build
   ```

### Running the Server

#### For VS Code

1. Clone the repo.
2. Install .NET 10 runtime.
3. Open VS Code, create a `mcp.json` file under `.vscode` folder if not available.
4. Add the following code - You need to modify the placeholders.

```json
{
	"servers": {
		"sqlite-mcp-server": {
			"type": "stdio",
			"command": "dotnet",
			"args": [
				"run",
				"--project",
				"<CLONE LOCATION>"
			],
			"env": {
				"SQLITE_DB_PATH": "<SQLITE FILENAME WITH PATH>"
			}
		}
	},
	"inputs": []
}
```
5. Now Open GitHub copilot chat and ask about the Sqlite database.

## Available Tools

### Database Introspection

#### `db_info`
Returns database metadata including file path, existence status, file size in bytes, and table count.

```
{
  "database_path": "/path/to/database.db",
  "exists": true,
  "size_bytes": 4096,
  "table_count": 3
}
```

#### `list_tables`
Lists all user-defined tables (excludes SQLite system tables).

### Schema Operations

#### `get_table_schema`
Retrieves detailed schema information for a specified table, including column definitions, types, constraints, and primary key information.

**Parameters:**
- `tableName` (string): Name of the table

### Data Operations

#### `create_record`
Inserts a new record into the specified table.

**Parameters:**
- `tableName` (string): Target table name
- `columnValues` (Dictionary<string, object>): Column names and their values

#### `read_records`
Retrieves records from a table with optional filtering and pagination.

**Parameters:**
- `tableName` (string): Target table name
- `conditions` (string, optional): WHERE clause conditions
- `limit` (int, default: 100): Maximum number of records to return
- `offset` (int, default: 0): Number of records to skip for pagination

**Returns:** JSON array of records

#### `update_records`
Updates existing records matching specified conditions.

**Parameters:**
- `tableName` (string): Target table name
- `columnValues` (Dictionary<string, object>): Columns and new values
- `conditions` (string): WHERE clause to identify records to update

#### `delete_records`
Deletes records matching specified conditions.

**Parameters:**
- `tableName` (string): Target table name
- `conditions` (string): WHERE clause to identify records to delete

### Advanced Operations

#### `query`
Executes raw SQL queries with optional parameterized values.

**Parameters:**
- `sqlQuery` (string): SQL query to execute
- `parameters` (Dictionary<string, object>, optional): Parameterized query values

**Warning:** This tool executes raw SQL. Ensure proper input validation when used with untrusted input.

## Architecture

### Dependency Injection

The application uses .NET's built-in DI container:

```csharp
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
```

The `SqliteConnection` is registered as a singleton, initialized with the database path from the `SQLITE_DB_PATH` environment variable.

### Tool Registration

Tools are registered via the `[McpServerToolType]` attribute on the `Tools` class. Each method represents a callable tool, with metadata provided through attributes:

- `[McpServerTool]`: Registers a method as an MCP tool
  - `Destructive`: Indicates if the operation modifies data
  - `ReadOnly`: Marks query-only operations
  - `Name`: The tool's callable name

- `[Description]`: Provides documentation for the tool

### Error Handling

All tools implement try-catch blocks returning error messages instead of throwing exceptions, ensuring graceful degradation when database operations fail.

## Development

### Building

```bash
dotnet build
```

## Contributing

1. Fork the repository
2. Create a feature branch
3. Implement changes with proper error handling
4. Test with various database schemas
5. Submit a pull request

## License

See [LICENSE](LICENSE) file for details.