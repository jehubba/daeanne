# ADR-002: Kestrel as Local Reverse Proxy / Agent Dispatcher

**Date:** 2026-06-07
**Status:** Accepted

## Context

We needed a local ingress point that could receive requests from both external
sources (the Service Bus bridge) and internal agent-to-agent traffic, and
dispatch work to sub-agent processes.

## Decision

**Use ASP.NET Core Minimal API with Kestrel as a persistent local HTTP server,
running as a Windows Service on a custom `.local` port.**

## Rationale

- Single ingress point for all traffic makes the system auditable and
  observable — every task passes through one place
- Separating Kestrel from the Bridge means swapping the Azure communication
  provider does not require changes to the local dispatcher
- ASP.NET Core / Kestrel is already the confirmed .NET stack
- Minimal API keeps the surface area small; no MVC overhead needed
- SQLite (via EF Core or Dapper) for task state gives restart resilience
  without external dependencies

## Key Endpoints

- `POST /tasks` — submit a task
- `GET  /tasks/{id}` — check status
- `POST /tasks/{id}/result` — agent posts result
- `POST /outbox/email` — request outbound email
- `GET  /tasks` — monitoring

## Consequences

- Daeanne and all sub-agents communicate exclusively through Dispatcher HTTP
  endpoints — never through direct file I/O or process calls
- The Dispatcher owns agent process spawning (`System.Diagnostics.Process`)
- Task state persists in SQLite and survives Dispatcher restarts
