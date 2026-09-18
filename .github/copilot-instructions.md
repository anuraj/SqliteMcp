# Copilot instructions for SqliteMcp

## Build, test, and validation

Use the repository's .NET toolchain directly.

- Restore dependencies:
  - `dotnet restore SqliteMcp.slnx`
- Build the solution:
  - `dotnet build SqliteMcp.slnx --no-restore`
- Run the full test suite:
  - `dotnet test Tests/SqliteMcp.Tests/SqliteMcp.Tests.csproj --no-build`
- Run a single test by name:
  - `dotnet test Tests/SqliteMcp.Tests/SqliteMcp.Tests.csproj --filter "FullyQualifiedName~ToolsTests.GetDatabaseInfo_ReturnsFormattedOutput"`
- Run the server locally:
  - `SQLITE_DB_PATH="/path/to/your/database.db" dotnet run --project Src/SqliteMcp.csproj`
  - PowerShell equivalent: `$env:SQLITE_DB_PATH = "C:\path\to\your\database.db"; dotnet run --project Src/SqliteMcp.csproj`

There is no separate lint target in this repo; the effective validation path is restore + build + test. If you change a tool or query path, prefer targeted test execution before running the broader suite.

## Architecture

This repo is a .NET 10 MCP server that exposes SQLite capabilities over stdio.

- `Src/Program.cs` is the application entry point.
  - It reads `SQLITE_DB_PATH` from the environment or host configuration.
  - It builds a `Func<SqliteConnection>` so each tool operation opens and disposes its own SQLite connection.
  - It registers the MCP server with `AddMcpServer()`, `WithStdioServerTransport()`, `WithToolsFromAssembly()`, `WithResourcesFromAssembly()`, `WithTasks()`, and `WithMcpApps()`.
- `Src/Tools/SqliteMcpTools.cs` contains the tool implementation.
  - Tools are discovered via `[McpServerToolType]` and individual methods are marked with `[McpServerTool]`.
  - This file is the main place to add or adjust database operations.
- `Src/UI/exec_plan.html` is copied into the output and exposed as an MCP app UI for the `execution_plan` tool.
- `Tests/SqliteMcp.Tests/` uses xUnit and SQLite in-memory databases with a shared cache to verify behavior without requiring a real on-disk database file.
- Packaging and MCP metadata live in `.mcp/server.json` and the project file (`PackAsTool`, `PackageType=McpServer`).

## Conventions and repository-specific patterns

- Treat table names as untrusted input and validate them before use. The project intentionally blocks SQLite system tables by rejecting names beginning with `sqlite_` and by checking `sqlite_master`.
- Prefer parameterized queries and safe identifier quoting instead of concatenating user input into SQL. `QuoteIdentifier` and `QuoteStringLiteral` are the normal helpers in `SqliteMcpTools`.
- Keep tools side-effecting operations explicit: `Destructive` and `ReadOnly` metadata should match the actual behavior of the tool.
- When changing SQL behavior, make sure the corresponding xUnit test covers both the success path and the validation/error path.
- The application expects a single database path via environment configuration; do not assume a default database exists in the repo.
- For new exported or UI-facing features, keep their registration aligned with the MCP app pattern used by `execution_plan` and the `ui://sqlite-app/execution-plan` resource.

## Typical edit points

- New/changed database tools: `Src/Tools/SqliteMcpTools.cs`
- Server wiring and dependencies: `Src/Program.cs`
- Test coverage for tool behavior: `Tests/SqliteMcp.Tests/ToolsTests.cs`
- Packaging or MCP metadata: `Src/SqliteMcp.csproj`, `.mcp/server.json`

## Release and publishing notes

- CI builds on GitHub Actions using .NET 10 and runs restore, build, and tests before any NuGet publish step.
- The package version comes from `Src/SqliteMcp.csproj` and is uploaded to NuGet only from the `main` branch when the version is new.
