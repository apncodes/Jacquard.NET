'use strict';

// ── State ─────────────────────────────────────────────────────────────────────
let running = false;
let currentAgentId = null;
let startTime = null;

// ── Entry points ──────────────────────────────────────────────────────────────
function setTopic(topic) {
  document.getElementById('topic-input').value = topic;
}

function switchTab(tab) {
  document.getElementById('pane-live').classList.toggle('hidden', tab !== 'live');
  document.getElementById('pane-article').classList.toggle('hidden', tab !== 'article');
  document.getElementById('tab-live').classList.toggle('active', tab === 'live');
  document.getElementById('tab-article').classList.toggle('active', tab === 'article');
}

function runSwarm() {
  if (running) return;
  const topic = document.getElementById('topic-input').value.trim();
  if (!topic) return;
  startSwarm(topic);
}

// ── Reset UI ──────────────────────────────────────────────────────────────────
function resetUI() {
  // Clear agent blocks
  document.getElementById('agent-blocks').innerHTML = '';
  document.getElementById('output-placeholder').style.display = '';

  // Clear article
  const articleContent = document.getElementById('article-content');
  articleContent.textContent = '';
  articleContent.style.display = 'none';
  document.getElementById('article-placeholder').style.display = '';

  // Reset pipeline nodes
  ['researcher', 'analyst', 'writer', 'editor'].forEach(id => {
    const node = document.getElementById(`node-${id}`);
    node.classList.remove('state-active', 'state-done');
    document.getElementById(`tokens-${id}`).textContent = '';
  });

  // Clear log
  const log = document.getElementById('log');
  log.innerHTML = '';

  // Hide stats
  document.getElementById('stats-bar').style.display = 'none';

  // Switch to live tab
  switchTab('live');
}

// ── Main swarm runner ─────────────────────────────────────────────────────────
async function startSwarm(topic) {
  running = true;
  startTime = Date.now();
  currentAgentId = null;

  resetUI();
  setStatus('running', 'Running');
  setInputEnabled(false);
  addLog(`Starting swarm for: "${topic}"`, 'accent');

  try {
    const response = await fetch('/swarm', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ topic }),
    });

    if (!response.ok) {
      addLog(`Server error: ${response.status}`, 'error');
      return;
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      const messages = buffer.split('\n\n');
      buffer = messages.pop(); // keep incomplete last chunk

      for (const msg of messages) {
        if (!msg.trim()) continue;
        const eventMatch = msg.match(/^event:\s*(.+)$/m);
        const dataMatch  = msg.match(/^data:\s*(.+)$/m);
        if (!eventMatch || !dataMatch) continue;

        const type    = eventMatch[1].trim();
        const payload = JSON.parse(dataMatch[1].trim());
        handleEvent(type, payload);
      }
    }
  } catch (err) {
    addLog(`Connection error: ${err.message}`, 'error');
  } finally {
    running = false;
    setInputEnabled(true);
  }
}

// ── Event handler ─────────────────────────────────────────────────────────────
function handleEvent(type, payload) {
  switch (type) {

    case 'swarm_started': {
      addLog(`Swarm started — entry: ${payload.entryAgentId}`, 'info');
      break;
    }

    case 'agent_started': {
      currentAgentId = payload.agentId;
      document.getElementById('output-placeholder').style.display = 'none';

      // Activate pipeline node
      setNodeState(payload.agentId, 'active');

      // Create agent block in live pane
      createAgentBlock(payload.agentId, payload.description, payload.iteration);

      const msg = payload.handoffMessage
        ? `[${payload.iteration}] ${payload.agentId} — handoff received`
        : `[${payload.iteration}] ${payload.agentId} — starting`;
      addLog(msg, 'accent');

      // Add handoff banner if applicable
      if (payload.handoffMessage) {
        appendHandoffBanner(payload.agentId, payload.handoffMessage);
      }
      break;
    }

    case 'agent_text_delta': {
      appendTextDelta(payload.agentId, payload.delta);
      break;
    }

    case 'agent_tool_call': {
      addToolRow(payload.agentId, payload.toolName, 'running');
      addLog(`  tool call: ${payload.toolName}`, 'info');
      break;
    }

    case 'agent_tool_result': {
      markToolDone(payload.agentId, payload.toolCallId);
      break;
    }

    case 'agent_completed': {
      finaliseAgentBlock(payload.agentId);
      setNodeState(payload.agentId, 'done');
      document.getElementById(`tokens-${payload.agentId}`).textContent =
        `${payload.total} tok`;
      addLog(`  ${payload.agentId} done — ${payload.total} tokens`, 'success');
      break;
    }

    case 'handoff': {
      addLog(`  handoff: ${payload.fromAgentId} → ${payload.toAgentId}`, 'accent');
      appendHandoffBetweenBlocks(payload.fromAgentId, payload.toAgentId, payload.message);
      break;
    }

    case 'swarm_completed': {
      const elapsed = ((Date.now() - startTime) / 1000).toFixed(1);
      setStatus('done', 'Done');

      // Show stats
      const statsBar = document.getElementById('stats-bar');
      statsBar.style.display = 'flex';
      document.getElementById('stat-status').textContent = payload.status;
      document.getElementById('stat-path').textContent   = payload.agentPath.join(' → ');
      document.getElementById('stat-tokens').textContent = `${payload.totalTokens} (${elapsed}s)`;

      // Populate article tab
      const articleContent = document.getElementById('article-content');
      articleContent.textContent = payload.finalMessage;
      articleContent.style.display = '';
      document.getElementById('article-placeholder').style.display = 'none';

      addLog(`Swarm complete — ${payload.totalTokens} tokens in ${elapsed}s`, 'success');
      addLog(`Status: ${payload.status}`, 'success');

      // Auto-switch to article tab
      setTimeout(() => switchTab('article'), 600);
      break;
    }
  }
}

