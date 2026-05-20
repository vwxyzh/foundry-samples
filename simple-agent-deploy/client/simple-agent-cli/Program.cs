using Azure.AI.Projects;
using Azure.Identity;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

#pragma warning disable OPENAI001

var input = args.Length > 0 ? string.Join(" ", args) : "hello";

var projectEndpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT");
var responsesEndpoint = Environment.GetEnvironmentVariable("AGENT_SIMPLE_AGENT_RESPONSES_ENDPOINT");
var agentName = Environment.GetEnvironmentVariable("AGENT_SIMPLE_AGENT_NAME") ?? "simple-agent";

if (string.IsNullOrWhiteSpace(projectEndpoint))
{
	Console.Error.WriteLine("Missing AZURE_AI_PROJECT_ENDPOINT environment variable.");
	Console.Error.WriteLine("Run in your azd environment, for example:");
	Console.Error.WriteLine("  azd env get-values");
	Environment.Exit(1);
}

var credential = new DefaultAzureCredential();
_ = new AIProjectClient(new Uri(projectEndpoint), credential);

if (string.IsNullOrWhiteSpace(responsesEndpoint))
{
	responsesEndpoint = $"{projectEndpoint.TrimEnd('/')}/agents/{agentName}/endpoint/protocols/openai/responses?api-version=2025-11-15-preview";
}

var token = await credential.GetTokenAsync(
	new Azure.Core.TokenRequestContext(["https://ai.azure.com/.default"]));

using var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

Console.WriteLine($"Sending to agent '{agentName}': {input}");
var payload = JsonSerializer.Serialize(new { input, stream = false });
using var content = new StringContent(payload, Encoding.UTF8, "application/json");
using var response = await httpClient.PostAsync(responsesEndpoint, content);
var responseBody = await response.Content.ReadAsStringAsync();

if (!response.IsSuccessStatusCode)
{
	Console.Error.WriteLine($"Request failed with status {(int)response.StatusCode}: {response.ReasonPhrase}");
	Console.Error.WriteLine(responseBody);
	Environment.Exit(1);
}

var outputText = ExtractOutputText(responseBody);
Console.WriteLine("Agent response:");
Console.WriteLine(string.IsNullOrWhiteSpace(outputText) ? "<empty>" : outputText);

static string? ExtractOutputText(string responseBody)
{
	using var doc = JsonDocument.Parse(responseBody);
	var root = doc.RootElement;

	if (root.TryGetProperty("output_text", out var directText) && directText.ValueKind == JsonValueKind.String)
	{
		return directText.GetString();
	}

	if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
	{
		var sb = new StringBuilder();
		foreach (var item in output.EnumerateArray())
		{
			if (!item.TryGetProperty("content", out var contentArray) || contentArray.ValueKind != JsonValueKind.Array)
			{
				continue;
			}

			foreach (var content in contentArray.EnumerateArray())
			{
				if (content.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
				{
					if (sb.Length > 0)
					{
						sb.AppendLine();
					}
					sb.Append(text.GetString());
				}
			}
		}

		return sb.ToString();
	}

	return null;
}
