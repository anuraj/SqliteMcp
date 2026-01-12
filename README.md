# SqliteMcp

A .NET Model Context Protocol (MCP) server for SQLite database operations. Enables AI agents and LLMs to interact with SQLite databases through a standardized MCP interface.

![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/anuraj/SqliteMcp/ci.yml?branch=main)
![NuGet Version](https://img.shields.io/nuget/v/SqliteMcp)
![GitHub License](https://img.shields.io/github/license/anuraj/SqliteMcp)

## Overview

SqliteMcp is a Model Context Protocol server that exposes SQLite database operations as tools accessible to AI models like GitHub Copilot. It provides a comprehensive set of operations for database introspection, data retrieval, insertion, updates, deletion, and raw SQL query execution.

## Tech Stack

- **.NET 10.0**: Latest .NET runtime
- **Microsoft.Data.Sqlite**: Native SQLite provider for .NET
- **ModelContextProtocol**: MCP server implementation
- **Microsoft.Extensions.Hosting**: Host builder and dependency injection container

## Project Structure

```
SqliteMcp/
├── Program.cs              # Application entry point, MCP server initialization
├── Tools.cs                # MCP tool definitions for database operations
├── SqliteMcp.csproj        # Project configuration
├── SqliteMcp.sln           # Visual Studio solution file
├── LICENSE                 # Project license
└── README.md               # This file
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

#### For Visual Studio Code

1. Clone the repository:
   ```bash
   git clone https://github.com/anuraj/SqliteMcp.git
   cd SqliteMcp
   ```

2. Install .NET 10 runtime (if not already installed).

3. Create or update a `.vscode/mcp.json` file in your VS Code settings folder with the following configuration:

```json
{
  "servers": {
    "sqlite-mcp-server": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "<CLONE_LOCATION>/SqliteMcp"
      ],
      "env": {
        "SQLITE_DB_PATH": "<PATH_TO_YOUR_SQLITE_DATABASE.db>"
      }
    }
  },
  "inputs": []
}
```

4. Replace placeholders:
   - `<CLONE_LOCATION>`: Full path to where you cloned the repository
   - `<PATH_TO_YOUR_SQLITE_DATABASE.db>`: Full path to your SQLite database file

5. Open GitHub Copilot Chat and query your SQLite database using natural language.

#### For Command Line

```bash
cd SqliteMcp
export SQLITE_DB_PATH="/path/to/your/database.db"  # Linux/macOS
set SQLITE_DB_PATH=C:\path\to\your\database.db      # Windows
dotnet run
```

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
Retrieves detailed schema information for a specified table, including column names, data types, constraints, and primary key information.

**Parameters:**
- `tableName` (string): Name of the table to inspect

**Read-only:** Yes  
**Destructive:** No

### Data Manipulation

#### `create_record`
Inserts a new record into the specified table.

**Parameters:**
- `tableName` (string): Target table name
- `columnValues` (Dictionary): Key-value pairs of column names and their values

**Read-only:** No  
**Destructive:** No

#### `read_records`
Retrieves records from a table with optional filtering and pagination.

**Parameters:**
- `tableName` (string): Target table name
- `conditions` (string, optional): WHERE clause conditions for filtering
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
- `conditions` (string): WHERE clause to identify records to update

**Read-only:** No  
**Destructive:** Yes

#### `delete_records`
Deletes records from a table matching specified conditions.

**Parameters:**
- `tableName` (string): Target table name
- `conditions` (string): WHERE clause to identify records to delete

**Read-only:** No  
**Destructive:** Yes

### Advanced Operations

#### `query`
Executes raw SQL queries against the database with optional parameterized values.

**Parameters:**
- `sqlQuery` (string): SQL query to execute
- `parameters` (Dictionary, optional): Parameterized query values

**Read-only:** No  
**Destructive:** Yes

**⚠️ Warning:** This tool executes raw SQL. Ensure proper input validation when used with untrusted input to prevent SQL injection.

## Architecture

### Application Entry Point

The `Program.cs` file configures the MCP server with the following setup:

1. **Database Connection**: Initializes a `SqliteConnection` using the `SQLITE_DB_PATH` environment variable
2. **Dependency Injection**: Registers the connection as a singleton service
3. **MCP Server Configuration**: Sets up MCP with stdio transport and auto-discovers tools from the assembly
4. **Logging**: Configures console logging with output directed to standard error

```csharp
var databasePath = Environment.GetEnvironmentVariable("SQLITE_DB_PATH") ??
    throw new InvalidOperationException("Environment variable SQLITE_DB_PATH is not set.");

builder.Services.AddSingleton(new SqliteConnection($"Data Source={databasePath}"));

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
```

### Tool Implementation

The `Tools.cs` class contains all MCP tools:

- **Attribute-based Registration**: Tools are registered using the `[McpServerToolType]` class attribute
- **Method-based Tools**: Each public method represents a callable tool
- **Metadata**: Each tool method is decorated with:
  - `[McpServerTool]`: Marks the method as an MCP tool with metadata
    - `Destructive`: Indicates if the operation modifies database state
    - `ReadOnly`: Marks query-only operations
    - `Name`: The callable tool name
  - `[Description]`: Provides user-friendly documentation

### Error Handling

All tools implement comprehensive error handling:

- Try-catch blocks wrap all database operations
- Exceptions are caught and returned as descriptive error messages
- Database connections are properly disposed using `using` statements
- No exceptions are thrown; instead, errors are communicated as return values

## Building and Packaging

### Building Locally

```bash
dotnet restore
dotnet build
```

### Building as Self-Contained Executable

```bash
dotnet publish -c Release
```

The project is configured to publish as:
- A self-contained single-file executable
- Supports multiple platform targets (Windows, Linux, macOS)

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