using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace HelloWasmApp;

public static partial class Interop
{
    [SupportedOSPlatform("browser")]
    [JSExport]
    public static string ExecuteCompiledScript()
    {
        // This will be replaced by build-time generated code
        return CompiledPowerShell.Execute();
    }

    public static void Main()
    {
        Console.WriteLine("Pre-compiled PowerShell Ready.");
    }
}
