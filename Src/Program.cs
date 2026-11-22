using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;
using SqliteMcp.Tools;

var builder = Host.CreateApplicationBuilder(args);

var isCodeMode = args.Contains("--code-mode", StringComparer.OrdinalIgnoreCase)
    || string.Equals(Environment.GetEnvironmentVariable("MCP_CODE_MODE"), "true",
        StringComparison.OrdinalIgnoreCase);

var databasePath = Environment.GetEnvironmentVariable("SQLITE_DB_PATH") ??
    builder.Configuration["SQLITE_DB_PATH"] ??
    throw new InvalidOperationException("Environment variable SQLITE_DB_PATH is not set.");

var connectionString = $"Data Source={databasePath}";
builder.Services.AddSingleton<Func<SqliteConnection>>(_ => () => new SqliteConnection(connectionString));

var store = new InMemoryMcpTaskStore { DefaultPollIntervalMs = 250 };

builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddTransient<SqliteMcpTools>();

var mcpBuilder = builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = "Sqlite MCP Server",
            Version = "1.1.0"
        };
        options.Capabilities = new ServerCapabilities
        {
            Tools = new ToolsCapability(),
            Resources = new ResourcesCapability(),
        };
    })
    .WithStdioServerTransport();

if (isCodeMode)
{
    builder.Services.AddSingleton(sp =>
        CodeModeTools.CreateTool(sp.GetRequiredService<SqliteMcpTools>()));
}
else
{
    mcpBuilder
        .WithToolsFromAssembly()
        .WithResourcesFromAssembly()
        .WithTasks(store)
        .WithMcpApps();

}

await builder.Build().RunAsync();
