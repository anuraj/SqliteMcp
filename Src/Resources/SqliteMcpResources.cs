using System.ComponentModel;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Server;

namespace SqliteMcp.Resources
{
    [McpServerResourceType]
    public sealed class SqliteMcpResources
    {
        private static readonly string UiDir = Path.Combine(AppContext.BaseDirectory, "ui");

        [McpServerResource(UriTemplate = "ui://sqlite-app/execution-plan", Name = "sqlite-exec-plan-ui", MimeType = McpApps.HtmlMimeType)]
        [McpMeta("ui", """{"csp":{"connectDomains":[]},"prefersBorder":true}""")]
        [Description("Interactive SQLite execution plan UI")]
        public static string GetSqliteExecPlanUi() => File.ReadAllText(Path.Combine(UiDir, "exec_plan.html"));
    }
}