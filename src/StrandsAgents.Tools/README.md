# Strands.Tools

Built-in tools for [Strands.NET](https://github.com/apncodes/strands.net) agents.

```bash
dotnet add package Strands.Tools
```

| Tool | Description |
|---|---|
| `CalculatorTool` | Safe arithmetic evaluation via NCalc |
| `FileReadTool(basePath)` | Sandboxed file reads — rejects path traversal |
| `FileWriteTool(basePath)` | Sandboxed file writes and appends |
| `HttpRequestTool` | GET / POST / PUT / DELETE via HttpClient |
| `McpToolProvider` | Connect any MCP server (stdio or SSE) |

```csharp
// Calculator
var agent = new Agent(model, tools: [new CalculatorTool_Calculate_Tool(new CalculatorTool())]);

// MCP
await using var mcp = await McpToolProvider.CreateForStdioAsync(
    "npx", ["-y", "@modelcontextprotocol/server-filesystem", "/tmp"]);
var agent = new Agent(model, tools: await mcp.GetToolsAsync());
```
