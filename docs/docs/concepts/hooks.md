---
sidebar_position: 4
---

# Hooks

## What it is

Hooks are callbacks that fire at specific points in the agent event loop. They let you observe, modify, or interrupt agent behavior without changing the agent's core logic.

## Problem it solves

Without hooks, you'd need to subclass `Agent` or wrap every tool call to add logging, auditing, PII redaction, or human-in-the-loop approval. Hooks give you these interception points without modifying the agent or tool code.

## How to use it

Register hooks on the agent's `Hooks` property:

```csharp
var agent = new Agent(model, systemPrompt: "...", toolProviders: [new MyTools()]);

// Log every tool call
agent.Hooks.Register<BeforeToolCallEvent>(async (e, ct) =>
{
    Console.WriteLine($"Calling tool: {e.ToolName} with args: {e.Input}");
});

// Log every tool result
agent.Hooks.Register<AfterToolCallEvent>(async (e, ct) =>
{
    Console.WriteLine($"Tool {e.ToolName} returned: {e.Result.Content}");
});
```

## The four hook events

| Event | When it fires | What you can do |
|---|---|---|
| `BeforeModelCallEvent` | Before each model API call | Inspect/modify the messages being sent |
| `AfterModelCallEvent` | After each model API call | Inspect the raw model response |
| `BeforeToolCallEvent` | Before each tool execution | Inspect args, interrupt execution |
| `AfterToolCallEvent` | After each tool execution | Inspect result, modify it |

## Human-in-the-loop

Set `e.Interrupt = true` in a `BeforeToolCallEvent` hook to pause execution before a sensitive tool call:

```csharp
agent.Hooks.Register<BeforeToolCallEvent>(async (e, ct) =>
{
    if (e.ToolName == "DeleteFile")
    {
        Console.WriteLine($"About to delete: {e.Input}");
        Console.Write("Approve? (y/n): ");
        var response = Console.ReadLine();
        if (response?.ToLower() != "y")
            e.Interrupt = true;  // cancels the tool call
    }
});
```

When `Interrupt = true`, the tool is not called. The agent receives a cancellation result and typically stops or asks the user what to do next.

## Audit logging

```csharp
agent.Hooks.Register<BeforeToolCallEvent>(async (e, ct) =>
{
    await auditLog.WriteAsync(new AuditEntry
    {
        Timestamp = DateTimeOffset.UtcNow,
        ToolName = e.ToolName,
        Input = e.Input.GetRawText(),
        UserId = currentUser.Id
    });
});
```

## PII redaction

```csharp
agent.Hooks.Register<AfterModelCallEvent>(async (e, ct) =>
{
    // Scan model output for PII before it reaches tool calls
    if (ContainsPii(e.Response))
        e.Response = RedactPii(e.Response);
});
```

## Trade-offs

Hooks run synchronously in the event loop. A slow hook (e.g., one that makes a network call) adds latency to every tool call. Keep hooks fast or use fire-and-forget patterns for non-blocking operations like audit logging.
