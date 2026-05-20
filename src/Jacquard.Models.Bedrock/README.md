# Jacquard.Models.Bedrock

Amazon Bedrock model provider for [Strands Agents .NET](https://github.com/apncodes/Jacquard.net).

```bash
dotnet add package Jacquard.Core
dotnet add package Jacquard.Models.Bedrock
```

```csharp
using Jacquard.Core;
using Jacquard.Models.Bedrock;

IModel model = new BedrockModel(
    region: "us-east-1",
    modelId: "us.anthropic.claude-sonnet-4-5-v1:0");

var agent = new Agent(model, systemPrompt: "You are a helpful assistant.");
var result = await agent.InvokeAsync("Hello!");
```

Supports Claude, Nova, Llama, and all other Bedrock-hosted models. Requires AWS credentials
configured via environment variables, `~/.aws/credentials`, or IAM role.
