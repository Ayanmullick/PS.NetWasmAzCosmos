using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace HelloWasmApp;

public static partial class Interop
{
    [SupportedOSPlatform("browser")]
    [JSExport]
    public static async Task<string> ExecuteCompiledScriptAsync()
    {
        // This will be replaced by build-time generated code
        return await CompiledPowerShell.ExecuteAsync();
    }

    public static void Main()
    {
        Console.WriteLine("Pre-compiled PowerShell Ready.");
    }
}
