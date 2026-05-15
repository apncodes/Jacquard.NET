using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;
using AotLambda;

// NativeAOT Lambda bootstrap — no reflection, no JIT warm-up.
// The handler is a plain async delegate; LambdaBootstrapBuilder wires it to the Lambda runtime.
var handler = async (string input, ILambdaContext context) =>
{
    var agent = new Agent(
        model: new BedrockModel(
            region: Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1",
            modelId: "us.anthropic.claude-haiku-4-5-20251001-v1:0"),
        systemPrompt: "You are a helpful assistant. Use the weather tool when asked about weather.",
        toolProviders: [new WeatherTools()]);

    var result = await agent.InvokeAsync(input);
    return result.Message;
};

await LambdaBootstrapBuilder
    .Create(handler, new DefaultLambdaJsonSerializer())
    .Build()
    .RunAsync();

// Tool class must be in a namespace when mixed with top-level statements.
// The source generator emits IToolProvider in the same namespace — both partials merge at compile time.
namespace AotLambda
{
    /// <summary>
    /// Minimal tool class demonstrating [Tool] + partial class in an AOT-published Lambda.
    /// The Roslyn source generator emits the ITool wrapper and IToolProvider implementation
    /// at compile time — zero runtime reflection, fully AOT-safe.
    /// </summary>
    public partial class WeatherTools
    {
        [Tool("Returns the current weather for a city. Use this when the user asks about weather.")]
        public string GetWeather(string city) =>
            $"Sunny, 22°C in {city}. (This is a demo tool — replace with a real weather API call.)";
    }
}
