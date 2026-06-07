# Daeanne — Development Roadmap

**Last updated:** 2026-06-07

Ordered by dependency and value. Each phase should be usable before the next
begins.

---

## Phase 1: Foundation (Infrastructure Shell) ✅ COMPLETE

**Goal:** Kestrel Dispatcher and Bridge running locally with stub endpoints.
No real agent dispatch yet — just the plumbing.

- [x] `Daeanne.sln` with project structure
- [x] `Daeanne.Shared`: task models, API contracts, result types
- [x] `Daeanne.Dispatcher`: Kestrel Minimal API, SQLite task DB, all endpoints
      with full state-transition guards and enum string serialization
- [x] `Daeanne.Bridge`: Windows Service shell, Service Bus SDK wired up,
      inbound/outbound queues connected to Dispatcher HTTP endpoints
- [x] Manual end-to-end smoke test passed: POST task → GET task → POST result →
      409 guard → list → outbox email; all endpoints verified on port 47777

**Notes:**
- Dispatcher bound to `http://127.0.0.1:47777` (localhost-only)
- SQLite DB: `dispatcher.db` relative to working dir (set in appsettings.json)
- Bridge disabled mode: logs warning and idles when `ConnectionStrings:ServiceBus` is empty

---

## Phase 2: Agent Dispatch ✅ COMPLETE

**Goal:** Dispatcher can cold-start a CLI agent with a prompt and capture
its result.

- [x] `IAgentDispatcher` / `CopilotCliDispatcher`: invokes `copilot --agent <name> -p "<prompt>" --silent --no-ask-user --allow-all-tools` using `ArgumentList` (safe prompt injection)
- [x] `DispatchConfig`: maps AgentTaskType → agent name, configures timeout/concurrency/workDir
- [x] `DispatchWorker`: BackgroundService with `Channel<Guid>` wake-up, DB-backed state, startup rehydration, `SemaphoreSlim` concurrency cap
- [x] Research Agent orchestrated mode: prompt includes `task_id`/`output_path`; agent writes `<task_id>-research.md` + `session.md` to per-task workdir
- [x] `AgentTaskStatus.TimedOut` added to terminal statuses
- [x] End-to-end test passed: POST research task → Pending → Running → Succeeded in ~70s; report and session.md written to `~/.daeanne/tasks/<id>/`

**Notes:**
- Per-task work dirs: `~/.daeanne/tasks/<task_id>/` (configurable via `Dispatch:WorkDir`)
- CLI invocation: `copilot --agent <name> -p <prompt> --silent --no-ask-user --allow-all-tools --allow-all-paths --allow-all-urls --share <session.md> -C <workdir>`
- Task timeout: 10 minutes (configurable `Dispatch:TaskTimeoutMinutes`)

---

## Phase 3: Daeanne (CoS Agent) ✅ COMPLETE

**Goal:** Daeanne can receive a task, make an orchestration decision, and
dispatch via the Dispatcher.

- [x] `agents/daeanne.agent.md` — full agent profile: identity (Daeanne =
      daemon + Diane from Twin Peaks), startup routine, orchestration pipeline
      (classify → decompose → dispatch → poll → read results → synthesize),
      working memory format, escalation rules with structured format, tool
      use policy, outbound email protocol
- [x] Tools: `read`, `web`, `shell` (for Dispatcher HTTP calls via PowerShell)
- [x] Dispatcher contract: exact PowerShell snippets for POST /tasks, GET /tasks/{id},
      reading ResultJson and report files, POST /outbox/email
- [x] Symlink: `~/.copilot/agents/daeanne.agent.md` → repo file (live in CLI + VS Code)
- [x] Available alongside research-agent in `~/.copilot/agents/`

**Notes:**
- Run `copilot --agent daeanne` to start a Daeanne session
- Dispatcher must be running at `http://127.0.0.1:47777`
- Daeanne uses PowerShell `Invoke-RestMethod` (via shell tool) for all Dispatcher calls
- Per-task work dirs: `~/.daeanne/tasks/<task_id>/`

---

## Phase 4: Email Pipeline ✅ COMPLETE (pending ACS provisioning)

**Goal:** Emails can reach Daeanne and Daeanne can send emails.

- [x] Azure Service Bus namespace: `sb-daeanne-comm`, queues: `daeanne-inbox` / `daeanne-outbox`
- [x] `Daeanne.Shared`: `BridgeEmailMessage` contract, `OutboxEmailStatus.Processing`, `UpdateOutboxStatusRequest`
- [x] `Daeanne.Dispatcher`: `POST /outbox/email`, `GET /outbox/email/{id}`, `PATCH /outbox/email/{id}/status`
- [x] `DispatchWorker`: Email/Scheduling tasks left Pending (no agent in map); Bridge claims them
- [x] `Daeanne.Bridge`: full bidirectional worker — inbound ServiceBusProcessor → POST /tasks; outbound 10s poll → PATCH Processing → SB publish → PATCH Sent/Failed
- [x] `Daeanne.Functions`: `EmailIngest` (ACS EventGrid webhook → daeanne-inbox) + `EmailSend` (daeanne-outbox → ACS Email SDK)
- [x] Full solution builds clean (0 errors, 0 warnings)
- [ ] **Remaining:** ACS provisioning, domain setup, Function App deploy, Bridge connection string config, end-to-end smoke test

**Notes:**
- Bridge runs as Windows Service (`UseWindowsService()`)
- Stale `Processing` emails (>2 min) auto-retried on next Bridge poll cycle
- `local.settings.json` has placeholder keys: `ServiceBusConnection`, `AcsEmailConnectionString`, `AcsEmailSenderAddress`
- ACS event type parsed: `Microsoft.Communication.EmailReceived`

---

## Phase 5: Scheduler Sub-Agent

**Goal:** Daeanne can schedule and query calendar events without knowing
about Microsoft Graph.

- [ ] `agents/scheduler.agent.md`
- [ ] Microsoft Graph API credentials and consent
- [ ] Scheduler handles: create event, check availability, list upcoming
- [ ] Daeanne dispatches scheduling requests without knowing Graph details
- [ ] End-to-end test: ask Daeanne to schedule a meeting, verify it appears
      in calendar

---

## Future / Backlog

- Daeanne context compaction strategy (long-running session management)
- Outbox retry logic (failed email sends)
- Task priority and scheduling (defer, repeat, deadline)
- Additional sub-agents (code review, documentation, etc.)
- Monitoring dashboard (web UI for Dispatcher task queue)
- Multi-machine support (if ever needed)
