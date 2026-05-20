# Jacquard.Tools

Built-in tools for [Strands Agents .NET](https://github.com/apncodes/Jacquard.net) agents.

```bash
dotnet add package Jacquard.Tools
```

| Tool | Description |
|---|---|
| `CalculatorTool` | Safe arithmetic evaluation |
| `FileReadTool(basePath)` | Sandboxed file reads — rejects path traversal |
| `FileWriteTool(basePath)` | Sandboxed file writes and appends |
| `HttpRequestTool` | GET / POST via `IHttpClientFactory` |
| `McpToolProvider` | Connect any MCP server (stdio or SSE) |

```csharp
using Jacquard.Core;
using Jacquard.Tools;

// Calculator via toolProviders: (recommended)
var agent = new Agent(model, toolProviders: [new CalculatorTool()]);

// MCP — connect a filesystem MCP server
await using var mcp = await McpToolProvider.CreateForStdioAsync(
    "npx", ["-y", "@modelcontextprotocol/server-filesystem", "/tmp"]);
var agent = new Agent(model, tools: await mcp.GetToolsAsync());
```

Full documentation: [github.com/apncodes/Jacquard.net](https://github.com/apncodes/Jacquard.net)
