using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;

var builder = Host.CreateApplicationBuilder(args);

var databasePath = Environment.GetEnvironmentVariable("SQLITE_DB_PATH") ??
    builder.Configuration["SQLITE_DB_PATH"] ??
    throw new InvalidOperationException("Environment variable SQLITE_DB_PATH is not set.");

var connectionString = $"Data Source={databasePath}";
builder.Services.AddSingleton<Func<SqliteConnection>>(_ => () => new SqliteConnection(connectionString));

builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "Sqlite MCP Server", Version = "1.0.0" };
        options.Capabilities = new ServerCapabilities
        {
            Tools = new ToolsCapability(),
            Resources = new ResourcesCapability(),
        };
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly()
    .WithMcpApps();

await builder.Build().RunAsync();
