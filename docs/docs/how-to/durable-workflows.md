---
sidebar_position: 3
---

# Durable Workflows with Step Functions

For multi-step agentic pipelines where individual steps are long-running or expensive, use the **Decomposed Sequential Pipeline** pattern with AWS Step Functions.

Each agent runs as a separate Lambda function. Step Functions manages checkpointing, retry, and state passing between steps. If a step fails, Step Functions retries that step only — prior steps are not re-run.

See the **[DurableWorkflow sample](https://github.com/apncodes/Jacquard.net/tree/main/samples/DurableWorkflow)** for a complete, deployed example with:

- Three NativeAOT Lambda functions (Plan → Execute → Summarize)
- Three different Bedrock models (Claude Sonnet 4.6 × 2, Amazon Nova Pro)
- Real execution data: 183s end-to-end, 10s/164s/4s per stage
- Failure simulation to demonstrate retry behavior
- Raw AWS CLI deploy script (no SAM required)
