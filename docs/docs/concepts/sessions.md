---
sidebar_position: 5
---

# Sessions

## What it is

A **session** is the conversation history between a user and an agent — the sequence of messages that gives the agent context across multiple turns. The `ISessionManager` interface controls how this history is stored and retrieved.

## Problem it solves

Without session management, every `InvokeAsync` call starts a fresh conversation. The agent has no memory of previous turns. Sessions let you build multi-turn assistants, persistent agents, and workflows that span multiple Lambda invocations.

## How to use it

```csharp
// In-memory session — lives for the lifetime of the Agent instance
var agent = new Agent(
    model: new BedrockModel("us-east-1"),
    systemPrompt: "You are a helpful assistant.");

// Turn 1
var r1 = await agent.InvokeAsync("My name is Alice.");
// Turn 2 — agent remembers "Alice"
var r2 = await agent.InvokeAsync("What's my name?");
Console.WriteLine(r2.Message); // "Your name is Alice."
```

For persistence across process restarts, use `FileSessionManager`:

```csharp
var agent = new Agent(
    model: new BedrockModel("us-east-1"),
    systemPrompt: "You are a helpful assistant.",
    sessionManager: new FileSessionManager("/var/sessions"),
    sessionId: "user-123");
```

## Session managers

| Manager | Storage | Use when |
|---|---|---|
| `InMemorySessionManager` (default) | Process memory | Single-process apps, short-lived agents |
| `FileSessionManager` | Local filesystem | Long-running processes, development |
| `AgentCoreSessionManager` | Amazon Bedrock AgentCore Memory | Lambda, distributed, production |
| Custom `ISessionManager` | Anywhere | DynamoDB, Redis, your own store |

## DI wiring

```csharp
builder.Services
    .AddBedrockModel("us-east-1")
    .AddStrandsInMemorySessionManager()   // or AddStrandsFileSessionManager(path)
    .AddStrandsAgent();
```

## Context window trimming

Long conversations eventually exceed the model's context window. Two strategies:

**Sliding window** — keeps the most recent N messages:

```csharp
var agent = new Agent(
    model: model,
    conversationManager: new SlidingWindowConversationManager(maxMessages: 20));
```

**Summarizing** — periodically summarizes old messages into a single summary message:

```csharp
var agent = new Agent(
    model: model,
    conversationManager: new SummarizingConversationManager(model, maxMessages: 30));
```

## Trade-offs

**In-memory sessions** are fast but lost on process restart. Use them for stateless Lambda functions where each invocation is independent.

**File sessions** persist across restarts but don't work in distributed environments (multiple Lambda instances can't share a local file).

**AgentCore sessions** work in any environment but add latency for the memory read/write operations.
