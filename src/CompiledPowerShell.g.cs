// Auto-generated from Hello.ps1 at build time
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

namespace HelloWasmApp;

public static class CompiledPowerShell
{
    private static readonly HttpClient Http = new HttpClient();

    public static async Task<string> ExecuteAsync()
    {
        var outputs = new List<string>();
        outputs.Add("Hello PowerShell");
        if (outputs.Count == 0)
        {
            return "No output generated.";
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
