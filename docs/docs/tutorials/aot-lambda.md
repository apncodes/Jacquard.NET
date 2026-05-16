---
sidebar_position: 3
---

# Deploy to Lambda with AOT

**Time:** ~30 minutes  
**What you'll build:** A Strands Agents .NET agent published as a NativeAOT AWS Lambda function with sub-100ms cold start.

## Why AOT?

Standard .NET Lambda functions use the JIT runtime. On first invocation (cold start), the runtime must load, JIT-compile the code, and initialize the agent — typically 200–500ms.

NativeAOT compiles everything to native machine code at build time. There is no JIT warm-up. Cold-start init duration drops to under 100ms — often under 50ms for simple agents.

The Strands Agents .NET tool system is designed for this: the `[Tool]` attribute triggers a Roslyn source generator that emits compile-time `ITool` wrappers. Zero runtime reflection means zero trimming surprises.

## Prerequisites

- .NET 10 SDK
- AWS CLI configured with credentials
- Amazon Bedrock access enabled
- **Linux build environment** — NativeAOT cross-compilation from macOS to `linux-x64` requires a Linux linker. Use one of:
  - A Linux machine or WSL2
  - Docker: `docker run --rm -v $(pwd):/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet publish ...`
  - EC2 instance (see the [AotLambda sample README](https://github.com/apncodes/StrandsAgents.net/tree/main/samples/AotLambda))

## Step 1: Create the project

```bash
dotnet new console -n AotWeatherAgent
cd AotWeatherAgent
dotnet add package StrandsAgents.Core
dotnet add package StrandsAgents.Models.Bedrock
dotnet add package Amazon.Lambda.Core
dotnet add package Amazon.Lambda.RuntimeSupport
dotnet add package Amazon.Lambda.Serialization.SystemTextJson
dotnet add package StrandsAgents.SourceGenerator
```

## Step 2: Configure for AOT

Edit `AotWeatherAgent.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <StripSymbols>false</StripSymbols>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <NoWarn>$(NoWarn);IL2026;IL3050;IL2104</NoWarn>
  </PropertyGroup>
</Project>
```

## Step 3: Write the Lambda handler

Replace `Program.cs`:

```csharp
using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;
using System.Text.Json.Serialization;
using AotWeatherAgent;

var handler = async (string input, ILambdaContext context) =>
{
    var agent = new Agent(
        model: new BedrockModel(
            region: Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1",
            modelId: "us.anthropic.claude-haiku-4-5-20251001-v1:0"),
        systemPrompt: "You are a helpful weather assistant.",
        toolProviders: [new WeatherTools()]);

    var result = await agent.InvokeAsync(input);
    return result.Message;
};

await LambdaBootstrapBuilder
    .Create(handler, new SourceGeneratorLambdaJsonSerializer<LambdaJsonContext>())
    .Build()
    .RunAsync();

[JsonSerializable(typeof(string))]
public partial class LambdaJsonContext : JsonSerializerContext { }

namespace AotWeatherAgent
{
    public partial class WeatherTools
    {
        [Tool("Returns the current weather for a city")]
        public string GetWeather(string city) => $"Sunny, 22°C in {city}";
    }
}
```

:::important AOT serialization
Use `SourceGeneratorLambdaJsonSerializer<T>` instead of `DefaultLambdaJsonSerializer`. The default serializer uses reflection which is disabled in AOT.
:::

## Step 4: Build on Linux

On a Linux machine (or in Docker):

```bash
dotnet publish -c Release -r linux-x64 --output ./publish
cp ./publish/AotWeatherAgent ./bootstrap
zip -j function.zip bootstrap
```

## Step 5: Deploy to Lambda

```bash
# Create the function
aws lambda create-function \
  --function-name aot-weather-agent \
  --runtime provided.al2023 \
  --handler bootstrap \
  --role arn:aws:iam::YOUR_ACCOUNT:role/YOUR_LAMBDA_ROLE \
  --zip-file fileb://function.zip \
  --memory-size 512 \
  --timeout 30 \
  --region us-east-1

# Wait for it to be active
aws lambda wait function-active --function-name aot-weather-agent --region us-east-1
```

## Step 6: Test it

```bash
aws lambda invoke \
  --function-name aot-weather-agent \
  --payload '"What is the weather in London?"' \
  --cli-binary-format raw-in-base64-out \
  --log-type Tail \
  --region us-east-1 \
  --query 'LogResult' --output text \
  response.json | base64 --decode | grep "Init Duration"

cat response.json
```

You should see `Init Duration: ~100ms` in the logs and the agent's response in `response.json`.

## Benchmark results

From the [AotLambda sample](https://github.com/apncodes/StrandsAgents.net/tree/main/samples/AotLambda), measured on `us-east-1`, 512 MB, `provided.al2023`:

| Measurement | Value |
|---|---|
| Cold start init (avg) | 118 ms |
| Cold start init (min) | 107 ms |
| Memory used | 52 MB |
| Binary size | 14 MB |

## Next steps

- **[AotLambda sample](https://github.com/apncodes/StrandsAgents.net/tree/main/samples/AotLambda)** — the full sample with detailed benchmark methodology
- **[DurableWorkflow sample](https://github.com/apncodes/StrandsAgents.net/tree/main/samples/DurableWorkflow)** — multi-step AOT agents with Step Functions durability
- **[FAQ: AOT trimming warnings](../faq#aot-trimming-warnings)** — common issues and fixes
