---
sidebar_position: 9
---

# Durability

Agents fail. Models time out, tools throw, Lambda functions hit memory limits. Durability is how you handle those failures — and there are two distinct levels.

## Agent durability (within a single invocation)

Inside one call to `InvokeAsync` or `StreamAsync`, the agent event loop already provides a degree of resilience:

- **Retry on transient model errors** — the event loop can retry failed model calls before surfacing the exception
- **Tool error handling** — if a tool throws, the error is reported back to the model as a tool result, giving the model a chance to recover or try a different approach
- **Graceful degradation** — the `StrandsException` always carries a `ConversationSnapshot` so you can inspect what happened and resume

Agent durability is built into the framework. You get it for free with every agent.

## Workflow durability (between invocations)

When a pipeline spans multiple agent invocations — each potentially running in a separate Lambda function or container — you need something external to manage state between stages. This is workflow durability.

The problem: if Stage 3 of a 5-stage pipeline fails, you don't want to re-run Stages 1–2. Their outputs (and the LLM costs that produced them) should be preserved.

The solution: an external orchestrator that checkpoints each stage's output and retries only the failed stage.

### When you need workflow durability

- Pipelines that take more than a few seconds end-to-end
- Workflows where individual stages are expensive (multiple LLM calls, external API calls)
- Production systems where partial failure shouldn't mean total restart
- Long-running research or analysis tasks broken into discrete steps

### How it works with Step Functions

The [DurableWorkflow sample](https://github.com/apncodes/Jacquard.NET/tree/main/samples/DurableWorkflow) demonstrates this pattern using AWS Step Functions:

```
[Stage 1: Plan] ──checkpoint──→ [Stage 2: Execute] ──checkpoint──→ [Stage 3: Summarize]
```

Each stage is a separate Lambda function running a stateless agent. Step Functions:

1. Passes the output of each stage as input to the next
2. Stores intermediate results in the execution state
3. Retries failed stages without re-running successful ones
4. Provides visibility into which stage failed and why

The key insight: **the state machine is the state store**. No shared database, no message queue, no session manager between stages.

### Right-sizing models per stage

Because each stage is an independent deployment unit, you can choose the right model for each cognitive task:

| Stage | Model choice | Rationale |
|---|---|---|
| Planning | Claude Sonnet | Structured reasoning, reliable decomposition |
| Execution | Claude Sonnet | Superior tool use, instruction following |
| Summarization | Amazon Nova Pro | Fast synthesis, cost-efficient for writing tasks |

In a single-invocation pipeline, you're locked into one model. Decomposed pipelines let you optimize cost and quality per stage.

## Choosing between the two

| Concern | Agent durability | Workflow durability |
|---|---|---|
| Scope | Within one `InvokeAsync` call | Across multiple invocations |
| State management | In-memory (conversation history) | External orchestrator |
| Retry granularity | Individual model/tool calls | Entire pipeline stages |
| Cost of failure | Re-run one tool call | Re-run one stage (not the whole pipeline) |
| Infrastructure | None — built into the SDK | Step Functions, Durable Functions, or similar |
| When to use | Always (it's automatic) | Multi-stage pipelines, expensive workflows |

## In-process vs. decomposed pipelines

Jacquard.NET's `PipelineOrchestrator` runs all stages in one process. This is simpler but offers no durability between stages — if the process crashes at Stage 3, you restart from Stage 1.

The decomposed pattern (separate Lambda per stage + Step Functions) adds deployment complexity but gives you:

- Stage-level retries without re-running prior work
- Independent scaling and timeout configuration per stage
- Model selection per stage
- Visibility into pipeline progress via Step Functions console

For short pipelines (under 30 seconds, low cost per stage), in-process is fine. For long or expensive pipelines, decompose.

## Further reading

- [DurableWorkflow sample](https://github.com/apncodes/Jacquard.NET/tree/main/samples/DurableWorkflow) — full working implementation with deploy scripts
- [Agent & Event Loop](./agent-event-loop) — how the inner loop handles tool failures
- [How-To: Durable Workflows](../how-to/durable-workflows) — step-by-step deployment guide
