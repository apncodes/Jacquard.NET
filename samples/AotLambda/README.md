# AotLambda — NativeAOT Strands Agent on AWS Lambda

This sample publishes a Strands Agents .NET agent as a **NativeAOT** AWS Lambda function using the `provided.al2023` custom runtime. The result is a self-contained native binary with no .NET runtime dependency.

**Recommended: use `arm64` (Graviton2).** Measured ~96ms average cold-start init duration at 512 MB — 18% faster than x86_64, smaller binary, and ~20% cheaper per GB-second.

## Why AOT?

Standard .NET Lambda functions use the JIT runtime. On first invocation (cold start), the runtime must load, JIT-compile the code, and initialize the agent. This typically takes 200–500ms.

NativeAOT compiles everything to native machine code at build time. There is no JIT warm-up. Cold-start init duration drops to under 100ms on Graviton2 — compared to 200–500ms for the equivalent JIT runtime.

The Strands Agents .NET tool system is designed for this: the `[Tool]` attribute triggers a Roslyn source generator that emits compile-time `ITool` wrappers. Zero runtime reflection means zero trimming surprises.

## Prerequisites

- [.NET 10 SDK](https://dot.net)
- [AWS CLI](https://aws.amazon.com/cli/) configured with credentials
- Amazon Bedrock access enabled in your AWS account (Claude Haiku model)
- **Linux build environment** — NativeAOT requires a Linux linker. Use one of:
  - A Linux machine or WSL2
  - Docker: `docker run --rm -v $(pwd):/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash -c "apt-get update -qq && apt-get install -y -qq clang zlib1g-dev && dotnet publish ..."`
  - GitHub Actions (Ubuntu runner)

## Build and publish

### arm64 (recommended — Graviton2)

```bash
# From the repo root (strands.net/)
docker run --rm -v $(pwd):/src -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  bash -c "apt-get update -qq && apt-get install -y -qq clang zlib1g-dev && \
  dotnet publish samples/AotLambda/AotLambda.csproj \
    --configuration Release \
    --runtime linux-arm64 \
    --output samples/AotLambda/publish-arm64 \
    -p:StripSymbols=true"

# Package for Lambda (binary must be named 'bootstrap')
cp samples/AotLambda/publish-arm64/AotLambda samples/AotLambda/publish-arm64/bootstrap
zip -j samples/AotLambda/publish-arm64/function-arm64.zip samples/AotLambda/publish-arm64/bootstrap
```

### x86_64

```bash
docker run --rm -v $(pwd):/src -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  bash -c "apt-get update -qq && apt-get install -y -qq clang zlib1g-dev && \
  dotnet publish samples/AotLambda/AotLambda.csproj \
    --configuration Release \
    --runtime linux-x64 \
    --output samples/AotLambda/publish \
    -p:StripSymbols=true"

cp samples/AotLambda/publish/AotLambda samples/AotLambda/publish/bootstrap
zip -j samples/AotLambda/publish/function.zip samples/AotLambda/publish/bootstrap
```

## Deploy to AWS Lambda

### arm64 (Graviton2)

```bash
aws lambda create-function \
  --function-name strands-aot-demo-arm64 \
  --runtime provided.al2023 \
  --handler bootstrap \
  --architectures arm64 \
  --role arn:aws:iam::YOUR_ACCOUNT:role/YOUR_LAMBDA_ROLE \
  --zip-file fileb://samples/AotLambda/publish-arm64/function-arm64.zip \
  --memory-size 512 \
  --timeout 30 \
  --region us-east-1

# Update an existing function
aws lambda update-function-code \
  --function-name strands-aot-demo-arm64 \
  --zip-file fileb://samples/AotLambda/publish-arm64/function-arm64.zip \
  --region us-east-1
```

### x86_64

```bash
aws lambda create-function \
  --function-name strands-aot-demo \
  --runtime provided.al2023 \
  --handler bootstrap \
  --role arn:aws:iam::YOUR_ACCOUNT:role/YOUR_LAMBDA_ROLE \
  --zip-file fileb://samples/AotLambda/publish/function.zip \
  --memory-size 512 \
  --timeout 30 \
  --region us-east-1
```

**Required IAM permissions for the Lambda execution role:**
- `bedrock:InvokeModel` and `bedrock:InvokeModelWithResponseStream` on the model ARN

## Invoke

```bash
aws lambda invoke \
  --function-name strands-aot-demo-arm64 \
  --payload '"What is the weather in London?"' \
  --cli-binary-format raw-in-base64-out \
  response.json

cat response.json
```

## Measure cold-start duration

Force a cold start by updating the function configuration (this resets the execution environment):

```bash
aws lambda update-function-configuration \
  --function-name strands-aot-demo-arm64 \
  --description "cold-start-test-$(date +%s)" \
  --region us-east-1

aws lambda wait function-updated --function-name strands-aot-demo-arm64 --region us-east-1

aws lambda invoke \
  --function-name strands-aot-demo-arm64 \
  --payload '"What is the weather in London?"' \
  --cli-binary-format raw-in-base64-out \
  --log-type Tail \
  --region us-east-1 \
  --query 'LogResult' \
  --output text \
  /dev/null | base64 --decode | grep "Init Duration"
```

## Benchmarks

### Test conditions

| Parameter | Value |
|---|---|
| Date | 2026-05-19 |
| AWS region | `us-east-1` |
| Lambda runtime | `provided.al2023` |
| Lambda memory | 512 MB |
| Model | `us.anthropic.claude-haiku-4-5-20251001-v1:0` (cross-region inference profile) |
| Cold-start method | `update-function-configuration` between each invocation (forces new execution environment) |
| Workload | Single tool-using agent: user asks for weather → model calls `GetWeather` tool → model synthesizes response |
| LLM calls per invocation | 2 (one to decide tool call, one to synthesize result with tool output) |
| Runs | 5 cold starts per architecture |

### Results — arm64 (Graviton2)

| Run | Init Duration (ms) | Total Duration (ms) | Memory Used |
|---|---|---|---|
| 1 | 112.22 | 2,311 | 53 MB |
| 2 | 94.96 | 2,081 | 53 MB |
| 3 | 82.77 | 3,700 | 51 MB |
| 4 | 102.94 | 2,130 | 51 MB |
| 5 | 89.14 | 2,372 | 53 MB |

| Metric | Init Duration (ms) | Total Duration (ms) |
|---|---|---|
| **Average** | **96.4** | 2,519 |
| **Min** | **82.8** | 2,081 |
| Max | 112.2 | 3,700 |

### Results — x86_64

| Run | Init Duration (ms) | Total Duration (ms) | Memory Used |
|---|---|---|---|
| 1 | 107.39 | 2,462 | 52 MB |
| 2 | 140.28 | 2,970 | 52 MB |
| 3 | 124.48 | 2,604 | 52 MB |
| 4 | 108.74 | 2,385 | 52 MB |
| 5 | 107.26 | 2,457 | 52 MB |

| Metric | Init Duration (ms) | Total Duration (ms) |
|---|---|---|
| Average | 117.6 | 2,576 |
| Min | 107.3 | 2,385 |
| Max | 140.3 | 2,970 |

### Architecture comparison

| | arm64 (Graviton2) | x86_64 |
|---|---|---|
| Avg init duration | **96.4 ms** | 117.6 ms |
| Min init duration | **82.8 ms** | 107.3 ms |
| Binary size (uncompressed) | **14 MB** | 25 MB |
| Zip size | **5.4 MB** | ~14 MB |
| Memory used | 51–53 MB | 52 MB |
| Price per GB-second | ~20% cheaper | baseline |

### What the numbers mean

**Init Duration** — this is the AOT advantage. The native binary loads and initializes in under 100ms on Graviton2. The equivalent JIT runtime (`dotnet10` managed runtime) typically shows 200–500ms init duration for the same code.

**Total Duration (~2,500ms avg)** — dominated by two Bedrock API calls (~1,100–1,300ms each for Claude Haiku). The framework overhead (event loop, tool dispatch, serialization) is under 50ms. Warm invocations show the same ~2,400ms because model inference latency doesn't change between cold and warm.

**Memory (~52 MB)** — includes the agent, Bedrock SDK, tool class, and two full LLM round-trips. The JIT runtime baseline for the same code typically uses 80–120 MB.

**Binary size** — arm64 produces a 14 MB binary vs 25 MB for x86_64. Smaller binary = faster load from storage = lower init duration.

## How it works

```
[Tool] attribute on partial class WeatherTools
         ↓
Roslyn source generator (compile time)
         ↓
WeatherTools_GetWeather_Tool.g.cs  ← generated ITool wrapper
WeatherTools_IToolProvider.g.cs    ← generated IToolProvider
         ↓
ILC (IL Compiler) — compiles everything to native arm64/x64
         ↓
Single native binary: AotLambda (14 MB arm64 / 25 MB x64)
         ↓
Lambda cold start: load binary → execute
```

The source generator emits all tool schema and dispatch code at compile time. There is no `Type.GetMethod()`, no `Activator.CreateInstance()`, no `JsonSerializer` with reflection — the hot path is fully AOT-safe.

## Troubleshooting

**`clang: error: invalid linker name in argument '-fuse-ld=bfd'`**
You're building on macOS without Docker. Use the Docker command above.

**`NU1102: Unable to find package StrandsAgents.Core with version >= X`**
The NuGet package hasn't propagated yet. Wait a few minutes and retry, or use project references (already configured in this sample's `.csproj`).

**`IL2104: Assembly 'AWSSDK.Core' produced trim warnings`**
Suppressed in the `.csproj` — these come from AWS SDK internals and are safe for this usage pattern.
