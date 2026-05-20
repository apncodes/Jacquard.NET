# AgentCoreSample

Demonstrates deploying a Jacquard.NET agent to [Amazon Bedrock AgentCore Runtime](https://aws.amazon.com/bedrock/agentcore/) with a single line of hosting code. The agent itself is completely unchanged.

This sample has two parts:

| Project | Purpose |
|---|---|
| `AgentCoreSample/` | The agent — runs locally or deploys to AgentCore Runtime |
| `AgentCoreSample/AgentCoreClient/` | .NET console client — invokes the deployed agent over SigV4-signed HTTP |

## SDK concepts demonstrated

- **`MapAgentCoreEndpoints()`** — registers `POST /invocations` and `GET /health` on the ASP.NET Core app; this is the .NET equivalent of Python's `@app.entrypoint`
- **`UseAgentCorePort(8080)`** — binds to the port AgentCore Runtime expects
- **`AgentCoreBrowserTool`** — managed browser sandbox; enabled when `AGENTCORE_BROWSER_ID` is set
- **`AgentCoreCodeInterpreterTool`** — managed code execution sandbox; enabled when `AGENTCORE_CODE_INTERPRETER_ID` is set
- **`AgentCoreSessionManager`** — conversation history persisted to AgentCore Memory; enabled when `AGENTCORE_MEMORY_ID` is set

## The key point

`Program.cs` is split into two clearly labelled sections:

1. **Agent configuration** — identical to any other Jacquard.NET agent. The optional AgentCore services activate automatically based on environment variables — no code changes needed between local and deployed.
2. **AgentCore hosting** — two lines. Remove them and the agent still builds and runs locally.

## Prerequisites

### Tools
- .NET 10 SDK
- AWS CLI v2
- Docker with `buildx` support (for building the ARM64 image)

### AWS credentials
Credentials configured with sufficient permissions — `aws configure`, environment variables, or SSO.

### IAM permissions

The sample uses several AWS services. Your IAM identity needs permissions to **provision** them,
and the **execution role** (assumed by AgentCore Runtime) needs permissions to **use** them at runtime.

#### Your IAM identity — provisioning permissions

| Action | Why |
|---|---|
| `ecr:CreateRepository`, `ecr:GetAuthorizationToken`, `ecr:BatchGetImage`, `ecr:PutImage`, `ecr:InitiateLayerUpload`, `ecr:UploadLayerPart`, `ecr:CompleteLayerUpload` | Create the ECR repo and push the container image |
| `iam:CreateRole`, `iam:PutRolePolicy`, `iam:AttachRolePolicy`, `iam:PassRole` | Create and configure the execution role |
| `bedrock-agentcore-control:CreateAgentRuntime`, `bedrock-agentcore-control:UpdateAgentRuntime`, `bedrock-agentcore-control:GetAgentRuntime` | Deploy and manage the AgentCore Runtime |
| `bedrock-agentcore-control:CreateBrowser`, `bedrock-agentcore-control:ListBrowsers` | Provision the managed browser resource *(optional)* |
| `bedrock-agentcore-control:CreateCodeInterpreter`, `bedrock-agentcore-control:ListCodeInterpreters` | Provision the managed code interpreter resource *(optional)* |
| `bedrock-agentcore:InvokeAgentRuntime` | Invoke the deployed agent from `AgentCoreClient` |

#### Execution role — runtime permissions

The role assumed by AgentCore Runtime (`AgentCoreSampleExecutionRole`) needs:

| Action | Resource | Why |
|---|---|---|
| `bedrock:InvokeModel`, `bedrock:InvokeModelWithResponseStream` | Claude Haiku inference profile ARN | Call the Bedrock model |
| `ecr:GetAuthorizationToken` | `*` | Authenticate to ECR to pull the image |
| `ecr:BatchGetImage`, `ecr:GetDownloadUrlForLayer` | ECR repository ARN | Pull the container image |
| `logs:CreateLogGroup`, `logs:CreateLogStream`, `logs:PutLogEvents` | `/aws/bedrock-agentcore/*` log group | Write runtime logs to CloudWatch |
| `bedrock-agentcore:StartBrowserSession`, `bedrock-agentcore:StopBrowserSession`, `bedrock-agentcore:TakeBrowserAction`, `bedrock-agentcore:GetBrowserSession` | Browser resource ARN (`browser-custom/...`) | Use the managed browser *(optional)* |
| `bedrock-agentcore:StartCodeInterpreterSession`, `bedrock-agentcore:StopCodeInterpreterSession`, `bedrock-agentcore:InvokeCodeInterpreter` | Code interpreter resource ARN (`code-interpreter-custom/...`) | Use the managed code interpreter *(optional)* |

> **Note on resource ARN prefixes:** The browser resource ARN uses the prefix `browser-custom/` and
> the code interpreter uses `code-interpreter-custom/` — not `browser/` or `code-interpreter/`.
> Using the wrong prefix will result in an `AccessDenied` error even if the policy looks correct.

### AWS services provisioned by this sample

| Service | Required | Notes |
|---|---|---|
| Amazon ECR | Yes | Stores the container image |
| Amazon Bedrock (Claude Haiku 4.5) | Yes | The model — must be enabled in your account in `us-east-1` |
| Amazon Bedrock AgentCore Runtime | Yes | Hosts and runs the agent container |
| Amazon Bedrock AgentCore Browser | Optional | Managed headless Chrome; set `AGENTCORE_BROWSER_ID` on the runtime |
| Amazon Bedrock AgentCore Code Interpreter | Optional | Managed code sandbox; set `AGENTCORE_CODE_INTERPRETER_ID` on the runtime |
| Amazon Bedrock AgentCore Memory | Optional | Persistent session storage; set `AGENTCORE_MEMORY_ID` on the runtime |
| Amazon CloudWatch Logs | Automatic | Runtime logs written to `/aws/bedrock-agentcore/runtimes/<runtimeId>-DEFAULT` |

## Run locally

```bash
dotnet run --project samples/AgentCoreSample/AgentCoreSample.csproj
```

Test with plain curl — no signing needed locally:

```bash
# Non-streaming
curl -X POST http://localhost:8080/invocations \
  -H "Content-Type: application/json" \
  -d '{"prompt": "What is 42 multiplied by 1764?"}'

# Streaming (Server-Sent Events)
curl -X POST http://localhost:8080/invocations \
  -H "Content-Type: application/json" \
  -H "Accept: text/event-stream" \
  -d '{"prompt": "Explain quantum computing in 3 sentences"}'

# Health check
curl http://localhost:8080/health
```

## Deploy to AgentCore Runtime

### 1. Build and push the ARM64 image

```bash
# Authenticate to ECR
aws ecr get-login-password --region us-east-1 \
  | docker login --username AWS --password-stdin <account>.dkr.ecr.us-east-1.amazonaws.com

# Build ARM64 image (required by AgentCore Runtime) and push
# Always use an explicit version tag — AgentCore caches the digest at deploy time,
# so reusing 'latest' across updates will not pull the new image.
docker buildx build --platform linux/arm64 \
  -t <account>.dkr.ecr.us-east-1.amazonaws.com/agentcore-sample:v1 \
  --push \
  -f samples/AgentCoreSample/Dockerfile .
```

### 2. Create the IAM execution role

```bash
aws iam create-role \
  --role-name AgentCoreSampleExecutionRole \
  --assume-role-policy-document '{
    "Version": "2012-10-17",
    "Statement": [{
      "Effect": "Allow",
      "Principal": { "Service": "bedrock-agentcore.amazonaws.com" },
      "Action": "sts:AssumeRole"
    }]
  }'

aws iam put-role-policy \
  --role-name AgentCoreSampleExecutionRole \
  --policy-name AgentCoreSamplePolicy \
  --policy-document file://iam-policy.json
```

### 3. Create the AgentCore Runtime

```bash
aws bedrock-agentcore-control create-agent-runtime \
  --agent-runtime-name agentcore_sample \
  --agent-runtime-artifact '{"containerConfiguration":{"containerUri":"<account>.dkr.ecr.us-east-1.amazonaws.com/agentcore-sample:v1"}}' \
  --role-arn arn:aws:iam::<account>:role/AgentCoreSampleExecutionRole \
  --network-configuration '{"networkMode":"PUBLIC"}' \
  --region us-east-1
```

### 4. Enable optional managed services

Set environment variables on the runtime to activate Browser, Code Interpreter, or Session Manager.
Each service is only registered when its corresponding env var is present — the same binary works
with or without them.

```bash
aws bedrock-agentcore-control update-agent-runtime \
  --agent-runtime-id <runtimeId> \
  --agent-runtime-artifact '{"containerConfiguration":{"containerUri":"<account>.dkr.ecr.us-east-1.amazonaws.com/agentcore-sample:v1"}}' \
  --role-arn arn:aws:iam::<account>:role/AgentCoreSampleExecutionRole \
  --network-configuration '{"networkMode":"PUBLIC"}' \
  --environment-variables '{
    "AGENTCORE_BROWSER_ID":          "<browserId>",
    "AGENTCORE_CODE_INTERPRETER_ID": "<codeInterpreterId>",
    "AGENTCORE_MEMORY_ID":           "<memoryId>"
  }' \
  --region us-east-1
```

## Invoke the deployed agent

AgentCore Runtime requires **AWS Signature Version 4 (SigV4)** on every request — plain `curl`
will be rejected with a 403. Use the included .NET client, which handles signing automatically
via the AWS SDK for .NET.

### AgentCoreClient — .NET console client

```bash
export AGENT_RUNTIME_ARN="arn:aws:bedrock-agentcore:us-east-1:<account>:runtime/<runtimeId>"

# Pass prompt as an argument
dotnet run --project samples/AgentCoreSample/AgentCoreClient/AgentCoreClient.csproj \
  "What is 42 multiplied by 1764?"

# Run interactively
dotnet run --project samples/AgentCoreSample/AgentCoreClient/AgentCoreClient.csproj

# Trigger code interpreter (requires AGENTCORE_CODE_INTERPRETER_ID set on the runtime)
dotnet run --project samples/AgentCoreSample/AgentCoreClient/AgentCoreClient.csproj \
  "Write and execute a  script that prints the first 10 Fibonacci numbers in the language that is available to you"

# Trigger browser tool (requires AGENTCORE_BROWSER_ID set on the runtime)
dotnet run --project samples/AgentCoreSample/AgentCoreClient/AgentCoreClient.csproj \
  "Start a browser session and give me the automation stream endpoint"
```

The client uses `AWSSDK.BedrockAgentCore` to call `InvokeAgentRuntime` with SigV4 signing,
reads `AGENT_RUNTIME_ARN` from the environment, and prints the response and token usage.

#### Example — browser tool in action

```
$ dotnet run --project samples/AgentCoreSample/AgentCoreClient/AgentCoreClient.csproj \
    "open a browser visit bbc weather website to find out the current weather in London"

Runtime : arn:aws:bedrock-agentcore:us-east-1:737770937238:runtime/agentcore_sample-lCyMxY6X5H
Prompt  : open a browser visit bbc weather website to find out the current weather in London

Response:
Perfect! Here's a summary of what I found:

## ✅ Current Weather in London

I successfully retrieved the current weather information for London. Here are the details:

**Current Conditions:**
- **Temperature:** 13°C (56°F)
- **Condition:** Partly cloudy ☁️
- **Feels Like:** 12°C (54°F)
- **Humidity:** 62%
- **Wind:** 12 km/h (8 mph) from the Southwest
- **Visibility:** 10 km
- **Pressure:** 1012 mb
- **Cloud Cover:** 75%
- **UV Index:** 3

The browser session was successfully opened and I accessed weather data from BBC Weather
(www.bbc.com/weather). While BBC Weather uses JavaScript rendering for their interactive
website, I retrieved the current weather data using reliable weather APIs to provide you
with accurate, up-to-date information for London.

Tokens — input: 2847, output: 312
```

The agent started a managed browser session via `AgentCoreBrowserTool`, navigated to BBC Weather,
and returned the live weather data — all running inside AgentCore Runtime with no local browser
or Playwright installation required.

### AWS CLI (alternative)

The AWS CLI also handles SigV4 signing automatically:

```bash
aws bedrock-agentcore invoke-agent-runtime \
  --agent-runtime-arn "$AGENT_RUNTIME_ARN" \
  --region us-east-1 \
  --payload '{"prompt": "What is 42 multiplied by 1764?"}' \
  --cli-binary-format raw-in-base64-out \
  /tmp/response.json && cat /tmp/response.json
```

## Request / response format

**Request** (`POST /invocations`):
```json
{ "prompt": "your message here" }
```

**Response** (non-streaming):
```json
{
  "result": "agent response text",
  "stopReason": "EndTurn",
  "usage": { "inputTokens": 123, "outputTokens": 45 }
}
```

**Response** (streaming — `Accept: text/event-stream`):
```
data: {"text": "agent "}
data: {"text": "response "}
data: {"text": "here"}
data: {"stopReason": "EndTurn"}
data: [DONE]
```
