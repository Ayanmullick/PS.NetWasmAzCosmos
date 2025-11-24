# Build-time PowerShell to C# compiler
# This script reads Hello.ps1 and generates C# code

$scriptPath = "src/wwwroot/Hello.ps1"
$outputPath = "src/CompiledPowerShell.g.cs"

Write-Host "Compiling PowerShell script to C#..."

# Read the PowerShell script
$psCode = Get-Content $scriptPath -Raw

# Simple compilation: convert Write-Output 'text' to C# return "text";
$csharpCode = @"
// Auto-generated from Hello.ps1 at build time
namespace HelloWasmApp;

public static class CompiledPowerShell
{
    public static string Execute()
    {
"@

# Parse the script (very simplified)
$lines = $psCode -split "`n"
foreach ($line in $lines) {
    $line = $line.Trim()

    # Skip comments and empty lines
    if ($line -match '^#' -or [string]::IsNullOrWhiteSpace($line)) {
        continue
    }

    # Handle Write-Output
    if ($line -match "Write-Output\s+'([^']+)'") {
        $message = $matches[1]
        $csharpCode += "`n        return `"$message`";"
    }
    elseif ($line -match 'Write-Output\s+"([^"]+)"') {
        $message = $matches[1]
        $csharpCode += "`n        return `"$message`";"
    }
}

$csharpCode += @"

    }
}
"@

# Write the generated C# code
$csharpCode | Out-File -FilePath $outputPath -Encoding UTF8
Write-Host "Generated: $outputPath"
