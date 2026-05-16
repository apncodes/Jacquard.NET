---
sidebar_position: 1
---

# Agent & Event Loop

## What it is

The `Agent` class is the central abstraction. It wraps a model, a set of tools, and a system prompt, and runs the **event loop** — the cycle of model call → tool execution → model call that continues until the model signals it's done.

## Problem it solves

Without an event loop, you'd write this yourself: call the model, check if it wants to use a tool, execute the tool, feed the result back, call the model again, repeat. Every agent would re-implement this loop with slightly different error handling and state management. The `Agent` class does this once, correctly.

## How to use it

```csharp
var agent = new Agent(
    model: new BedrockModel("us-east-1"),
    systemPrompt: "You are a helpful assistant.",
    toolProviders: [new MyTools()]);

// Single invocation — returns when the model signals EndTurn
var result = await agent.InvokeAsync("What is 2 + 2?");
Console.WriteLine(result.Message);

// Streaming — yields events as they arrive
await foreach (var evt in agent.StreamAsync("Explain recursion"))
{
    if (evt is TextDeltaEvent delta)
        Console.Write(delta.Delta);
}
```

## The loop in detail

```
User prompt
    │
    ▼
Model call ──► EndTurn? ──► Return result
    │
    ▼ (tool calls requested)
Execute tools (parallel if multiple)
    │
    ▼
Feed tool results back to model
    │
    └──► Model call (repeat)
```

The model decides:
- Which tools to call (zero, one, or many simultaneously)
- What arguments to pass to each tool
- When to stop (by returning `EndTurn` instead of tool calls)

You never write this logic. The event loop handles it.

## Trade-offs

**Depth:** The loop runs until `EndTurn`. A model that keeps requesting tools will keep looping. Set `maxIterations` on the `Agent` constructor to cap this for safety.

**Parallelism:** When the model requests multiple tools in one response, the SDK executes them concurrently via `Task.WhenAll`. This is the default and usually what you want.

**Streaming vs invocation:** `InvokeAsync` buffers the full response. `StreamAsync` yields `StreamEvent` objects as they arrive — use this for interactive UIs or when you want to show progress.

## Key types

| Type | Description |
|---|---|
| `Agent` | The main class. Holds model, tools, system prompt, session manager. |
| `AgentResult` | Returned by `InvokeAsync`. Contains `Message` (final text) and `StopReason`. |
| `StreamEvent` | Base type for streaming events. Subtypes: `TextDeltaEvent`, `ToolCallEvent`, `ToolResultEvent`. |
| `IModel` | Interface implemented by `BedrockModel`, `AnthropicModel`, `OpenAICompatibleModel`, `GeminiModel`. |
