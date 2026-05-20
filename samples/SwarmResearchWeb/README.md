# SwarmResearchWeb — Equity Research Swarm (Web UI)

ASP.NET Core web application demonstrating the **Swarm** multi-agent pattern with a real-time browser UI. The same `SwarmOrchestrator` used in the console sample streams events over **Server-Sent Events (SSE)** to a dark-theme single-page frontend.

## Architecture

```
Browser ──POST /swarm──▶ ASP.NET Core ──SwarmOrchestrator.StreamAsync──▶ SSE stream
   ◀──── SSE events ─────────────────────────────────────────────────────────────────
```

The backend maps each `SwarmEvent` to a typed SSE message. The frontend pattern-matches on `event:` type and updates the UI in real time — no polling, no WebSockets.

## SDK features shown

- `SwarmOrchestrator.StreamAsync` — `IAsyncEnumerable<SwarmEvent>` consumed server-side, forwarded as SSE
- Full `SwarmEvent` hierarchy surfaced to the browser
- `[Tool]` source generator — compile-time `ITool` wrappers

## Prerequisites

AWS credentials configured (env vars, `~/.aws/credentials`, or IAM role) with Amazon Bedrock access.

## Usage

```bash
dotnet run --project samples/SwarmResearchWeb
# → http://localhost:5170
```

Open `http://localhost:5170` in Chrome, Firefox, or Safari.

## UI layout

**Left panel** — control and observation:
- Topic input + Run button + quick-select chips (Quantum Computing, Renewable Energy, Artificial Intelligence)
- Agent pipeline — 4 nodes with live state indicators (idle → pulsing amber → green done), per-agent token counts
- Activity log — timestamped relative to run start, colour-coded by event type
- Stats bar — status, agent execution path, total tokens + elapsed time

**Right panel** — content:
- Live tab: agent blocks appear as each agent starts; text streams token-by-token with a blinking cursor; tool calls show inline with running/done badges; handoff banners connect blocks
- Article tab: auto-switches when the swarm completes; shows the final polished article

## SSE event types

| SSE event | SwarmEvent |
|---|---|
| `swarm_started` | `SwarmStartedEvent` |
| `agent_started` | `AgentStartedEvent` |
| `agent_text_delta` | `AgentTextDeltaEvent` |
| `agent_tool_call` | `AgentToolCallEvent` |
| `agent_tool_result` | `AgentToolResultEvent` |
| `agent_completed` | `AgentCompletedEvent` |
| `handoff` | `HandoffEvent` |
| `swarm_completed` | `SwarmCompletedEvent` |

## Integrating the SSE stream in your own frontend

```javascript
const response = await fetch('/swarm', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ topic: 'quantum computing' }),
});

const reader = response.body.getReader();
// parse SSE frames, then:
switch (type) {
    case 'agent_started':   /* show agent card */ break;
    case 'agent_text_delta': /* stream text */    break;
    case 'handoff':         /* show arrow */      break;
    case 'swarm_completed': /* show article */    break;
}
```

## Design

Dark slate background (`#0f1117`), warm white text, single amber accent (`#d97706`) for active state, green for completion. Monospace font for tool calls and token counts. No gradients, no emoji in UI chrome — professional research tool aesthetic.
