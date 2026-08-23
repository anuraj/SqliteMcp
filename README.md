# SqliteMcp

A .NET Model Context Protocol (MCP) server for inspecting and managing SQLite databases from MCP clients such as GitHub Copilot.

![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/anuraj/SqliteMcp/ci.yml?branch=main)
[![NuGet Version](https://img.shields.io/nuget/v/SqliteMcp)](https://www.nuget.org/packages/SqliteMcp)
![GitHub License](https://img.shields.io/github/license/anuraj/SqliteMcp)

## Overview

SqliteMcp communicates over stdio and reads the database path from `SQLITE_DB_PATH`. It exposes database introspection, schema inspection, CRUD operations, raw SQL execution, and an interactive execution-plan view.

## Tech Stack

- **.NET 10.0**: Latest .NET runtime
- **Microsoft.Data.Sqlite**: Native SQLite provider for .NET
- **ModelContextProtocol**: MCP server implementation
- **Microsoft.Extensions.Hosting**: Host builder and dependency injection container

## Project Structure

```
SqliteMcp/
├── Src/
│   ├── Program.cs                    # Host and MCP server configuration
│   ├── SqliteMcp.csproj              # Executable and packaging configuration
│   ├── Tools/SqliteMcpTools.cs       # SQLite MCP tools
│   ├── Resources/SqliteMcpResources.cs # MCP Apps resource registration
│   └── UI/exec_plan.html             # Execution-plan UI
├── Tests/SqliteMcp.Tests/            # Tool tests
├── database/                          # Sample or local database files
├── mcpb/manifest.json                # MCP bundle manifest
├── .mcp/server.json                  # NuGet MCP server metadata
├── SqliteMcp.slnx                    # Solution file
└── README.md
```

## Getting Started

### Prerequisites

- .NET 10.0 SDK or later
- A SQLite database file

### Installation

1. Clone the repository:
   ```bash
  git clone https://github.com/anuraj/SqliteMcp.git
   cd SqliteMcp
   ```

2. Restore dependencies and build:
   ```bash
   dotnet restore
   dotnet build
   ```

### Running the Server

#### For Visual Studio Code

Create or update `.vscode/mcp.json` in the workspace that uses the server:

```json
{
  "servers": {
    "sqlite-mcp-server": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "<CLONE_LOCATION>/SqliteMcp/Src/SqliteMcp.csproj"
      ],
      "env": {
        "SQLITE_DB_PATH": "<PATH_TO_YOUR_SQLITE_DATABASE.db>"
      }
    }
  },
  "inputs": []
}
```

Replace the placeholders:
   - `<CLONE_LOCATION>`: Full path to where you cloned the repository
   - `<PATH_TO_YOUR_SQLITE_DATABASE.db>`: Full path to your SQLite database file

Then open GitHub Copilot Chat and query your SQLite database using natural language.

#### For Command Line

```bash
cd SqliteMcp
# Linux/macOS
SQLITE_DB_PATH="/path/to/your/database.db" dotnet run --project Src/SqliteMcp.csproj

# Windows PowerShell
$env:SQLITE_DB_PATH = "C:\path\to\your\database.db"
dotnet run --project Src/SqliteMcp.csproj
```

The server also accepts `SQLITE_DB_PATH` from host configuration, but an environment variable is the usual choice for stdio clients.

## Available Tools

SqliteMcp exposes the following tools through the MCP interface:

### Database Introspection

#### `db_info`
Returns comprehensive database metadata including file path, existence status, file size in bytes, and table count.

**Read-only:** Yes  
**Destructive:** No

#### `list_tables`
Lists all user-defined tables in the database (excludes SQLite system tables).

**Read-only:** Yes  
**Destructive:** No

### Schema Operations

#### `get_table_schema`
Retrieves column names, data types, not-null flags, default values, and primary-key information for a specified table.

**Parameters:**
- `tableName` (string): Name of the table to inspect

**Read-only:** Yes  
**Destructive:** No

### Data Manipulation

#### `create_record`
Inserts a new record into the specified table.

**Parameters:**
- `tableName` (string): Target table name
- `columnValues` (Dictionary): Key-value pairs of column names and values

**Read-only:** No  
**Destructive:** Yes

#### `read_records`
Retrieves records from a table with optional equality filtering and pagination.

**Parameters:**
- `tableName` (string): Target table name
- `conditions` (Dictionary, optional): Column-value pairs combined with `AND`
- `limit` (int, default: 100): Maximum number of records to return
- `offset` (int, default: 0): Number of records to skip (for pagination)

**Returns:** JSON array of record objects

**Read-only:** Yes  
**Destructive:** No

#### `update_records`
Updates existing records in a table matching specified conditions.

**Parameters:**
- `tableName` (string): Target table name
- `columnValues` (Dictionary): Columns to update with their new values
- `conditions` (Dictionary): Column-value pairs used to identify records; pairs are combined with `AND`

**Read-only:** No  
**Destructive:** Yes

#### `delete_records`
Deletes records from a table matching specified conditions.

**Parameters:**
- `tableName` (string): Target table name
- `conditions` (Dictionary): Column-value pairs used to identify records; pairs are combined with `AND`

**Read-only:** No  
**Destructive:** Yes

### Advanced Operations

#### `execute_query`
Executes a raw SQL query against the database with optional parameter values. `SELECT` statements return a JSON array of rows; other statements return the number of affected rows.

**Parameters:**
- `sqlQuery` (string): SQL query to execute
- `parameters` (Dictionary, optional): Values for named SQL parameters, referenced as `@name`

**Read-only:** No  
**Destructive:** Yes

**Warning:** This tool executes raw SQL. Use it only with trusted input or carefully parameterized values.

#### `execution_plan`
Runs `EXPLAIN QUERY PLAN` for a SQL query and returns a `CallToolResult` containing a text summary and structured data with the original `query`, result `columns`, and plan `results`. MCP clients that support MCP Apps can render the associated interactive UI.

**Parameters:**
- `sqlQuery` (string): SQL query to explain

**Read-only:** Yes in intent; the query is prefixed with `EXPLAIN QUERY PLAN`.

For invalid SQL, the tool returns an error in its text content and omits structured plan data.

## Available Resources

### `sqlite-exec-plan-ui`

- **URI:** `ui://sqlite-app/execution-plan`
- **MIME type:** `text/html`
- **Description:** Interactive SQLite execution-plan UI associated with `execution_plan`

The HTML resource is copied to the output directory during build and registered through MCP Apps.

The resource is associated with `execution_plan` through MCP Apps metadata and is served from the same `ui://sqlite-app/execution-plan` URI.

## Architecture

### Application Entry Point

The `Program.cs` file configures the MCP server with the following setup:

1. **Database Connection**: Reads `SQLITE_DB_PATH` from the environment or host configuration.
2. **Connection Factory**: Registers a factory so each tool operation opens and disposes its own SQLite connection.
3. **MCP Server Configuration**: Enables stdio transport, discovers tools and resources from the assembly, and enables MCP Apps.
4. **Logging**: Configures console logging with output directed to standard error.

```csharp
var databasePath = Environment.GetEnvironmentVariable("SQLITE_DB_PATH") ??
  builder.Configuration["SQLITE_DB_PATH"] ??
  throw new InvalidOperationException("Environment variable SQLITE_DB_PATH is not set.");

builder.Services.AddSingleton<Func<SqliteConnection>>(_ =>
  () => new SqliteConnection($"Data Source={databasePath}"));

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly()
    .WithMcpApps();
```

### Tool Implementation

The `Src/Tools/SqliteMcpTools.cs` class contains all MCP tools:

- **Attribute-based Registration**: Tools are registered using the `[McpServerToolType]` class attribute
- **Method-based Tools**: Public methods represent callable tools
- **Metadata**: Each tool method is decorated with:
  - `[McpServerTool]`: Marks the method as an MCP tool with metadata
    - `Destructive`: Indicates if the operation modifies database state
    - `ReadOnly`: Marks query-only operations
    - `Name`: The callable tool name
  - `[Description]`: Provides user-friendly documentation

### Safety and Error Handling

- Table names are validated against `sqlite_master`; SQLite system tables cannot be accessed.
- Identifiers are quoted and values are passed as SQLite parameters where applicable.
- `read_records` rejects negative `limit` and `offset` values.
- Database connections are opened per operation and disposed after use.
- Tool failures are returned as descriptive error messages.

## Building and Packaging

### Building Locally

```bash
dotnet restore
dotnet build
dotnet test
```

The test project covers database introspection, schema inspection, CRUD operations, SQL execution, execution-plan results, and the bundled HTML resource.

### Building as Self-Contained Executable

```bash
dotnet publish -c Release
```

The project is configured to publish a self-contained, single-file executable. The executable and UI resource are placed under the target framework's publish directory.

The repository also includes `.mcp/server.json` for NuGet MCP server metadata and `mcpb/manifest.json` for the packaged Windows binary configuration. Both require a `SQLITE_DB_PATH` file setting.

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Implement changes with proper error handling and documentation
4. Test with various database schemas
5. Commit your changes (`git commit -m 'Add some amazing feature'`)
6. Push to the branch (`git push origin feature/amazing-feature`)
7. Open a Pull Request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.