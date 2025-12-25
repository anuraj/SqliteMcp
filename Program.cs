using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

var databasePath = Environment.GetEnvironmentVariable("SQLITE_DB_PATH") ??
    throw new InvalidOperationException("Environment variable SQLITE_DB_PATH is not set.");

builder.Services.AddSingleton(new SqliteConnection($"Data Source={databasePath}"));

builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
