---
sidebar_position: 8
---

# Amazon Bedrock AgentCore

Amazon Bedrock AgentCore is a suite of managed AWS services for production agentic AI. Jacquard.NET has first-class support for all AgentCore capabilities via the `Jacquard.Runtime` package.

```bash
dotnet add package Jacquard.Runtime
```

---

## AgentCore Runtime — Deploy any agent in one line

AgentCore Runtime is a managed hosting environment for AI agents. Deploy any Jacquard.NET agent to it by adding two lines to your ASP.NET Core app:

```csharp
using Jacquard.Runtime.Hosting;
using Jacquard.Extensions.DI;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddBedrockModel("us-east-1")
    .AddJacquardAgent("You are a helpful assistant.");

var app = builder.Build();
app.MapAgentCoreEndpoints();  // registers POST /invocations + GET /health
app.UseAgentCorePort(8080);   // AgentCore Runtime routes traffic to port 8080
app.Run();
```

`MapAgentCoreEndpoints()` registers:
- `POST /invocations` — receives agent requests from AgentCore Runtime
- `GET /health` — health check endpoint

Your agent code is **completely unchanged**. The same agent that runs locally runs on AgentCore Runtime — only the hosting changes.

**Dockerfile:**

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY publish/ .
ENTRYPOINT ["dotnet", "YourAgent.dll"]
```

See the [AgentCoreSample](https://github.com/apncodes/Jacquard.NET/tree/main/samples/AgentCoreSample) for a complete deployment example.

---

## AgentCore Memory — Persistent agent memory

AgentCore Memory provides a managed, scalable memory store for agents. Two integration points:

### Session persistence (automatic)

Persist conversation sessions across Lambda invocations or process restarts:

```csharp
builder.Services
    .AddBedrockModel("us-east-1")
    .AddAgentCoreSessionManager(
        memoryId: Environment.GetEnvironmentVariable("AGENTCORE_MEMORY_ID") ?? "")
    .AddJacquardAgent();
```

The agent automatically loads and saves conversation history from AgentCore Memory. No code changes to the agent.

### Explicit memory tool (agent-initiated)

Give the agent the ability to explicitly store, retrieve, and delete memory records:

```csharp
builder.Services
    .AddBedrockModel("us-east-1")
    .AddAgentCoreMemory(memoryId: "your-memory-id")  // adds AgentCoreMemoryTool
    .AddJacquardAgent();
```

The agent can then call the `agentcore_memory` tool with three operations:

```
store_memory:    Save a text record. Returns the assigned memoryRecordId.
retrieve_memory: Fetch a stored record by its memoryRecordId.
delete_memory:   Remove a record by its memoryRecordId.
```

Example agent interaction:
> "Remember that the user prefers dark mode."
> → Agent calls `store_memory` with content "User prefers dark mode" and namespace "user:preferences"
> → Returns: "Stored memory record. memoryRecordId: abc123"

### Semantic memory search

For meaning-based retrieval (find memories similar to a query), use `SemanticMemoryTool`:

```csharp
builder.Services
    .AddBedrockModel("us-east-1")
    .AddAgentCoreSemanticMemory(memoryId: "your-memory-id")
    .AddJacquardAgent();
```

See the [SemanticMemorySample](https://github.com/apncodes/Jacquard.NET/tree/main/samples/SemanticMemorySample) for a complete example.

---

## AgentCore Code Interpreter — Stateful code execution sandbox

AgentCore Code Interpreter provides a managed, stateful sandbox for executing Python, JavaScript, and TypeScript. The session persists across calls — variables and imports survive between invocations.

```csharp
builder.Services
    .AddBedrockModel("us-east-1")
    .AddAgentCoreCodeInterpreter()  // adds AgentCoreCodeInterpreterTool
    .AddJacquardAgent();
```

The agent can execute code via the `agentcore_code_interpreter` tool:

```
Parameters:
  code:          The source code to execute
  language:      "python" | "javascript" | "typescript"
  clear_context: Reset session state (optional, default: false)

Returns:
  Exit code, execution time, stdout, stderr
