# DurableWorkflow — Decomposed Sequential Pipeline with Platform-Managed Durability

## Pattern: Decomposed Sequential Pipeline

This sample demonstrates the **Decomposed Sequential Pipeline** pattern — a multi-agent architecture where:

1. A complex task is broken into discrete, sequential stages
2. Each stage is a separate, stateless agent (Lambda function)
3. The orchestrator (Step Functions) owns the state between stages
4. Each stage is independently retryable without re-running prior stages

This is distinct from the in-process `PipelineOrchestrator` already in Strands Agents .NET. The in-process pipeline runs all stages in one Lambda invocation. The decomposed pipeline runs each stage in a separate Lambda invocation, with Step Functions as the checkpoint manager between them.

## Why decompose? The durability problem

Consider a research pipeline that:
- Calls an LLM to decompose a topic into focus areas (Stage 1: ~5s)
- Calls an LLM once per focus area to gather findings (Stage 2: ~5-15s × 3 areas = 15-45s)
- Calls an LLM to synthesize a final report (Stage 3: ~5s)

**Total: 25-55 seconds of LLM work.** In a real implementation with external API calls (search engines, knowledge bases, databases), Stage 2 could easily take 5-10 minutes.

### The single-Lambda problem

```
Single Lambda (no durability):

[Stage 1: Plan] → [Stage 2: Execute area 1] → [Stage 2: Execute area 2] → FAIL at area 3
                                                                                    ↓
                                                              Restart from Stage 1. All work lost.
                                                              Cost: 3 wasted Bedrock calls.
```

If the Lambda times out or throws at area 3, you've lost the plan and the first two areas of research. You restart from scratch. Every failure multiplies your cost and latency.

### The decomposed pipeline solution

```
Decomposed Pipeline (Step Functions durability):

[Stage 1: Plan] ──checkpoint──→ [Stage 2: Execute] ──checkpoint──→ [Stage 3: Summarize]
       ↓                                  ↓                                  ↓
  Stored in                         Stored in                          Final output
  execution state                   execution state

If Stage 2 fails:
  Step Functions retries Stage 2 only.
  Stage 1's output is preserved. It is NEVER re-run.
  Cost: only the failed LLM calls are retried.
```

**The key insight:** Step Functions stores each stage's output in the execution state. A Lambda failure does not lose prior work. The retry starts exactly where the failure occurred.

## Architecture

```
Input: { "topic": "serverless AI agents on AWS" }
         │
         ▼
┌─────────────────────────────────────────────────────┐
│  Stage 1: PlanAgent Lambda                          │
│                                                     │
│  Agent task: Decompose topic into 3 focus areas,    │
│  each with a specific research question.            │
│                                                     │
│  Agent tools: none (pure LLM reasoning)             │
│  Typical duration: 3-8 seconds                      │
└─────────────────────┬───────────────────────────────┘
                      │
          Step Functions stores output as checkpoint
                      │
                      ▼ ResearchPlan { topic, objective, focusAreas[] }
┌─────────────────────────────────────────────────────┐
│  Stage 2: ExecuteAgent Lambda                       │
│                                                     │
│  Agent task: For each focus area, call the          │
│  Research tool and answer the research question.    │
│                                                     │
│  Agent tools: [Research] — looks up information     │
│  Typical duration: 15-45 seconds (1 LLM call/area)  │
│                                                     │
│  ← This stage has Retry (2×) + Catch in the ASL.   │
│    If it fails, Step Functions retries it.          │
│    Stage 1 is NOT re-run.                           │
└─────────────────────┬───────────────────────────────┘
                      │
          Step Functions stores output as checkpoint
                      │
                      ▼ ResearchFindings { topic, objective, findings[] }
┌─────────────────────────────────────────────────────┐
│  Stage 3: SummarizeAgent Lambda                     │
│                                                     │
│  Agent task: Synthesize all findings into a         │
│  150-200 word executive summary.                    │
│                                                     │
│  Agent tools: none (pure LLM synthesis)             │
│  Typical duration: 3-8 seconds                      │
└─────────────────────┬───────────────────────────────┘
                      │
                      ▼ WorkflowResult { topic, objective, summary, focusAreas[] }
```

## How state flows between stages

Step Functions passes the **entire output** of each Lambda as the **input** to the next Lambda. The `ResultPath: "$"` setting in `statemachine.asl.json` replaces the current execution state with the Lambda's return value.

