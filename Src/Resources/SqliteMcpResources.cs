using System.ComponentModel;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Server;

namespace SqliteMcp.Resources
{
    [McpServerResourceType]
    public sealed class SqliteMcpResources
    {
        // Matches the "UI" folder casing preserved by the Content item in SqliteMcp.csproj;
        // must stay in sync since Linux file systems are case-sensitive.
        private static readonly string UiDir = Path.Combine(AppContext.BaseDirectory, "UI");

        [McpServerResource(UriTemplate = "ui://sqlite-app/execution-plan", Name = "sqlite-exec-plan-ui", MimeType = McpApps.HtmlMimeType)]
        [McpMeta("ui", """{"csp":{"connectDomains":[]},"prefersBorder":true}""")]
        [Description("Interactive SQLite execution plan UI")]
        public static string GetSqliteExecPlanUi() => File.ReadAllText(Path.Combine(UiDir, "exec_plan.html"));

        [McpServerResource(UriTemplate = "ui://sqlite-app/visualize", Name = "sqlite-visualize-ui", MimeType = McpApps.HtmlMimeType)]
        [McpMeta("ui", """{"csp":{"connectDomains":[]},"prefersBorder":true}""")]
        [Description("Interactive SQLite Query visualization UI")]
        public static string GetSqliteVisualizeUi() => File.ReadAllText(Path.Combine(UiDir, "visualize.html"));
    }
}