# Build-time PowerShell to C# compiler
# This script reads inline <script type="pwsh"> blocks in index.html and generates C# code
# that mimics a tiny subset of the PowerShell the project uses (Write-Output and Read-AzCosmosItems).
param(
    [string]$OutputPath
)

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$htmlPath = Join-Path $scriptRoot "src/wwwroot/index.html"
if (-not $OutputPath) {
    # Default outside the project folder to avoid accidental compilation conflicts
    $OutputPath = Join-Path $scriptRoot "build/CompiledPowerShell.g.cs"
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null

Write-Host "Compiling PowerShell script to C#..."

# Read the PowerShell script(s) from <script type="pwsh"> in index.html
$psBlocks = @()
if (Test-Path $htmlPath) {
    $indexHtml = Get-Content $htmlPath -Raw
    $regexOptions = [System.Text.RegularExpressions.RegexOptions]::Singleline -bor `
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
    $matches = [regex]::Matches($indexHtml, '<script\s+type="pwsh"[^>]*>(?<code>.*?)</script>', $regexOptions)
    $psBlocks = $matches | ForEach-Object { $_.Groups['code'].Value.Trim() } | Where-Object { $_ }
}

if (-not $psBlocks -or $psBlocks.Count -eq 0) {
    Write-Warning "No inline PowerShell <script type=""pwsh""> blocks found in index.html."
    $psBlocks = @()
}

function Normalize-ParamValue {
    param(
        [string]$Value,
        [string]$Name,
        [hashtable]$EnvRefs
    )

    $normalized = $Value.Trim().Trim('"').Trim("'")

    if ($normalized -match '^\$env:([A-Za-z_][A-Za-z0-9_]*)$') {
        $envName = $matches[1]
        $EnvRefs[$Name] = $envName
        $envValue = [Environment]::GetEnvironmentVariable($envName)
        if ($envValue) {
            $normalized = $envValue
        }
    }

    return $normalized
}

$csharpCode = @"
// Auto-generated from inline PowerShell (index.html) at build time
#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PsWasmApp;

public static class CompiledPowerShell
{
    private static readonly HttpClient Http = new HttpClient();

    public static async Task<string> ExecuteAsync()
    {
        var outputs = new List<string>();
"@

foreach ($codeBlock in $psBlocks) {
    # Flatten line continuations (trailing backtick) so multi-line commands can be parsed per script block
    $statements = @()
    $buffer = ""
    foreach ($line in ($codeBlock -split "`n")) {
        $trimmed = $line.Trim()

        # Skip comments and empty lines
        if ($trimmed -match '^#' -or [string]::IsNullOrWhiteSpace($trimmed)) {
            continue
        }

        if ($trimmed.EndsWith('`')) {
            $buffer += $trimmed.TrimEnd('`').TrimEnd() + " "
            continue
        }

        $statement = ($buffer + $trimmed).Trim()
        $buffer = ""

        if ($statement) {
            $statements += $statement
        }
    }

    if ($buffer.Trim()) {
        $statements += $buffer.Trim()
    }

    $csharpCode += @"

        {
            var blockOutputs = new List<string>();
"@

    $hashtables = @{}

    foreach ($statement in $statements) {
        $hashtableMatch = [regex]::Match($statement, '^\$(?<name>\w+)\s*=\s*@\{(?<body>.*)\}$')
        if ($hashtableMatch.Success) {
            $htBody = $hashtableMatch.Groups['body'].Value
            $entries = $htBody -split ';'
            $paramMap = @{}
            $envMap = @{}

            foreach ($entry in $entries) {
                $part = $entry.Trim()
                if (-not $part) {
                    continue
                }

                $kvMatch = [regex]::Match($part, '^(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<val>.+)$')
                if (-not $kvMatch.Success) {
                    continue
                }

                $keyName = $kvMatch.Groups['key'].Value.ToLowerInvariant()
                $rawValue = $kvMatch.Groups['val'].Value.Trim()
                $normalized = Normalize-ParamValue -Value $rawValue -Name $keyName -EnvRefs $envMap
                $paramMap[$keyName] = $normalized
            }

            $hashtables[$hashtableMatch.Groups['name'].Value] = [pscustomobject]@{
                Params  = $paramMap
                EnvRefs = $envMap
            }
            continue
        }

        # Parsing statements generated from inline PowerShell
        # Handle Write-Output 'text' or "text" by capturing content within quotes
        if ($statement -match "Write-Output\s+(['""])(.*?)\1") {
            $message = $matches[2] -replace '"', '""' # Use group 2 for content
            $csharpCode += "`n            blockOutputs.Add(`"$message`");"
            continue
        }

        # Handle Read-AzCosmosItems with connection string or account name/key
        if ($statement -match '(?i)^Read-AzCosmosItems\b') {
            $paramRegex = '(?<=^|\s)-(?<name>\w+)\s+(?<value>"[^"]*"|''[^'']*''|[^-\s]\S*)'
            $params = @{}
            $envRefs = @{}
            foreach ($splat in [regex]::Matches($statement, '@(?<name>\w+)')) {
                $splatName = $splat.Groups['name'].Value
                if ($hashtables.ContainsKey($splatName)) {
                    $ht = $hashtables[$splatName]
                    foreach ($key in $ht.Params.Keys) {
                        $params[$key] = $ht.Params[$key]
                    }
                    foreach ($envKey in $ht.EnvRefs.Keys) {
                        $envRefs[$envKey] = $ht.EnvRefs[$envKey]
                    }
                }
            }

            foreach ($m in [regex]::Matches($statement, $paramRegex)) {
                $name = $m.Groups['name'].Value.ToLowerInvariant()
                $raw = $m.Groups['value'].Value.Trim()
                $params[$name] = Normalize-ParamValue -Value $raw -Name $name -EnvRefs $envRefs
            }

            $databaseName = $params['databasename']
            $containerName = $params['containername']
            $accountName = $params['accountname']

            $top = if ($params.ContainsKey('top')) { [int]$params['top'] } else { 1 }
            $query = if ($params.ContainsKey('query')) { $params['query'] } else { "SELECT TOP $top * FROM c" }
            $partitionKey = if ($params.ContainsKey('partitionkey')) { $params['partitionkey'] } else { "" }

            $connectionString = ""
            $isDynamic = $false
            $envVar = ""

            if ($params.ContainsKey('connectionstring')) {
                $connectionString = $params['connectionstring']
            }
            elseif ($accountName -and $params.ContainsKey('accountkey')) {
                $accountKey = $params['accountkey']

                if ($envRefs.ContainsKey('accountkey')) {
                    $envVar = $envRefs['accountkey']
                    $isDynamic = $true
                }
                else {
                    if (-not $accountKey) {
                        $csharpCode += @"

            blockOutputs.Add("Cosmos AccountKey not provided (check environment variable).");
"@
                        continue
                    }
                    if ($accountKey.StartsWith("=")) {
                        $accountKey = $accountKey.TrimStart("=")
                    }
                    $endpoint = "https://$accountName.documents.azure.com:443/"
                    $connectionString = "AccountEndpoint=$endpoint;AccountKey=$accountKey;"
                }
            }

            if (-not $databaseName -or -not $containerName) {
                $csharpCode += @"

            blockOutputs.Add("Cosmos parameters missing.");
"@
                continue
            }

            $query = $query -replace '"', '\"'
            $partitionKey = $partitionKey -replace '"', '""'

            if ($isDynamic) {
                $csharpCode += @"

            string? accountKey = Environment.GetEnvironmentVariable("$envVar");
            if (string.IsNullOrWhiteSpace(accountKey))
            {
                accountKey = BuildSecrets.CosmosKey;
            }
            if (string.IsNullOrWhiteSpace(accountKey))
            {
                blockOutputs.Add("Environment variable $envVar not set.");
            }
            else
            {
                if (accountKey.StartsWith("="))
                {
                    accountKey = accountKey.TrimStart('=');
                }
                string endpoint = "https://$accountName.documents.azure.com:443/";
                string connectionString = $"AccountEndpoint={endpoint};AccountKey={accountKey};";

                blockOutputs.Add(await ReadFirstCosmosItemViaRestAsync(
                    connectionString: connectionString,
                    databaseName: "$databaseName",
                    containerName: "$containerName",
                    query: @"$query",
                    partitionKey: @"$partitionKey"));
            }
"@
            } else {
                $connectionString = $connectionString -replace '"', '""'

                $csharpCode += @"

            blockOutputs.Add(await ReadFirstCosmosItemViaRestAsync(
                connectionString: @"$connectionString",
                databaseName: "$databaseName",
                containerName: "$containerName",
                query: @"$query",
                partitionKey: @"$partitionKey"));
"@
            }
            continue
        }
    }

    $csharpCode += @"

            outputs.Add(string.Join(Environment.NewLine, blockOutputs));
        }
"@
}

$csharpCode += "`n"
$csharpCode += @"
        if (outputs.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, outputs);
    }

    private static async Task<string> ReadFirstCosmosItemViaRestAsync(string connectionString, string databaseName, string containerName, string query, string? partitionKey)
    {
        try
        {
            if (!TryParseConnectionString(connectionString, out var endpoint, out var key))
            {
                return "Invalid Cosmos connection string.";
            }

            var baseUri = new Uri(endpoint);
            var resourceLink = $"dbs/{databaseName}/colls/{containerName}";
            var date = DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture);

            const string verb = "post";
            var httpMethod = HttpMethod.Post;
            var auth = BuildAuthToken(verb, "docs", resourceLink, date, key);
            var requestUri = new Uri(baseUri, $"{resourceLink}/docs").ToString();

            var payload = BuildQueryPayload(query);

            using var message = new HttpRequestMessage(httpMethod, requestUri);
            message.Headers.TryAddWithoutValidation("x-ms-date", date);
            message.Headers.TryAddWithoutValidation("x-ms-version", "2018-12-31");
            message.Headers.TryAddWithoutValidation("Authorization", auth);
            message.Headers.TryAddWithoutValidation("x-ms-documentdb-isquery", "true");
            if (string.IsNullOrWhiteSpace(partitionKey))
            {
                message.Headers.TryAddWithoutValidation("x-ms-documentdb-query-enablecrosspartition", "true");
            }
            else
            {
                var escapedPk = JsonEncodedText.Encode(partitionKey);
                message.Headers.TryAddWithoutValidation("x-ms-documentdb-partitionkey", $"[\"{escapedPk.ToString()}\"]");
            }
            message.Headers.TryAddWithoutValidation("Accept", "application/json");
            message.Content = new StringContent(payload, Encoding.UTF8, "application/query+json");
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/query+json");

            var response = await Http.SendAsync(message);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return $"Cosmos REST error {(int)response.StatusCode}: {response.ReasonPhrase} {content}";
            }

            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("Documents", out var docs) || docs.GetArrayLength() == 0)
            {
                return "No items found.";
            }

            return docs[0].GetRawText();
        }
        catch (Exception ex)
        {
            return $"Failed to read Cosmos DB item via REST: {ex.Message}";
        }
    }

    private static bool TryParseConnectionString(string connectionString, out string endpoint, out string key)
    {
        endpoint = string.Empty;
        key = string.Empty;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2)
            {
                continue;
            }

            var name = kv[0].Trim();
            var value = kv[1].Trim();

            if (name.Equals("AccountEndpoint", StringComparison.OrdinalIgnoreCase))
            {
                endpoint = value;
            }
            else if (name.Equals("AccountKey", StringComparison.OrdinalIgnoreCase))
            {
                key = value;
            }
        }

        return !string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(key);
    }

    private static string BuildAuthToken(string verb, string resourceType, string resourceLink, string date, string key)
    {
        var sb = new StringBuilder();
        sb.Append(verb.ToLowerInvariant());
        sb.Append('\n');
        sb.Append(resourceType.ToLowerInvariant());
        sb.Append('\n');
        sb.Append(resourceLink);
        sb.Append('\n');
        sb.Append(date.ToLowerInvariant());
        sb.Append('\n');
        sb.Append('\n');
        var payload = sb.ToString();

        var keyBytes = Convert.FromBase64String(key);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var signature = Convert.ToBase64String(hash);
        var auth = $"type=master&ver=1.0&sig={signature}";

        return Uri.EscapeDataString(auth);
    }

    private static string BuildQueryPayload(string query)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("query", query);
            writer.WriteStartArray("parameters");
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
"@

# Write the generated C# code
$csharpCode | Out-File -FilePath $OutputPath -Encoding UTF8
Write-Host "Generated: $OutputPath"