// ── Pipeline node state ───────────────────────────────────────────────────────
function setNodeState(agentId, state) {
  const node = document.getElementById(`node-${agentId}`);
  if (!node) return;
  node.classList.remove('state-active', 'state-done');
  if (state) node.classList.add(`state-${state}`);
}

// ── Agent blocks ──────────────────────────────────────────────────────────────
function createAgentBlock(agentId, description, iteration) {
  const blocks = document.getElementById('agent-blocks');

  const block = document.createElement('div');
  block.className = 'agent-block is-active';
  block.id = `block-${agentId}`;

  block.innerHTML = `
    <div class="agent-block-header">
      <div class="agent-block-dot"></div>
      <div class="agent-block-name">${agentId}</div>
      ${description ? `<div class="node-desc" style="font-size:11px;color:var(--text-muted);margin-left:4px">${escHtml(description)}</div>` : ''}
      <div class="agent-block-iter">#${iteration}</div>
    </div>
    <div class="agent-block-body" id="body-${agentId}">
      <div class="agent-text" id="text-${agentId}"></div>
    </div>
  `;

  blocks.appendChild(block);
  block.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

function appendTextDelta(agentId, delta) {
  const textEl = document.getElementById(`text-${agentId}`);
  if (!textEl) return;

  // Remove cursor from previous position
  const oldCursor = textEl.querySelector('.cursor');
  if (oldCursor) oldCursor.remove();

  textEl.appendChild(document.createTextNode(delta));

  // Add blinking cursor at end
  const cursor = document.createElement('span');
  cursor.className = 'cursor';
  textEl.appendChild(cursor);

  // Scroll into view
  textEl.closest('.agent-block')?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
}

function finaliseAgentBlock(agentId) {
  const block = document.getElementById(`block-${agentId}`);
  if (!block) return;
  block.classList.remove('is-active');
  block.classList.add('is-done');

  // Remove cursor
  const cursor = block.querySelector('.cursor');
  if (cursor) cursor.remove();
}

// ── Tool rows ─────────────────────────────────────────────────────────────────
// Track tool call order per agent so we can mark them done
const toolCallCounters = {};

function addToolRow(agentId, toolName, status) {
  const body = document.getElementById(`body-${agentId}`);
  if (!body) return;

  const count = (toolCallCounters[agentId] = (toolCallCounters[agentId] || 0) + 1);
  const rowId = `tool-${agentId}-${count}`;

  const row = document.createElement('div');
  row.className = 'tool-row';
  row.id = rowId;
  row.dataset.toolName = toolName;
  row.innerHTML = `
    <span class="tool-name">${escHtml(toolName)}</span>
    <span class="tool-status running" id="ts-${rowId}">running</span>
  `;

  // Insert before the text element
  const textEl = document.getElementById(`text-${agentId}`);
  body.insertBefore(row, textEl);
}

function markToolDone(agentId) {
  // Find the last running tool for this agent
  const body = document.getElementById(`body-${agentId}`);
  if (!body) return;
  const running = body.querySelectorAll('.tool-status.running');
  if (running.length > 0) {
    const last = running[running.length - 1];
    last.textContent = 'done';
    last.classList.remove('running');
    last.classList.add('done');
  }
}

// ── Handoff banners ───────────────────────────────────────────────────────────
function appendHandoffBanner(toAgentId, message) {
  // Shown inside the receiving agent's block header area — skip, handled by agent_started
}

function appendHandoffBetweenBlocks(fromId, toId, message) {
  const blocks = document.getElementById('agent-blocks');
  const banner = document.createElement('div');
  banner.className = 'handoff-banner';

  const preview = message.split('\n')[0].trim().slice(0, 120);
  banner.innerHTML = `
    <span class="handoff-arrow">→</span>
    <span class="handoff-agents">${escHtml(fromId)} → ${escHtml(toId)}</span>
    <span class="handoff-msg">${escHtml(preview)}</span>
  `;
  blocks.appendChild(banner);
}

// ── Activity log ──────────────────────────────────────────────────────────────
function addLog(text, variant) {
  const log = document.getElementById('log');

  // Remove placeholder
  const placeholder = log.querySelector('.log-placeholder');
  if (placeholder) placeholder.remove();

  const elapsed = startTime ? `+${((Date.now() - startTime) / 1000).toFixed(1)}s` : '0.0s';

  const entry = document.createElement('div');
  entry.className = 'log-entry';
  entry.innerHTML = `
    <span class="log-time">${elapsed}</span>
    <span class="log-text ${variant ? `log-${variant}` : ''}">${escHtml(text)}</span>
  `;
  log.appendChild(entry);
  log.scrollTop = log.scrollHeight;
}

// ── Status badge ──────────────────────────────────────────────────────────────
function setStatus(state, label) {
  const badge = document.getElementById('status-badge');
  badge.className = `status-badge status-${state}`;
  badge.textContent = label;
}

// ── Input enable/disable ──────────────────────────────────────────────────────
function setInputEnabled(enabled) {
  document.getElementById('topic-input').disabled = !enabled;
  document.getElementById('run-btn').disabled = !enabled;
  document.querySelectorAll('.chip').forEach(c => c.disabled = !enabled);
}

// ── Utilities ─────────────────────────────────────────────────────────────────
function escHtml(str) {
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

// Allow Enter key in topic input
document.getElementById('topic-input').addEventListener('keydown', e => {
  if (e.key === 'Enter' && !running) runSwarm();
});
