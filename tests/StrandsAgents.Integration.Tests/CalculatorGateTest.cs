using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;
using StrandsAgents.Tools;
using Xunit;

namespace StrandsAgents.Integration.Tests;

/// <summary>
/// Week 1 gate test: agent.InvokeAsync uses CalculatorTool against live Bedrock
/// and returns the correct answer.
///
/// These tests require live AWS credentials (via environment variables, ~/.aws/credentials,
/// or an IAM role). They are skipped automatically unless the environment variable
/// STRANDS_INTEGRATION_TESTS=true is set to prevent unintended charges in CI.
///
/// To run locally:
///   STRANDS_INTEGRATION_TESTS=true dotnet test --filter "Category=Integration"
/// </summary>
public sealed class CalculatorGateTest
{
    private static bool ShouldRun =>
        string.Equals(
            Environment.GetEnvironmentVariable("STRANDS_INTEGRATION_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InvokeAsync_CalculatorTool_ReturnsCorrectProduct()
    {
        if (!ShouldRun)
            return; // requires STRANDS_INTEGRATION_TESTS=true

        var region = Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION") ?? "us-east-1";
        var model = new BedrockModel(region);
        var calcTool = new CalculatorTool_Calculate_Tool(new CalculatorTool());
        var agent = new Agent(model, tools: [calcTool]);

        var result = await agent.InvokeAsync("What is 42 multiplied by 1764?");

        Assert.Equal(StopReason.EndTurn, result.StopReason);
        Assert.Contains("74088", result.Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task InvokeAsync_CalculatorTool_Addition()
    {
        if (!ShouldRun)
            return;

        var region = Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION") ?? "us-east-1";
        var model = new BedrockModel(region);
        var calcTool = new CalculatorTool_Calculate_Tool(new CalculatorTool());
        var agent = new Agent(model, tools: [calcTool]);

        var result = await agent.InvokeAsync("What is 1337 plus 663?");

        Assert.Equal(StopReason.EndTurn, result.StopReason);
        Assert.Contains("2000", result.Message);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StreamAsync_CalculatorTool_YieldsTextBeforeComplete()
    {
        if (!ShouldRun)
            return;

        var region = Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION") ?? "us-east-1";
        var model = new BedrockModel(region);
        var calcTool = new CalculatorTool_Calculate_Tool(new CalculatorTool());
        var agent = new Agent(model, tools: [calcTool]);

        var textDeltas = new List<string>();
        AgentResult? finalResult = null;

        await foreach (var evt in agent.StreamAsync("What is 42 multiplied by 1764?"))
        {
            if (evt is TextDeltaEvent td)
                textDeltas.Add(td.Delta);
            else if (evt is AgentCompleteEvent complete)
                finalResult = complete.Result;
        }

        Assert.NotNull(finalResult);
        Assert.Equal(StopReason.EndTurn, finalResult.StopReason);
        Assert.Contains("74088", string.Concat(textDeltas));
        Assert.NotEmpty(textDeltas); // streaming actually yielded deltas
    }
}
