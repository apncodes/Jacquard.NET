# StrandsAgents.Tools

Built-in tools for [Strands Agents .NET](https://github.com/apncodes/strands.net) agents.

```bash
dotnet add package StrandsAgents.Tools
```

| Tool | Description |
|---|---|
| `CalculatorTool` | Safe arithmetic evaluation |
| `FileReadTool(basePath)` | Sandboxed file reads — rejects path traversal |
| `FileWriteTool(basePath)` | Sandboxed file writes and appends |
| `HttpRequestTool` | GET / POST via HttpClient |
| `McpToolProvider` | Connect any MCP server (stdio or SSE) |

```csharp
using StrandsAgents.Core;
using StrandsAgents.Tools;

// Calculator
var agent = new Agent(model, tools: [new CalculatorTool_Calculate_Tool(new CalculatorTool())]);

// MCP
await using var mcp = await McpToolProvider.CreateForStdioAsync(
    "npx", ["-y", "@modelcontextprotocol/server-filesystem", "/tmp"]);
var agent = new Agent(model, tools: await mcp.GetToolsAsync());
```
