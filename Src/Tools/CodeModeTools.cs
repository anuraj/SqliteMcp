using System.ComponentModel;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace SqliteMcp.Tools;

public class CodeModeTools(SqliteMcpTools dbTools)
{
    public async Task<string> ExecuteCode(
        [Description("C# script body that calls the available database methods and returns a string.")] string code)
    {
        try
        {
            var options = ScriptOptions.Default
                .AddReferences(
                    typeof(SqliteMcpTools).Assembly,
                    typeof(CallToolResult).Assembly,
                    typeof(System.Text.Json.JsonDocument).Assembly)
                .AddImports(
                    "System",
                    "System.Linq",
                    "System.Text.Json",
                    "System.Collections.Generic",
                    "System.Threading.Tasks",
                    "ModelContextProtocol.Protocol");

            var result = await CSharpScript.EvaluateAsync<object>(
                code,
                options,
                globals: dbTools);

            return result?.ToString() ?? "Script executed successfully with no return value.";
        }
        catch (CompilationErrorException ex)
        {
            return $"C# Compilation Error:\n{string.Join("\n", ex.Diagnostics)}";
        }
        catch (Exception ex)
        {
            return $"Runtime Error: {ex.Message}";
        }
    }

    public static McpServerTool CreateTool(SqliteMcpTools dbTools)
    {
        var instance = new CodeModeTools(dbTools);
        var method = typeof(CodeModeTools).GetMethod(nameof(ExecuteCode))!;

        return McpServerTool.Create(
            method,
            instance,
            new McpServerToolCreateOptions
            {
                Name = "execute_code",
                Description = BuildDynamicDescription()
            });
    }

    private static string BuildDynamicDescription()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Executes a C# script against the SQLite database.");
        sb.AppendLine("You have direct access to the following C# methods in global scope:");
        sb.AppendLine();

        var toolMethods = typeof(SqliteMcpTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null);

        foreach (var method in toolMethods)
        {
            var desc = method.GetCustomAttribute<DescriptionAttribute>()?.Description;
            var returnType = FormatTypeName(method.ReturnType);
            var parameters = string.Join(", ", method.GetParameters().Select(FormatParameter));

            if (!string.IsNullOrWhiteSpace(desc))
            {
                sb.AppendLine($"// {desc}");
            }
            sb.AppendLine($"{returnType} {method.Name}({parameters});");
            sb.AppendLine();
        }

        sb.AppendLine("Rules:");
        sb.AppendLine("- Write valid C# top-level statements and end with a `return` statement returning a string.");
        sb.AppendLine("- All async methods return Task<T>, so use `await` when calling them.");
        sb.AppendLine("- Use System.Text.Json (JsonDocument / JsonSerializer) to parse JSON strings returned by tools.");

        return sb.ToString();
    }

    private static string FormatParameter(ParameterInfo p)
    {
        var typeName = FormatTypeName(p.ParameterType);
        var defaultVal = p.HasDefaultValue
            ? $" = {(p.DefaultValue is null ? "null" : p.DefaultValue is string s ? $"\"{s}\"" : p.DefaultValue.ToString()?.ToLowerInvariant())}"
            : "";
        return $"{typeName} {p.Name}{defaultVal}";
    }

    private static string FormatTypeName(Type type)
    {
        if (type == typeof(void)) return "void";
        if (type == typeof(string)) return "string";
        if (type == typeof(int)) return "int";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(object)) return "object";

        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
            return $"{FormatTypeName(underlying)}?";

        if (type.IsGenericType)
        {
            var baseName = type.Name[..type.Name.IndexOf('`')];
            var args = string.Join(", ", type.GetGenericArguments().Select(FormatTypeName));
            return $"{baseName}<{args}>";
        }

        return type.Name;
    }
}