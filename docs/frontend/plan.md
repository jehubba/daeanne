# Daeanne Frontend — Plan (v1)

> **This is the plan.** It describes the concrete implementation choices for v1 of the
> frontend: what stack, what connectivity approach, what queue names, what auth mechanism.
> These are decisions about *this instance* of the spec, not properties of the app itself.
>
> See `spec.md` for what the app must do and why.

---

## Stack

| Layer | Choice | Rationale |
|-------|--------|-----------|
| Frontend | Blazor WASM | .NET ecosystem, PWA support, no context switch |
| Component library | MudBlazor | Responsive, mobile-capable, well-maintained |
| Hosting | Azure Static Web Apps (SWA) | Free tier, built-in auth, SWA-managed API Functions |
| PWA | Yes (via SWA PWA support) | Spec requirement; SWA handles service worker scaffolding |
| Authentication | SWA built-in auth — Microsoft provider | Jeffrey's Microsoft identity; zero auth code in app |

---

## Connectivity

The Dispatcher runs locally at `127.0.0.1:47777`. The frontend cannot reach it directly.
There is no tunnel. All connectivity goes through the existing Service Bus infrastructure.

### Existing SB infrastructure (already live)

The Bridge (`Daeanne.Bridge`) is the SB-to-Dispatcher relay. It already runs two paths:

| Queue | Direction | Current use |
|-------|-----------|-------------|
| `daeanne-inbox` | SB → Bridge → Dispatcher | Inbound email → task creation |
| `daeanne-outbox` | Bridge polls Dispatcher → SB → Functions → ACS | Outbound email send |

Bridge already has: SB processor (consumer), Dispatcher HTTP client, bidirectional wiring.
**No new SB consumer pattern is needed — we extend the Bridge.**

### New queues for frontend

| Queue | Direction | Purpose |
|-------|-----------|---------|
| `daeanne-frontend-requests` | Cloud → Local | Frontend commands → Dispatcher |
| `daeanne-frontend-results` | Local → Cloud | Dispatcher responses → Frontend |

### Call flows

#### Command (write path — async)
1. Blazor POSTs to `/api/command` (SWA-managed Azure Function)
2. Function writes message to `daeanne-frontend-requests` SB queue; returns `202 Accepted` + correlationId
3. Bridge picks up message → POST `/tasks` to Dispatcher
4. Dispatcher creates task, processes normally
5. Bridge (or Dispatcher webhook) writes result to `daeanne-frontend-results` queue
6. Blazor polls `/api/result/{correlationId}` until result arrives
7. Function reads from `daeanne-frontend-results` (or a cache keyed by correlationId) → returns to Blazor

#### Status read (read path — synchronous relay)
1. Blazor GETs `/api/tasks`
2. Function calls Bridge relay endpoint → Bridge calls `GET /tasks` on Dispatcher → returns JSON
3. Function returns to Blazor

Read paths don't need queues — synchronous relay through Functions → Bridge → Dispatcher
is sufficient and simpler. Only writes (task creation, commands) use the async queue path.

### Bridge changes required

Add a second inbound processor to Bridge for `daeanne-frontend-requests`:

```csharp
// In BridgeWorker.ExecuteAsync — start a second processor for frontend queue
var frontendQueue = _config["Bridge:FrontendRequestsQueue"] ?? "daeanne-frontend-requests";
var frontendTask = RunFrontendInboundAsync(sbClient, frontendQueue, dispatcherUrl, stoppingToken);
await Task.WhenAll(inboundTask, outboundTask, frontendTask);
```

The `RunFrontendInboundAsync` method follows the same pattern as `RunInboundAsync` but:
- Deserializes a `FrontendRequest` message (type + prompt + correlationId)
- POSTs to Dispatcher with appropriate task type
- Writes result back to `daeanne-frontend-results` (needs a corresponding outbound path)

### Message schema (new)

```csharp
// Daeanne.Shared.Models — add these
public record FrontendRequest(
    string Prompt,
    string CorrelationId,
    string? TaskType = "Generic"
);

public record FrontendResult(
    string CorrelationId,
    bool Succeeded,
    string? Response,
    string? Error
);
```

These go in `Daeanne.Shared` so Bridge, Functions, and the API project share the contract.

---

