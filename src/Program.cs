using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace HelloWasmApp;

public static partial class Interop
{
    [SupportedOSPlatform("browser")]
    [JSExport]
    public static string GetMessage() => "Hello World from C#!";

    public static void Main()
    {
        System.Console.WriteLine("C# Main is running...");
    }
}