```
PlanAgent returns:
{
  "topic": "serverless AI agents on AWS",
  "objective": "Understand the current state and best practices",
  "focusAreas": [
    { "name": "Architecture patterns", "question": "...", "rationale": "..." },
    { "name": "Cold start optimization", "question": "...", "rationale": "..." },
    { "name": "Cost model", "question": "...", "rationale": "..." }
  ]
}

↓ Step Functions stores this. Passes it as input to ExecuteAgent.

ExecuteAgent returns:
{
  "topic": "serverless AI agents on AWS",
  "objective": "...",
  "findings": [
    { "name": "Architecture patterns", "question": "...", "answer": "..." },
    { "name": "Cold start optimization", "question": "...", "answer": "..." },
    { "name": "Cost model", "question": "...", "answer": "..." }
  ]
}

↓ Step Functions stores this. Passes it as input to SummarizeAgent.
```

No shared database. No message queue. No session manager. **The state machine is the state store.**

## What each agent knows

| Agent | Knows about Stage 1? | Knows about Stage 2? | Knows about Stage 3? |
|---|---|---|---|
| PlanAgent | — | No | No |
| ExecuteAgent | No | — | No |
| SummarizeAgent | No | No | — |

Each agent is completely isolated. It receives typed input, does its job, and returns typed output. The pipeline topology is defined entirely in `statemachine.asl.json` — not in any agent's code.

## Model selection: right-sizing per stage

One of the advantages of the Decomposed Sequential Pipeline pattern is that **each stage can independently choose its model**. In a single-Lambda pipeline, you're locked into one model for the entire workflow. Here, each Lambda is a separate deployment unit with its own model configuration.

| Stage | Model | Rationale |
|---|---|---|
| PlanAgent | `us.anthropic.claude-sonnet-4-6` | Structured reasoning, reliable JSON output. Planning requires careful decomposition — Sonnet's reasoning depth produces well-formed focus areas. |
| ExecuteAgent | `us.anthropic.claude-sonnet-4-6` | Superior tool use and instruction following. Research execution makes multiple tool calls — Sonnet maximises quality for the most expensive stage. |
| SummarizeAgent | `us.amazon.nova-pro-v1:0` | AWS-native model, fast synthesis, cost-efficient. Summarization is a writing task, not a reasoning task — Nova Pro excels here and costs less than Sonnet. |

This also demonstrates Bedrock's model flexibility: you can mix Claude and Nova models in the same workflow, choosing the right model for each cognitive task. Swap any model by changing one line in the Lambda's `Function.cs` — no other stage is affected.

## Prerequisites

- .NET 10 SDK
- AWS CLI configured with credentials
- Amazon Bedrock access enabled (`us.anthropic.claude-haiku-4-5-20251001-v1:0`)
- An IAM role for Lambda execution with:
  - `bedrock:InvokeModel` and `bedrock:InvokeModelWithResponseStream` on `*`
  - `logs:CreateLogGroup`, `logs:CreateLogStream`, `logs:PutLogEvents`

## Deploy

```bash
chmod +x deploy.sh
export LAMBDA_ROLE_ARN=arn:aws:iam::YOUR_ACCOUNT:role/YOUR_LAMBDA_ROLE
./deploy.sh
```

## Trigger an execution

```bash
aws stepfunctions start-execution \
  --state-machine-arn YOUR_STATE_MACHINE_ARN \
  --input '{"topic": "serverless AI agents on AWS"}' \
  --region us-east-1
```

## Demonstrate the durability (failure simulation)

This is the most important thing to try. It shows that Step Functions retries Stage 2 without re-running Stage 1.

**Step 1: Enable failure simulation on ExecuteAgent**

```bash
aws lambda update-function-configuration \
  --function-name durable-workflow-ExecuteAgent \
  --environment "Variables={SIMULATE_FAILURE=true}" \
  --region us-east-1
```

**Step 2: Trigger an execution**

```bash
aws stepfunctions start-execution \
  --state-machine-arn YOUR_STATE_MACHINE_ARN \
  --input '{"topic": "serverless AI agents on AWS"}' \
  --region us-east-1
```

**Step 3: Watch in the Step Functions console**

Open the execution in the AWS console. You'll see:
- Stage 1 (Plan): ✅ Succeeded
- Stage 2 (Execute): ❌ Failed → ⏳ Retrying → ❌ Failed again (SIMULATE_FAILURE still true)
- Stage 2 goes to ExecuteFailed state

**Step 4: Disable failure simulation and re-run**

```bash
aws lambda update-function-configuration \
  --function-name durable-workflow-ExecuteAgent \
  --environment "Variables={SIMULATE_FAILURE=false}" \
  --region us-east-1

aws stepfunctions start-execution \
  --state-machine-arn YOUR_STATE_MACHINE_ARN \
  --input '{"topic": "serverless AI agents on AWS"}' \
  --region us-east-1
```

This time all three stages succeed. Notice that Stage 1 ran in both executions — the failure simulation doesn't preserve state across executions, only within a single execution's retry cycle. In a real failure scenario (Lambda timeout, transient error), the retry would reuse Stage 1's output from the same execution.

## Check execution results

