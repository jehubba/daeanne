# Daeanne — Agent OS Architecture Overview

**Last updated:** 2026-06-07
**Status:** Active design — pre-implementation

---

## What Is This?

Daeanne is a personal AI agent operating system built around a persistent
Chief of Staff agent (also named Daeanne) that orchestrates specialized
sub-agents for research, scheduling, communication, and other tasks.

The system is a hybrid of local infrastructure and Azure cloud services,
designed for a single user on a Windows machine with M365 integration.

---

## System Diagram

```
INBOUND EMAIL
─────────────────────────────────────────────────────────────────
  Email
    → Azure Communication Services (ACS)
    → Azure Function: email-ingest        [cloud]
    → Service Bus: inbound queue          [cloud]
    → Daeanne.Bridge (Windows Service)    [local]
    → Kestrel Dispatcher (.local:port)    [local]
    → Daeanne (CoS Agent)                 [local, persistent]

OUTBOUND EMAIL
─────────────────────────────────────────────────────────────────
  Daeanne (CoS Agent)
    → Kestrel Dispatcher: POST /outbox/email
    → Daeanne.Bridge (Windows Service)    [local]
    → Service Bus: outbound queue         [cloud]
    → Azure Function: email-send          [cloud]
    → Azure Communication Services (ACS)
    → Email

INTERNAL ORCHESTRATION
─────────────────────────────────────────────────────────────────
  Daeanne (CoS Agent)
    → Kestrel Dispatcher: POST /tasks
    → Sub-agent processes (cold-started per task)
      - Research Agent
      - Scheduler Agent
      - (future agents)
    → Sub-agent POSTs result: POST /tasks/{id}/result
    → Kestrel routes result back to Daeanne

CALENDAR / M365
─────────────────────────────────────────────────────────────────
  Daeanne (CoS Agent)
    → POST /tasks  [type: scheduling]
    → Scheduler Sub-Agent (cold-started)
    → Microsoft Graph API
    (Daeanne never calls Graph directly)
```

---

## Components

### Daeanne.Dispatcher (Kestrel — local HTTP server)

- **Role:** Single ingress point for all requests, both external (from Bridge)
  and internal (agent-to-agent)
- **Technology:** ASP.NET Core Minimal API, Kestrel
- **Process model:** Persistent Windows Service
- **Key endpoints:**
  - `POST /tasks` — submit a task for dispatch
  - `GET  /tasks/{id}` — check task status
  - `POST /tasks/{id}/result` — agent posts its completed result
  - `POST /outbox/email` — request an outbound email
  - `GET  /tasks` — list in-flight tasks (monitoring)
- **State:** SQLite database for task queue, history, and status
- **Design principle:** Daeanne never touches files or spawns processes
  directly. Everything goes through the Dispatcher.

### Daeanne.Bridge (Windows Service — bidirectional comms bridge)

- **Role:** Two-way bridge between local Kestrel and Azure Service Bus
- **Technology:** .NET Worker Service, Azure.Messaging.ServiceBus SDK
- **Process model:** Windows Service (starts on login)
- **Inbound:** Polls/receives from Service Bus inbound queue → HTTP POST to
  Kestrel
- **Outbound:** Receives from Kestrel outbound queue → publishes to Service
  Bus outbound queue
- **Design principle:** The Bridge is a pure transport layer. It has no
  business logic. Changing the Azure communication provider only affects
  the Bridge, not Kestrel or Daeanne.

### Daeanne (CoS Agent — persistent)

- **Role:** Chief of Staff. Reasoning, orchestration, escalation, planning.
  Does NOT do transport, file I/O, or process management directly.
- **Technology:** Copilot CLI custom agent (.agent.md profile)
- **Process model:** Persistent (warm) — maintains conversational context
  between tasks. Keep-alive strategy TBD.
- **Responsibilities:**
  - Parse and decompose inbound requests (email, direct prompt, scheduled)
  - Decide what to dispatch, to whom, and when
  - Track task progress via Dispatcher
  - Escalate to human when needed
  - Maintain working memory of active tasks and context
- **Does NOT:** Call Azure services, write files, spawn processes, call
  Microsoft Graph directly

### Research Agent (separate repo: jehubba/research-agent)

- **Role:** Multi-stage research specialist with self-evaluation loop
- **Technology:** Copilot CLI custom agent (.agent.md profile)
- **Process model:** Cold-started per task, fed full context in prompt
- **Orchestration contract:** Receives `task_id` + `output_path` in prompt;
  returns `---RESEARCH_COMPLETE---` block; POSTs result to Dispatcher
- **Repo:** https://github.com/jehubba/research-agent

### Scheduler Sub-Agent (planned)

- **Role:** All calendar and scheduling operations via Microsoft Graph
- **Technology:** Copilot CLI custom agent (.agent.md profile)
- **Process model:** Cold-started per task
- **Design principle:** Daeanne never calls Graph directly. If the calendar
  backend changes (M365 → Google), only this agent changes.

### Azure Functions (cloud)

- **email-ingest:** Receives from ACS → publishes to Service Bus inbound queue
- **email-send:** Receives from Service Bus outbound queue → sends via ACS
- **Technology:** Azure Functions consumption plan (no idle cost)

---

## Process Model

| Component | Process Model | State |
|-----------|--------------|-------|
| Daeanne.Dispatcher | Persistent Windows Service | SQLite task DB |
| Daeanne.Bridge | Persistent Windows Service | Stateless |
| Daeanne (CoS) | Persistent (warm) | Conversational context |
| Research Agent | Cold-started per task | Stateless (context in prompt) |
| Scheduler Agent | Cold-started per task | Stateless (context in prompt) |

---

## Key Design Principles

1. **Daeanne reasons; the Dispatcher executes.** Separation of cognition and
   plumbing. Daeanne is never a file carrier.

2. **Kestrel is the single ingress point.** All requests — internal or
   external — enter through the Dispatcher. This makes the Bridge swappable
   without touching agent logic.

3. **Sub-agents are stateless and cold-started.** Each task dispatch includes
   all necessary context. Agents don't maintain state between invocations.

4. **Azure is only at the edges.** Azure Functions and Service Bus handle the
   cloud/local boundary. Local components never call Azure services directly
   except through the Bridge.

5. **Abstractions protect Daeanne from infrastructure.** She knows about
   "tasks," "scheduling requests," and "email." She does not know about
   Service Bus, Graph API, ACS, or process spawning.

---

## What Doesn't Exist Yet

- [ ] Daeanne.Dispatcher (.NET project)
- [ ] Daeanne.Bridge (.NET project)
- [ ] Daeanne agent profile (daeanne.agent.md)
- [ ] Scheduler sub-agent
- [ ] Azure Function: email-ingest
- [ ] Azure Function: email-send
- [ ] Azure Communication Services setup
- [ ] Service Bus queues (inbound + outbound)
- [ ] SQLite schema for task queue
- [ ] Keep-alive strategy for persistent Daeanne process

---

## Related Repos

- [daeanne](https://github.com/jehubba/daeanne) — this repo (hub)
- [research-agent](https://github.com/jehubba/research-agent) — research
  sub-agent with self-eval loop
