using System.Text;
using System.Text.Json;
using Amazon;
using Amazon.BedrockAgentCore;
using Amazon.BedrockAgentCore.Model;

// ── Configuration ─────────────────────────────────────────────────────────────────
// Set AGENT_RUNTIME_ARN to your deployed AgentCore Runtime ARN.
// Credentials are resolved automatically via the standard AWS credential chain
// (environment variables, ~/.aws/credentials, instance metadata, etc.).
// ─────────────────────────────────────────────────────────────────────────────────

var runtimeArn = Environment.GetEnvironmentVariable("AGENT_RUNTIME_ARN")
    ?? throw new InvalidOperationException(
        "Set the AGENT_RUNTIME_ARN environment variable to your AgentCore Runtime ARN.\n" +
        "Example: export AGENT_RUNTIME_ARN=arn:aws:bedrock-agentcore:us-east-1:123456789012:runtime/my_agent-AbCdEfGh");

var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1";

// Read prompt from args or prompt interactively
var prompt = args.Length > 0
    ? string.Join(" ", args)
    : ReadPrompt();

using var client = new AmazonBedrockAgentCoreClient(RegionEndpoint.GetBySystemName(region));

Console.WriteLine($"Runtime : {runtimeArn}");
Console.WriteLine($"Prompt  : {prompt}");
Console.WriteLine();

// ── Non-streaming invocation ──────────────────────────────────────────────────────
var request = new InvokeAgentRuntimeRequest
{
    AgentRuntimeArn = runtimeArn,
    Payload = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { prompt }))),
};

InvokeAgentRuntimeResponse response;
try
{
    response = await client.InvokeAgentRuntimeAsync(request);
}
catch (AmazonBedrockAgentCoreException ex)
{
    Console.Error.WriteLine($"Error invoking runtime: {ex.Message}");
    return 1;
}

// The response body is a JSON stream — read and deserialise it
using var reader = new StreamReader(response.Response);
var body = await reader.ReadToEndAsync();

using var doc = JsonDocument.Parse(body);
var root = doc.RootElement;

if (root.TryGetProperty("result", out var result))
{
    Console.WriteLine("Response:");
    Console.WriteLine(result.GetString());
}
else
{
    // Unexpected shape — print the raw body so nothing is hidden
    Console.WriteLine("Raw response:");
    Console.WriteLine(JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true }));
}

if (root.TryGetProperty("usage", out var usage))
{
    Console.WriteLine();
    Console.WriteLine($"Tokens — input: {usage.GetProperty("inputTokens").GetInt32()}, " +
                      $"output: {usage.GetProperty("outputTokens").GetInt32()}");
}

return 0;

// ── Helpers ───────────────────────────────────────────────────────────────────────

static string ReadPrompt()
{
    Console.Write("Prompt: ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input))
        throw new InvalidOperationException("Prompt cannot be empty.");
    return input;
}
