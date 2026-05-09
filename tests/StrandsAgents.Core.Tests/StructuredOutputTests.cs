using Moq;
using StrandsAgents.Core;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace StrandsAgents.Core.Tests;

public class StructuredOutputTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static Agent AgentReturning(string responseText)
    {
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new ModelResponse(responseText, [], StopReason.EndTurn, TokenUsage.Zero));
        return new Agent(model.Object);
    }

    // ── target types ─────────────────────────────────────────────────────────

    private record WeatherResult(string City, double Temperature);

    private record RequiredFieldsResult
    {
        [JsonRequired]
        public string Name { get; init; } = default!;

        public int? Age { get; init; }
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStructuredOutputAsync_ValidJson_DeserializesToRecord()
    {
        var agent = AgentReturning("""{"city":"London","temperature":15.5}""");

        var result = await agent.GetStructuredOutputAsync<WeatherResult>("What is the weather?");

        Assert.Equal("London", result.City);
        Assert.Equal(15.5, result.Temperature);
    }

    [Fact]
    public async Task GetStructuredOutputAsync_RecordType_ReturnsImmutableInstance()
    {
        var agent = AgentReturning("""{"city":"Paris","temperature":20.0}""");

        var result = await agent.GetStructuredOutputAsync<WeatherResult>("Weather in Paris?");

        // Records are immutable value-like types — verify it's a record
        Assert.IsType<WeatherResult>(result);
        Assert.Equal("Paris", result.City);
    }

    [Fact]
    public async Task GetStructuredOutputAsync_InvalidJson_ThrowsStructuredOutputException()
    {
        var agent = AgentReturning("This is not JSON at all.");

        var ex = await Assert.ThrowsAsync<StructuredOutputException>(
            () => agent.GetStructuredOutputAsync<WeatherResult>("Weather?"));

        Assert.Contains("WeatherResult", ex.Message);
        Assert.Equal("This is not JSON at all.", ex.RawResponse);
        Assert.IsType<JsonException>(ex.InnerException);
    }

    [Fact]
    public async Task GetStructuredOutputAsync_MissingRequiredField_ThrowsStructuredOutputException()
    {
        // "name" is [JsonRequired] — omitting it should cause deserialization to throw
        var agent = AgentReturning("""{"age":30}""");

        await Assert.ThrowsAsync<StructuredOutputException>(
            () => agent.GetStructuredOutputAsync<RequiredFieldsResult>("Get person?"));
    }

    [Fact]
    public async Task GetStructuredOutputAsync_RequiredFieldPresent_Succeeds()
    {
        var agent = AgentReturning("""{"name":"Alice","age":25}""");

        var result = await agent.GetStructuredOutputAsync<RequiredFieldsResult>("Get person?");

        Assert.Equal("Alice", result.Name);
        Assert.Equal(25, result.Age);
    }

    [Fact]
    public async Task GetStructuredOutputAsync_ExceptionContainsRawResponse()
    {
        const string badJson = "not-json";
        var agent = AgentReturning(badJson);

        var ex = await Assert.ThrowsAsync<StructuredOutputException>(
            () => agent.GetStructuredOutputAsync<WeatherResult>("?"));

        Assert.Equal(badJson, ex.RawResponse);
    }
}
