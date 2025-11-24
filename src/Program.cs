using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetStandard = Basic.Reference.Assemblies.NetStandard20;

namespace HelloWasmApp;

public static partial class Interop
{
    [SupportedOSPlatform("browser")]
    [JSExport]
    public static string CompileAndRun(string sourceCode)
    {
        try
        {
            var references = NetStandard.References.All;

            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

            var compilation = CSharpCompilation.Create(
                "DynamicAssembly",
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, concurrentBuild: false));

            using var ms = new MemoryStream();
            var result = compilation.Emit(ms);

            if (!result.Success)
            {
                var failures = result.Diagnostics
                    .Where(diagnostic => diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error);

                return "Compilation Failed:\n" + string.Join("\n", failures.Select(d => d.ToString()));
            }

            ms.Seek(0, SeekOrigin.Begin);
            var assembly = Assembly.Load(ms.ToArray());

            var type = assembly.GetType("HelloWorld");
            if (type == null) return "Could not find type 'HelloWorld'";

            var method = type.GetMethod("GetMessage");
            if (method == null) return "Could not find method 'GetMessage'";

            var message = method.Invoke(null, null) as string;
            return message ?? "Method returned null";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}\n{ex.StackTrace}";
        }
    }

    public static void Main()
    {
        Console.WriteLine("Compiler Host Ready.");
    }
}
