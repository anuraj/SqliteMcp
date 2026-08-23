using Xunit;

namespace SqliteMcp.Tests;

public class ResourcesTests
{
    [Fact]
    public void GetSqliteExecPlanUi_ReturnsExecutionPlanHtmlResource()
    {
        var html = Resources.SqliteMcpResources.GetSqliteExecPlanUi();

        Assert.Contains("<!DOCTYPE html>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<title>Sqlite Execution Plan</title>", html);
        Assert.Contains("id=\"resultContainer\"", html);
    }
}