```

Example agent interaction:
> "Calculate the compound interest on $10,000 at 5% for 10 years."
> → Agent calls `agentcore_code_interpreter` with Python code
> → Returns: stdout with the calculated result

**Stateful sessions:** The tool creates a session on first use and reuses it across calls. Variables defined in one call are available in subsequent calls. The session is cleaned up when the tool is disposed.

See the [CodeInterpreterSample](https://github.com/apncodes/Jacquard.NET/tree/main/samples/CodeInterpreterSample) for a complete example.

---

## AgentCore Browser — Managed headless Chrome

AgentCore Browser provides a managed headless Chrome instance. The agent controls it via the `agentcore_browser` tool, which returns a CDP (Chrome DevTools Protocol) endpoint URL for Playwright or Nova Act automation.

```csharp
builder.Services
    .AddBedrockModel("us-east-1")
    .AddAgentCoreBrowser()  // adds AgentCoreBrowserTool
    .AddJacquardAgent();
```

The agent manages browser sessions via the `agentcore_browser` tool:

```
start_session: Start a new browser session.
               Returns: sessionId + automationStreamEndpoint (CDP URL)

get_session:   Get the status and stream endpoint of an existing session.

stop_session:  Stop a browser session and release its resources.
```

**Typical usage pattern:**

1. Agent calls `start_session` → receives `automationStreamEndpoint`
2. Your code connects to the endpoint via Playwright:
   ```csharp
   var browser = await playwright.Chromium.ConnectOverCDPAsync(automationStreamEndpoint);
   ```
3. Perform navigation, clicks, screenshots via Playwright
4. Agent calls `stop_session` when done

See the [BrowserSample](https://github.com/apncodes/Jacquard.NET/tree/main/samples/BrowserSample) for a complete example.

---

## AgentCore Gateway — Connect to managed tool endpoints

AgentCore Gateway is a managed MCP (Model Context Protocol) endpoint that proxies external APIs, databases, and services with built-in auth and observability. Connect your agent to gateway-hosted tools with three lines:

```csharp
// Direct usage
await using var gateway = await AgentCoreGatewayToolProvider.CreateAsync(
    gatewayUrl: new Uri("https://...gateway-url.../mcp"),
    auth: new AgentCoreGatewayAuth.Iam(region: "us-east-1"));

var tools = await gateway.ListToolsAsync();
var agent = new Agent(model, tools: tools);
```

**Three auth modes** match your gateway's inbound authorization setting:

```csharp
// IAM SigV4 — credentials resolved from the standard AWS chain
new AgentCoreGatewayAuth.Iam(region: "us-east-1")

// JWT Bearer — Cognito, Entra ID, Okta, Google, GitHub, etc.
new AgentCoreGatewayAuth.Bearer(accessToken: token)

// No auth — network-isolated (VPC / security groups)
new AgentCoreGatewayAuth.None()
```

**DI registration** — registers all gateway tools directly into the container:

```csharp
builder.Services
    .AddBedrockModel("us-east-1")
    .AddAgentCoreGatewayTools(
        gatewayUrl: new Uri("https://...gateway-url.../mcp"),
        auth: new AgentCoreGatewayAuth.Iam("us-east-1"))
    .AddJacquardAgent();
```

`AddJacquardAgent()` picks up the gateway tools automatically — no explicit wiring needed.

See the [AgentCoreGatewaySample](https://github.com/apncodes/Jacquard.NET/tree/main/samples/AgentCoreGatewaySample) for a complete travel booking assistant example.

---

## Combining AgentCore services

All AgentCore services can be combined on a single agent:

```csharp
builder.Services
    .AddBedrockModel("us-east-1")
    .AddAgentCoreSessionManager(memoryId)      // persist sessions to Memory
    .AddAgentCoreMemory(memoryId)              // explicit memory store/retrieve/delete
    .AddAgentCoreCodeInterpreter()             // Python/JS/TS execution sandbox
    .AddAgentCoreBrowser()                     // managed headless Chrome
    .AddAgentCoreGatewayTools(gatewayUrl, auth) // gateway-hosted tools
    .AddJacquardAgent("You are a helpful assistant.");
```

The agent has access to all registered tools and uses the AgentCore Memory for session persistence.

---

## Why AgentCore?

| Capability | Without AgentCore | With AgentCore |
|---|---|---|
| Session persistence | In-memory (lost on restart) or custom database | Managed, scalable, built-in |
| Code execution | Subprocess on Lambda (security risk) | Isolated sandbox, stateful, managed |
| Browser automation | Self-managed Chrome (complex, expensive) | Managed headless Chrome, CDP endpoint |
| External tool access | Custom HTTP clients, auth management | Managed MCP proxy, built-in auth |
| Agent hosting | Custom ASP.NET Core setup | One-line `MapAgentCoreEndpoints()` |

AgentCore handles the infrastructure so you focus on the agent logic.
