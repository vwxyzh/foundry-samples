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
var payload = JsonSerializer.Serialize(new { input, stream = true });

var request = new HttpRequestMessage(HttpMethod.Post, responsesEndpoint);
request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

if (!response.IsSuccessStatusCode)
{
	var errorBody = await response.Content.ReadAsStringAsync();
	Console.Error.WriteLine($"Request failed with status {(int)response.StatusCode}: {response.ReasonPhrase}");
	Console.Error.WriteLine(errorBody);
	Environment.Exit(1);
}

Console.WriteLine("Agent response:");
using var stream = await response.Content.ReadAsStreamAsync();
using var reader = new StreamReader(stream);

while (!reader.EndOfStream)
{
	var line = await reader.ReadLineAsync();
	if (line is null) break;
	if (!line.StartsWith("data: ")) continue;

	var data = line["data: ".Length..];
	if (data == "[DONE]") break;

	try
	{
		using var doc = JsonDocument.Parse(data);
		var root = doc.RootElement;

		// Extract delta text from response.output_text.delta events
		if (root.TryGetProperty("type", out var typeProp))
		{
			var type = typeProp.GetString();
			if (type == "response.output_text.delta" &&
				root.TryGetProperty("delta", out var delta) &&
				delta.ValueKind == JsonValueKind.String)
			{
				Console.Write(delta.GetString());
			}
		}
	}
	catch (JsonException)
	{
		// skip malformed events
	}
}
Console.WriteLine();
