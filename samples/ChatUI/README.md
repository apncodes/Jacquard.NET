# ChatUI

A browser chat interface backed by a streaming Jacquard agent. Each browser tab gets its own isolated session with full conversation history. Tool invocations appear as inline badges as they happen.

## SDK concepts demonstrated

**SSE streaming** — `StreamAsync` returns `IAsyncEnumerable<StreamEvent>`. The minimal API endpoint iterates events and writes each one to the HTTP response as `text/event-stream`. The browser consumes the stream with the Fetch API and `ReadableStream` — no WebSockets, no SignalR.

**Per-session agent isolation** — a `ConcurrentDictionary<string, Agent>` keyed by session ID stores one `Agent` per browser tab. Each `Agent` has its own `InMemoryConversationManager`, so conversations don't bleed across tabs.

**Source-generated tools** — `AssistantTools` exposes `GetWeather` and `GetCurrentTime` decorated with `[Tool]`. The Roslyn generator emits `AssistantTools_GetWeather_Tool` and `AssistantTools_GetCurrentTime_Tool` at compile time with JSON schemas baked in as string literals.

**Typed stream events** — the frontend distinguishes `{t:"delta"}`, `{t:"tool"}`, and `{t:"done"}` events to render text incrementally, show a tool badge mid-stream, and display token counts on completion.

## Scenario

You open the chat at `localhost:5000`. Each message is posted to `/chat` with your session ID. The response streams back token by token. If the agent calls a tool (e.g. to look up the weather), a badge appears inline in the bubble before the answer continues streaming.

## How to run

```bash
dotnet run --project samples/ChatUI
```

Open **http://localhost:5000** — try `"What's the weather in Tokyo?"` or `"What time is it in New York?"` to see tool badges rendered inline.

## Where you'd use these patterns

- **Customer-facing chat widgets** — the SSE + session isolation pattern maps directly to a production chat endpoint where each user has their own conversation.
- **Internal copilot tools** — embed the same streaming agent behind any HTML page without a frontend framework.
- **Prototyping** — the lightest path from a Jacquard agent to a browser UI: one minimal API endpoint, one HTML file, no build step.
