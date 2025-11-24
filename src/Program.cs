using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

namespace HelloWasmApp;

public static partial class Interop
{
    [SupportedOSPlatform("browser")]
    [JSExport]
    public static string ExecutePowerShell(string script)
    {
        try
        {
            var parser = new PowerShellParser();
            var result = parser.Execute(script);
            return result;
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    public static void Main()
    {
        Console.WriteLine("PowerShell Parser Ready.");
    }
}

public class PowerShellParser
{
    private Dictionary<string, object?> _variables = new();

    public string Execute(string script)
    {
        var output = new StringBuilder();

        // Split into lines and process each command
        var lines = script.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(l => l.Trim())
                         .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("#"));

        foreach (var line in lines)
        {
            var result = ExecuteLine(line);
            if (!string.IsNullOrEmpty(result))
            {
                output.AppendLine(result);
            }
        }

        return output.ToString().TrimEnd();
    }

    private string ExecuteLine(string line)
    {
        // Handle Write-Output cmdlet
        var writeOutputMatch = Regex.Match(line, @"^Write-Output\s+(.+)$", RegexOptions.IgnoreCase);
        if (writeOutputMatch.Success)
        {
            return EvaluateExpression(writeOutputMatch.Groups[1].Value);
        }

        // Handle variable assignment: $var = value
        var assignmentMatch = Regex.Match(line, @"^\$(\w+)\s*=\s*(.+)$");
        if (assignmentMatch.Success)
        {
            var varName = assignmentMatch.Groups[1].Value;
            var value = EvaluateExpression(assignmentMatch.Groups[2].Value);
            _variables[varName] = value;
            return string.Empty;
        }

        // Handle variable reference: $var
        if (line.StartsWith("$"))
        {
            var varName = line.Substring(1);
            if (_variables.TryGetValue(varName, out var value))
            {
                return value?.ToString() ?? string.Empty;
            }
            return string.Empty;
        }

        return $"Unknown command: {line}";
    }

    private string EvaluateExpression(string expr)
    {
        expr = expr.Trim();

        // Handle string literals with single quotes
        if (expr.StartsWith("'") && expr.EndsWith("'"))
        {
            return expr.Substring(1, expr.Length - 2);
        }

        // Handle string literals with double quotes (with variable expansion)
        if (expr.StartsWith("\"") && expr.EndsWith("\""))
        {
            var content = expr.Substring(1, expr.Length - 2);
            return ExpandVariables(content);
        }

        // Handle variable references
        if (expr.StartsWith("$"))
        {
            var varName = expr.Substring(1);
            if (_variables.TryGetValue(varName, out var value))
            {
                return value?.ToString() ?? string.Empty;
            }
        }

        // Handle numbers
        if (int.TryParse(expr, out var intValue))
        {
            return intValue.ToString();
        }

        return expr;
    }

    private string ExpandVariables(string text)
    {
        return Regex.Replace(text, @"\$(\w+)", match =>
        {
            var varName = match.Groups[1].Value;
            if (_variables.TryGetValue(varName, out var value))
            {
                return value?.ToString() ?? string.Empty;
            }
            return match.Value;
        });
    }
}
