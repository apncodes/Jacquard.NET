# AotLambda — NativeAOT Jacquard Agent on AWS Lambda

This sample publishes a Jacquard.NET agent as a **NativeAOT** AWS Lambda function using the `provided.al2023` custom runtime. The result is a self-contained native binary with no .NET runtime dependency.

**Recommended: `arm64` (Graviton2) at 1024 MB** — 89.6ms average cold-start, 19/20 runs under 100ms.

## Why AOT?

Most agent frameworks carry a JIT tax on Lambda. The runtime loads, assemblies resolve, the hot path compiles — all before your first request. For a typical managed .NET agent, that's 200–500ms of init duration you pay on every cold start.

Jacquard.NET is designed to eliminate that tax entirely. The `[Tool]` attribute triggers a Roslyn source generator that emits all tool schema and dispatch code at compile time. There is no runtime reflection, no dynamic type discovery, no `JsonSerializer` with reflection. The result is a binary that Lambda can load and start in under 100ms — on Graviton2, consistently, across all memory tiers from 512 MB to 2048 MB.

Across 60 measured cold starts on arm64 Graviton2 (512 MB, 1024 MB, 2048 MB, 3008 MB), **88% came in under 100ms with an overall average of 93.3ms**. The binary uses only 51–55 MB of memory at runtime — meaning you get sub-100ms cold starts on the smallest practical Lambda configuration, not just on oversized instances.

That's the design goal: a .NET agent that starts fast on small hardware, so you can run it cost-effectively at scale without trading cold-start latency for instance size.

## Why arm64 (Graviton2)?

The arm64 binary is 14 MB. The x86_64 binary is 25 MB. Smaller binary = less to load from storage at cold start. That's the primary reason arm64 is faster — not CPU speed, but binary size. Graviton2 also costs ~20% less per GB-second than x86_64, so you get better cold-start performance at lower cost.

x86_64 at 1024 MB averaged 119.8ms across 20 cold starts and never broke 100ms. arm64 at 512 MB averaged 95.3ms. The architecture choice matters more than the memory size.

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
| Model | `us.anthropic.claude-haiku-4-5-20251001-v1:0` (cross-region inference profile) |
| Cold-start method | `update-function-configuration` between each invocation (forces new execution environment) |
| Workload | Single tool-using agent: user asks for weather → model calls `GetWeather` tool → model synthesizes response |
| LLM calls per invocation | 2 (one to decide tool call, one to synthesize result with tool output) |
| Runs | 20 cold starts (512 MB, 1024 MB); 10 cold starts (2048 MB, 3008 MB) |

### Memory step-up summary — arm64 Graviton2

| Memory | Avg init | Min | Max | Under 100ms | vs 512 MB |
|---|---|---|---|---|---|
| 512 MB | 95.3 ms | 77.0 ms | 108.7 ms | 17/20 | baseline |
| **1024 MB** | **89.6 ms** | **77.7 ms** | **101.6 ms** | **19/20** | **−5.6 ms** |
| 2048 MB | 95.1 ms | 81.3 ms | 99.1 ms | 10/10 | −0.2 ms |
| 3008 MB | 95.1 ms | 82.9 ms | 110.1 ms | 7/10 | −0.2 ms |

**Sweet spot is 1024 MB.** The jump from 512 MB to 1024 MB saves ~5.6ms and pushes 19/20 runs under 100ms. Beyond 1024 MB there is no further improvement — 2048 MB and 3008 MB regress back to ~95ms average, likely due to Lambda provisioning different hardware at higher memory tiers.

### Results — arm64 Graviton2, 512 MB

| Run | Init (ms) | Run | Init (ms) |
|---|---|---|---|
| 1 | 108.69 | 11 | 95.85 |
| 2 | 107.45 | 12 | 83.47 |
| 3 | 94.29 | 13 | 100.39 |
| 4 | 97.22 | 14 | 92.29 |
| 5 | 97.76 | 15 | 86.62 |
| 6 | 98.88 | 16 | 97.17 |
| 7 | 94.36 | 17 | 76.99 |
| 8 | 97.72 | 18 | 99.55 |
| 9 | 98.78 | 19 | 81.99 |
| 10 | 99.12 | 20 | 97.00 |

| Metric | ms |
|---|---|
| Average | **95.3** |
| Min | 77.0 |
| Max | 108.7 |
| Under 100ms | **17 / 20** |

### Results — arm64 Graviton2, 1024 MB

