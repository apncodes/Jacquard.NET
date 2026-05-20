# AspNetAgent

## SDK concepts demonstrated

**DI-first wiring** — `AddBedrockModel()`, `AddHttpRequestTool()`, and `AddJacquardInMemorySessionManager()` register the entire Strands stack in three lines. `IModel` and `IEnumerable<ITool>` are injected directly into the minimal API handler.

**Per-session conversation continuity** — a `ConcurrentDictionary<string, Agent>` keyed by `sessionId` stores one `Agent` per client. The agent's `InMemoryConversationManager` accumulates history, so each `/chat` call continues the same conversation.

**SSE streaming** — `StreamAsync` drives the response. `TextDeltaEvent` writes `data: <delta>\n\n`; `AgentCompleteEvent` writes an `event: done` frame. No WebSockets, no SignalR.

## Scenario

POST `/chat` with a `sessionId` and `message`. Subsequent requests with the same `sessionId` continue the conversation. DELETE `/sessions/{id}` clears a session.

## How to run

```bash
dotnet run --project samples/AspNetAgent
```

```bash
# Start a conversation
curl -N -X POST http://localhost:5000/chat \
  -H "Content-Type: application/json" \
  -d '{"sessionId":"s1","message":"My name is Alice. What is 12 times 12?"}'

# Continue it — agent remembers Alice
curl -N -X POST http://localhost:5000/chat \
  -H "Content-Type: application/json" \
  -d '{"sessionId":"s1","message":"What did I just ask you to calculate?"}'

# Clear session
curl -X DELETE http://localhost:5000/sessions/s1
```

## Where you'd use these patterns

- **SaaS chat backends** — the sessionId pattern maps directly to a multi-tenant chat feature.
- **API-first AI services** — expose any agent capability over HTTP with SSE to any frontend.
