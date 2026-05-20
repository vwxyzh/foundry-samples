using Azure.Identity;
using OpenAI;
using OpenAI.Responses;
using System.ClientModel;

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

if (string.IsNullOrWhiteSpace(responsesEndpoint))
{
	responsesEndpoint = $"{projectEndpoint.TrimEnd('/')}/agents/{agentName}/endpoint/protocols/openai/responses";
}

// Derive the base OpenAI-compatible endpoint (strip trailing /responses, add api-version)
var baseUrl = responsesEndpoint[..responsesEndpoint.LastIndexOf("/responses")];
var baseEndpoint = new Uri($"{baseUrl}?api-version=2025-11-15-preview");

var credential = new DefaultAzureCredential();
var token = await credential.GetTokenAsync(
	new Azure.Core.TokenRequestContext(["https://ai.azure.com/.default"]));

var client = new OpenAIClient(
	new ApiKeyCredential(token.Token),
	new OpenAIClientOptions { Endpoint = baseEndpoint });
var responsesClient = client.GetResponsesClient();

Console.WriteLine($"Sending to agent '{agentName}': {input}");

var options = new CreateResponseOptions("not-used", new ResponseItem[] { ResponseItem.CreateUserMessageItem(input) })
{
	StreamingEnabled = true
};

await foreach (var update in responsesClient.CreateResponseStreamingAsync(options))
{
	if (update is StreamingResponseOutputTextDeltaUpdate textDelta)
	{
		Console.Write(textDelta.Delta);
	}
}
Console.WriteLine();