## Project Structure

Add to the existing `src/` mono-repo:

```
src/
  Daeanne.Web/
    Daeanne.Web.Client/       # Blazor WASM project (SWA static site)
      Components/
        Chat/
        Tasks/
        Summary/
      wwwroot/
        manifest.webmanifest  # PWA manifest
    Daeanne.Web.Api/          # SWA-managed Azure Functions (API backend)
      Commands/
        CommandFunction.cs    # POST /api/command
        ResultFunction.cs     # GET /api/result/{id}
        TasksFunction.cs      # GET /api/tasks
      staticwebapp.config.json
```

The SWA project root (`Daeanne.Web/`) also contains `staticwebapp.config.json` for
auth routing rules (require auth on all routes).

---

## Authentication

SWA built-in Microsoft provider. Configuration in `staticwebapp.config.json`:

```json
{
  "auth": {
    "identityProviders": {
      "azureActiveDirectory": {
        "userDetailsClaim": "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress",
        "registration": {
          "openIdIssuer": "https://login.microsoftonline.com/<tenant-id>/v2.0",
          "clientIdSettingName": "AZURE_CLIENT_ID",
          "clientSecretSettingName": "AZURE_CLIENT_SECRET"
        }
      }
    }
  },
  "routes": [
    { "route": "/api/*", "allowedRoles": ["authenticated"] },
    { "route": "/*", "allowedRoles": ["authenticated"] }
  ],
  "responseOverrides": {
    "401": { "redirect": "/.auth/login/aad", "statusCode": 302 }
  }
}
```

Additionally, the SWA should be configured with an allowed identities list (Jeffrey's
object ID / email) as an app setting, enforced in the API Functions, to prevent any
other Microsoft-authenticated user from accessing the app.

---

## Repo Location

**Collocated in `jehubba/daeanne`** under `src/Daeanne.Web/`.

Rationale: `FrontendRequest` / `FrontendResult` are message contracts shared with
`Daeanne.Shared`, `Daeanne.Bridge`, and `Daeanne.Functions`. Keeping them in the
same repo prevents schema drift — a change to the queue message format shows up as a
compile error across all consumers in the same build, not a runtime surprise.

Docs (this spec/plan) live in `docs/frontend/`.

---

## Milestones

| Milestone | Scope | Prerequisite |
|-----------|-------|--------------|
| M0: Scaffold | SWA project + Functions shell + auth wiring | None |
| M1: Read paths | Task list + task detail (status reads only, no write path) | Bridge relay endpoint |
| M2: Write path | Chat input → SB queue → Dispatcher → result display | Bridge frontend processor + new queues |
| M3: PWA + polish | Home screen install, offline state, responsive | M1 + M2 |

M1 can be built and deployed without any Bridge changes (read-only relay is a simple
Functions-to-Bridge HTTP proxy). M2 requires the Bridge changes and new SB queues.

---

## Open Questions

| Question | Status |
|----------|--------|
| SWA managed Functions vs. standalone Functions project? | Recommendation: SWA-managed (simpler deployment). Revisit if Functions need dedicated scaling. |
| Result delivery: polling vs. SSE? | Start with polling (simpler). SSE or SignalR for v2 if latency is a problem. |
| Bridge relay endpoint — new HTTP endpoint on Bridge, or direct Dispatcher passthrough via Functions? | Recommendation: Functions call Dispatcher directly for reads (Functions has no firewall issue; Dispatcher is reachable from Bridge, and Bridge is local). Actually requires Bridge as HTTP proxy — see note below. |

> **Note on read path:** Functions (cloud) cannot call Dispatcher (local) directly.
> Bridge (local) can. For read paths, Options are:
> 1. Bridge exposes a small HTTP passthrough endpoint (new, adds complexity)
> 2. Functions write a read-request to SB, Bridge responds via results queue (adds latency)
> 3. Dispatcher results are mirrored to a cloud store (Cosmos/Table Storage) that Functions can read directly
>
> Option 3 is cleanest for reads at scale — Dispatcher pushes state snapshots to Table Storage
> on task status change; Functions query Table Storage. No new SB queues for reads.
> Worth discussing before M1 scaffold.

---

*Last updated: 2026-06-10*
*Status: draft — pending Jeffrey review*
