using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace SqliteMcp.Tools;

public sealed partial class SqliteMcpTools
{
    private readonly JsonSerializerOptions jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool(Destructive = false, ReadOnly = true, Name = "execution_plan")]
    [McpMeta("ui", JsonValue = """{ "resourceUri": "ui://sqlite-app/execution-plan" }""")]
    [Description("Get the execution plan for a SQL query.")]
    public async Task<CallToolResult> ExecutionPlan([Description("SQL query to get the execution plan for")] string sqlQuery, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = CreateOpenConnection();

            using var command = connection.CreateCommand();
            command.CommandText = $"EXPLAIN QUERY PLAN {sqlQuery}";

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var results = new List<Dictionary<string, object>>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.GetValue(i);
                }
                results.Add(row);
            }

            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"Execution plan for query: {sqlQuery}" }],
                StructuredContent = JsonSerializer.SerializeToElement(new
                {
                    query = sqlQuery,
                    columns = results.FirstOrDefault()?.Keys ?? default,
                    results
                })
            };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"Error getting execution plan: {ex.Message}" }],
            };
        }
    }

    [McpServerTool(Destructive = false, ReadOnly = true, Name = "visualize")]
    [McpMeta("ui", JsonValue = """{ "resourceUri": "ui://sqlite-app/visualize" }""")]
    [Description("Get the Visualization for a SQL query.")]
    public async Task<CallToolResult> Visualize([Description("SQL query to get the query visualization for")] string sqlQuery,
        [Description("Chart type for the visualization, supported options are Line, Bar, Area, Donut, Pie, and Scatter only.")] string chartType,
        CancellationToken cancellationToken)
    {
        try
        {
            using var connection = CreateOpenConnection();

            using var command = connection.CreateCommand();
            command.CommandText = sqlQuery;

            using var reader = await command.ExecuteReaderAsync(cancellationToken);

            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"Visualization for query: {sqlQuery}" }],
                StructuredContent = JsonSerializer.SerializeToElement(new
                {
                    chartType,
                    data = ToChartData(reader, reader.GetName(0),
                        [.. Enumerable.Range(1, reader.FieldCount - 1).Select(i => reader.GetName(i))])
                }, jsonSerializerOptions)
            };


        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = $"Error getting visualization: {ex.Message}" }],
            };
        }
    }

    public static ChartData ToChartData(
    DbDataReader reader,
    string labelColumn,
    params string[] valueColumns)
    {
        var labels = new List<string>();

        var datasets = valueColumns
            .Select(column => new ChartDataset
            {
                Label = column,
                Values = []
            })
            .ToList();

        while (reader.Read())
        {
            // Label
            labels.Add(
                reader[labelColumn] == DBNull.Value
                    ? string.Empty
                    : reader[labelColumn].ToString()!
            );

            // Values
            for (var i = 0; i < valueColumns.Length; i++)
            {
                var value = reader[valueColumns[i]];

                datasets[i].Values.Add(
                    value == DBNull.Value
                        ? null
                        : Convert.ToDouble(value)
                );
            }
        }

        return new ChartData
        {
            Labels = labels,
            Datasets = datasets
        };
    }
}

public class ChartData
{
    public List<string> Labels { get; set; } = [];
    public List<ChartDataset> Datasets { get; set; } = [];
}

public class ChartDataset
{
    public string Label { get; set; } = string.Empty;
    public List<double?> Values { get; set; } = [];
}