# ADR-004: Process Model — Persistent CoS, Stateless Sub-Agents

**Date:** 2026-06-07
**Status:** Accepted

## Context

We needed to decide which agent processes should be persistent (warm) and
which should be cold-started per task.

## Decision

**Daeanne (CoS) runs as a persistent warm process. All sub-agents
(research, scheduler, etc.) are cold-started per task and are fully
stateless — all context is passed in the prompt at dispatch time.**

## Rationale

- Daeanne benefits from persistent context: she tracks active tasks,
  remembers recent decisions, and maintains a mental model of what's
  in flight. Cold-starting her per request would lose this continuity.
- Sub-agents have no need for cross-task memory. Each task is
  self-contained. Passing context in the prompt is simpler, more
  auditable, and avoids state corruption from long-running sessions.
- Cold-started sub-agents are easier to scale (no warm pool to manage)
  and easier to debug (each invocation is independent)

## Keep-Alive Strategy (deferred)

The mechanism for keeping Daeanne warm between tasks is TBD. Options:
- Copilot CLI `--keep-alive` flag (if available)
- Scheduled no-op prompts to prevent session timeout
- Persistent background process with session resume on reconnect

This decision is deferred until the Dispatcher is built and we can
observe actual session behavior.

## Consequences

- Sub-agent prompts must include all necessary context (task ID, output
  path, relevant background). The Dispatcher is responsible for
  assembling this context at dispatch time.
- Daeanne's session state is not persisted to disk in v1. If her
  process restarts, she recovers from the Dispatcher's SQLite task DB.
- Long-running Daeanne sessions may hit Copilot context window limits.
  Compaction strategy TBD.
