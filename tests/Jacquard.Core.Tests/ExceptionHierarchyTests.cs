using Jacquard.Core;
using System.Text.Json;
using Xunit;

namespace Jacquard.Core.Tests;

public class ExceptionHierarchyTests
{
    private static ModelRequest FakeRequest() =>
        new([], null, [], new ModelParameters());

    [Fact]
    public void ModelException_IsA_StrandsException()
    {
        var ex = new ModelException("model error", FakeRequest());
        Assert.IsAssignableFrom<StrandsException>(ex);
    }

    [Fact]
    public void ToolException_IsA_StrandsException()
    {
        var ex = new ToolException("tool error", "myTool", "call-1");
        Assert.IsAssignableFrom<StrandsException>(ex);
    }

    [Fact]
    public void StructuredOutputException_IsA_StrandsException()
    {
        var ex = new StructuredOutputException("bad json", "raw");
        Assert.IsAssignableFrom<StrandsException>(ex);
    }

    [Fact]
    public void ModelException_StoresRequest()
    {
        var request = FakeRequest();
        var ex = new ModelException("fail", request, httpStatusCode: 429);

        Assert.Same(request, ex.Request);
        Assert.Equal(429, ex.HttpStatusCode);
    }

    [Fact]
    public void ModelException_WithConversationSnapshot_SnapshotAccessible()
    {
        var messages = new List<Message> { Message.User("hello") };
        var ex = new ModelException("fail", FakeRequest(), conversationSnapshot: messages);

        Assert.NotNull(ex.ConversationSnapshot);
        Assert.Single(ex.ConversationSnapshot!);
    }

    [Fact]
    public void ToolException_StoresToolNameAndCallId()
    {
        var ex = new ToolException("fail", "calculator", "call-42");

        Assert.Equal("calculator", ex.ToolName);
        Assert.Equal("call-42", ex.ToolCallId);
    }

    [Fact]
    public void ModelException_WithInnerException_InnerAccessible()
    {
        var inner = new InvalidOperationException("root cause");
        var ex = new ModelException("wrapped", FakeRequest(), inner: inner);

        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void StructuredOutputException_StoresRawResponse()
    {
        const string raw = "{bad json}";
        var ex = new StructuredOutputException("parse error", raw);

        Assert.Equal(raw, ex.RawResponse);
        Assert.Null(ex.ConversationSnapshot);
    }
}