| Run | Init (ms) | Run | Init (ms) |
|---|---|---|---|
| 1 | 95.47 | 11 | 77.71 |
| 2 | 89.76 | 12 | 83.17 |
| 3 | 83.46 | 13 | 97.71 |
| 4 | 84.51 | 14 | 82.68 |
| 5 | 101.55 | 15 | 94.08 |
| 6 | 96.72 | 16 | 97.33 |
| 7 | 90.92 | 17 | 79.37 |
| 8 | 80.86 | 18 | 99.94 |
| 9 | 97.58 | 19 | 82.14 |
| 10 | 96.86 | 20 | 81.00 |

| Metric | ms |
|---|---|
| Average | **89.6** |
| Min | 77.7 |
| Max | 101.6 |
| Under 100ms | **19 / 20** |

### Results — arm64 Graviton2, 2048 MB

| Run | Init (ms) | Run | Init (ms) |
|---|---|---|---|
| 1 | 97.94 | 6 | 98.90 |
| 2 | 99.11 | 7 | 97.44 |
| 3 | 90.26 | 8 | 98.27 |
| 4 | 81.28 | 9 | 92.76 |
| 5 | 97.87 | 10 | 97.47 |

| Metric | ms |
|---|---|
| Average | 95.1 |
| Min | 81.3 |
| Max | 99.1 |
| Under 100ms | 10 / 10 |

### Results — arm64 Graviton2, 3008 MB

| Run | Init (ms) | Run | Init (ms) |
|---|---|---|---|
| 1 | 82.90 | 6 | 110.05 |
| 2 | 99.79 | 7 | 106.57 |
| 3 | 101.80 | 8 | 98.31 |
| 4 | 84.22 | 9 | 83.46 |
| 5 | 83.78 | 10 | 99.84 |

| Metric | ms |
|---|---|
| Average | 95.1 |
| Min | 82.9 |
| Max | 110.1 |
| Under 100ms | 7 / 10 |

### Results — x86_64, 1024 MB

| Run | Init (ms) | Run | Init (ms) |
|---|---|---|---|
| 1 | 113.09 | 11 | 112.01 |
| 2 | 107.96 | 12 | 165.20 |
| 3 | 113.60 | 13 | 138.43 |
| 4 | 101.53 | 14 | 114.96 |
| 5 | 121.22 | 15 | 102.56 |
| 6 | 116.81 | 16 | 123.84 |
| 7 | 118.65 | 17 | 110.55 |
| 8 | 129.82 | 18 | 107.72 |
| 9 | 128.41 | 19 | 114.56 |
| 10 | 108.65 | 20 | 146.41 |

| Metric | ms |
|---|---|
| Average | 119.8 |
| Min | 101.5 |
| Max | 165.2 |
| Under 100ms | 0 / 20 |

### Full comparison

| Configuration | Avg init | Min | Max | Under 100ms | Binary | Price/GB-s |
|---|---|---|---|---|---|---|
| **arm64 Graviton2, 1024 MB** | **89.6 ms** | 77.7 ms | 101.6 ms | **19/20** | 14 MB | ~20% cheaper |
| arm64 Graviton2, 512 MB | 95.3 ms | 77.0 ms | 108.7 ms | 17/20 | 14 MB | ~20% cheaper |
| arm64 Graviton2, 2048 MB | 95.1 ms | 81.3 ms | 99.1 ms | 10/10 | 14 MB | ~20% cheaper |
| arm64 Graviton2, 3008 MB | 95.1 ms | 82.9 ms | 110.1 ms | 7/10 | 14 MB | ~20% cheaper |
| x86_64, 1024 MB | 119.8 ms | 101.5 ms | 165.2 ms | 0/20 | 25 MB | baseline |

**Verdict:** 1024 MB is the sweet spot. It's the only tier that consistently beats 95ms average and gets 19/20 runs under 100ms. Higher memory tiers show no benefit — the bottleneck at 2048 MB+ is not CPU or memory bandwidth but binary loading and static initialization, which don't scale with memory. x86_64 at any memory size doesn't break 100ms.

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

**`NU1102: Unable to find package Jacquard.Core with version >= X`**
The NuGet package hasn't propagated yet. Wait a few minutes and retry, or use project references (already configured in this sample's `.csproj`).

**`IL2104: Assembly 'AWSSDK.Core' produced trim warnings`**
Suppressed in the `.csproj` — these come from AWS SDK internals and are safe for this usage pattern.