```bash
# List executions
aws stepfunctions list-executions \
  --state-machine-arn YOUR_STATE_MACHINE_ARN \
  --region us-east-1

# Get the final output
aws stepfunctions describe-execution \
  --execution-arn YOUR_EXECUTION_ARN \
  --query 'output' \
  --output text \
  --region us-east-1 | python3 -m json.tool
```

## Clean up

```bash
aws stepfunctions delete-state-machine \
  --state-machine-arn YOUR_STATE_MACHINE_ARN \
  --region us-east-1

for fn in PlanAgent ExecuteAgent SummarizeAgent; do
  aws lambda delete-function --function-name "durable-workflow-$fn" --region us-east-1
done

aws iam delete-role-policy --role-name durable-workflow-sfn-role --policy-name InvokeLambda
aws iam delete-role --role-name durable-workflow-sfn-role
```

---

## Architecture decision: Why not AWS SAM?

The original design called for a `template.yaml` (AWS SAM). After analysis, SAM was replaced with `deploy.sh` using raw AWS CLI. The reasoning:

**SAM adds prerequisite burden.** SAM requires `pip install aws-sam-cli` — a separate tool dependency. This sample only needs the AWS CLI and .NET SDK, the same tools required by the AotLambda sample.

**SAM abstracts what's being deployed.** The purpose of this sample is to show the Step Functions + Lambda orchestration pattern. SAM's `template.yaml` adds a layer of indirection between the developer and the actual AWS resources. The `deploy.sh` script makes every resource creation explicit.

**ARN substitution is trivial without SAM.** SAM's main concrete value here — substituting Lambda ARNs into the ASL definition — is three `sed` commands in `deploy.sh`.

**When SAM would be the right choice:** Building a production application deployed repeatedly across environments, needing API Gateway triggers or EventBridge schedules, or wanting CloudFormation stack lifecycle management. None of these apply to a sample demonstrating a pattern.

---

## Comparison with Microsoft Agent Framework (MAF) DurableTask

The most common question: "Why not use MAF's built-in `Microsoft.Agents.AI.DurableTask`?"

### MAF DurableTask approach

MAF ships workflow durability inside the framework. The developer writes workflow logic in C# code:

```csharp
// MAF — durability is built into the framework
var workflow = new WorkflowBuilder()
    .AddStep("plan", planAgent)
    .AddStep("execute", executeAgent)
    .AddStep("summarize", summarizeAgent)
    .Build();

await workflow.RunAsync(input);
// Framework handles checkpointing, retry, and resume internally
```

### Strands Agents .NET approach (this sample)

```csharp
// Strands Agents .NET — each agent is a plain Lambda function
// Step Functions handles orchestration, checkpointing, and retry
var agent = new Agent(model, systemPrompt, toolProviders: [...]);
var result = await agent.InvokeAsync(input);
return result; // Step Functions passes this to the next Lambda
```

### Trade-off table

| Dimension | MAF DurableTask | Strands Agents .NET + Step Functions |
|---|---|---|
| Durability mechanism | Built into the framework | AWS Step Functions (platform) |
| Setup complexity | Add NuGet package | Deploy state machine + Lambda functions |
| Observability | Framework logs | Step Functions console, CloudWatch, X-Ray |
| Retry configuration | Code (redeploy to change) | ASL JSON (change without redeploy) |
| State storage | Framework-managed | Step Functions execution history |
| Max workflow duration | Hours (configurable) | 1 year (Standard) / 5 min (Express) |
| Cost model | Lambda execution time only | Lambda + Step Functions state transitions |
| Cloud target | Azure-native (Durable Task) | AWS-native |
| Visual debugging | DevUI | Step Functions console execution graph |
| Human-in-the-loop | Callbacks | `waitForTaskToken` integration pattern |
| Agent isolation | Shared process | Separate Lambda deployments |

### When to pick each

**Pick MAF DurableTask if:**
- You deploy to Azure
- You want workflow durability built into the framework with minimal infrastructure setup
- Your team prefers code-first workflow definition over declarative ASL
- You're already using MAF and want consistency

**Pick Strands Agents .NET + Step Functions if:**
- You deploy to AWS
- You want platform-native observability (Step Functions console, CloudWatch, X-Ray)
- You want to change retry/timeout configuration without redeploying agent code
- You want workflows that can run for hours or days (Standard Workflows)
- You prefer separating orchestration concerns from agent logic
- Your durability requirements are met by the platform you're already running on

### The key philosophical difference

MAF's approach: the framework owns durability. Strands Agents .NET's approach: the platform owns durability, the framework stays lightweight. Neither is wrong — they reflect different priorities. MAF optimizes for developer convenience; Strands Agents .NET optimizes for operational transparency and AWS-native integration.

Both support MCP and A2A, so agents from either framework can interoperate.
