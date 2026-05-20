using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Jacquard.Core;
using Jacquard.Extensions.DI;
using Jacquard.Tools;
using System.Text.Json;
using Xunit;

namespace Jacquard.Core.Tests;

public class StrandsDIExtensionsTests
{
    [Fact]
    public void AddOpenAICompatibleModel_RegistersIModel()
    {
        var services = new ServiceCollection();
        services.AddOpenAICompatibleModel("http://localhost", "key", "gpt-4o");

        using var provider = services.BuildServiceProvider();
        var model = provider.GetService<IModel>();

        Assert.NotNull(model);
    }

    [Fact]
    public void AddAnthropicModel_RegistersIModel()
    {
        var services = new ServiceCollection();
        services.AddAnthropicModel("sk-test");

        using var provider = services.BuildServiceProvider();
        var model = provider.GetService<IModel>();

        Assert.NotNull(model);
    }

    [Fact]
    public void AddJacquardAgent_WithModel_ResolvesIAgent()
    {
        var services = new ServiceCollection();
        services.AddOpenAICompatibleModel("http://localhost", "key");
        services.AddJacquardAgent();

        using var provider = services.BuildServiceProvider();
        var agent = provider.GetService<IAgent>();

        Assert.NotNull(agent);
    }

    [Fact]
    public void AddStrandsTool_RegistersToolInEnumerable()
    {
        var services = new ServiceCollection();
        services.AddStrandsTool<StubTool>();

        using var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<ITool>().ToList();

        Assert.Single(tools);
        Assert.IsType<StubTool>(tools[0]);
    }

    [Fact]
    public void AddHttpRequestTool_RegistersITool()
    {
        var services = new ServiceCollection();
        services.AddHttpRequestTool();

        using var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<ITool>().ToList();

        Assert.Single(tools);
        Assert.IsType<HttpRequestTool>(tools[0]);
    }

    [Fact]
    public void AddFileReadTool_RegistersITool()
    {
        var basePath = Path.GetTempPath();
        var services = new ServiceCollection();
        services.AddFileReadTool(basePath);

        using var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<ITool>().ToList();

        Assert.Single(tools);
        Assert.IsType<FileReadTool>(tools[0]);
    }

    [Fact]
    public void AddFileWriteTool_RegistersITool()
    {
        var basePath = Path.GetTempPath();
        var services = new ServiceCollection();
        services.AddFileWriteTool(basePath);

        using var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<ITool>().ToList();

        Assert.Single(tools);
        Assert.IsType<FileWriteTool>(tools[0]);
    }

    [Fact]
    public void AddJacquardInMemorySessionManager_RegistersISessionManager()
    {
        var services = new ServiceCollection();
        services.AddJacquardInMemorySessionManager();

        using var provider = services.BuildServiceProvider();
        var sessionManager = provider.GetService<ISessionManager>();

        Assert.NotNull(sessionManager);
    }

    [Fact]
    public void AddJacquardWorker_RegistersIHostedService()
    {
        var services = new ServiceCollection();
        services.AddJacquardWorker<StubWorker>();

        using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToList();

        Assert.Single(hostedServices);
        Assert.IsType<StubWorker>(hostedServices[0]);
    }

    [Fact]
    public void AddJacquardAgent_MultipleTools_AllResolvable()
    {
        var services = new ServiceCollection();
        services.AddOpenAICompatibleModel("http://localhost", "key");
        services.AddFileReadTool(Path.GetTempPath());
        services.AddFileWriteTool(Path.GetTempPath());
        services.AddJacquardAgent();

        using var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<ITool>().ToList();

        Assert.Equal(2, tools.Count);
    }

    [Fact]
    public void AddOpenAICompatibleModel_RegistersNamedHttpClient()
    {
        var services = new ServiceCollection();
        services.AddOpenAICompatibleModel("http://localhost", "key");

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        // Named client must be resolvable without throwing.
        var client = factory.CreateClient(OpenAICompatibleModel.HttpClientName);
        Assert.NotNull(client);
    }
}

// Minimal ITool stub for DI registration tests.
internal sealed class StubTool : ITool
{
    private static readonly ToolDefinition _definition = new(
        "stub", "A stub tool.", JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone());

    public ToolDefinition Definition => _definition;

    public Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct = default)
        => Task.FromResult(ToolResult.Success("stub", "ok"));
}

internal sealed class StubWorker : IHostedService
{
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
