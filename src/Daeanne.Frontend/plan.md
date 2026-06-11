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

SWA built-in auth (Easy Auth) — enforced at the SWA edge, not in application code. Auth is
handled entirely by the platform: no SDK, no token handling, no middleware in Blazor or Functions.

Both **Microsoft/Entra** and **GitHub** providers are configured OOTB via `staticwebapp.config.json`.
Functions receive the authenticated user's identity via the `x-ms-client-principal` header, injected
automatically by SWA. No cross-service auth wiring needed.

Configuration in `staticwebapp.config.json`:

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
      },
      "github": {
        "registration": {
          "clientIdSettingName": "GITHUB_CLIENT_ID",
          "clientSecretSettingName": "GITHUB_CLIENT_SECRET"
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

Required app settings (SWA environment variables):
- `AZURE_CLIENT_ID` / `AZURE_CLIENT_SECRET` — Entra app registration
- `GITHUB_CLIENT_ID` / `GITHUB_CLIENT_SECRET` — GitHub OAuth app (created at github.com/settings/developers)

Additionally, enforce an allowed-identities check in the API Functions (Jeffrey's Entra object ID
or GitHub login) to prevent any other authenticated user from accessing the app.

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

## File Storage

For serving files to the frontend (research docs, specs, music):

**Approach: standalone Azure Blob Storage account + server-side SAS URLs**

- One storage account (Standard LRS, same subscription as SB). No firewall, no PE, no VNet.
- Public blob access disabled. All access goes through time-limited SAS URLs generated by the Functions API.
- Frontend never holds storage credentials — Functions has the connection string as an app setting.
- Cost: ~$0.02/GB/month. At our scale (tens of docs, a handful of music files), effectively $0.

**Why not SWA's backing storage account?** It's not user-accessible — SWA uses it for
static asset hosting internally and doesn't expose it for arbitrary blob writes.

**Why not Private Endpoint + VNet?** PE costs ~$7–10/endpoint/month. For SB + Storage
that's ~$15–20/month of infrastructure to protect a single user's personal data.
SAS over HTTPS is the pragmatically correct call for this project. If security posture
changes (multi-user, compliance requirement), PE is the upgrade path — the rest of the
architecture stays the same.

### "Last 5 specs + last 5 research docs" page

- Daeanne writes the file to blob on task completion (blob write added to plan-doc close step)
- Containers: `docs/specs/` and `docs/research/`
- Daeanne trims to last N blobs on each write (list by LastModified, delete oldest beyond N)
- Functions API generates SAS list on request — no pre-signed index needed
- No manual management required

### Music

PWA service worker caching:
- First play: Functions generates SAS URL → frontend fetches from blob
- Service worker intercepts and caches the audio file locally
- Subsequent plays: served from cache
- ETag heartbeat (see below) invalidates cache if file changes
- Works offline after first play

### Freshness heartbeat

5-minute background timer in Blazor (or service worker). For each cached doc/file:
- HEAD request to the SAS URL with `If-None-Match: <cached-etag>`
- `304 Not Modified` = zero data transfer, no action
- `200 OK` with new ETag = evict cache entry, re-fetch

---

## Open Questions

| Question | Status |
|----------|--------|
| SWA managed Functions vs. standalone Functions project? | **Resolved: SWA-managed.** Revisit if Functions need dedicated scaling. |
| Result delivery: polling vs. SSE? | **Resolved: polling.** SSE or SignalR for v2. |
| Read path: SB round-trips vs. Table Storage mirroring? | **Resolved: Table Storage mirroring.** See below. |
| Storage: SWA storage, private endpoint, or standalone blob? | **Resolved: standalone blob, no PE, SAS from Functions.** See File Storage section. |
| Table Storage account location | **Resolved: co-locate with existing SB subscription region.** |
| Blob storage: same account as Table Storage or separate? | **Resolved: same account, separate containers.** |
| Confirm M0 scaffold as next action? | **Resolved: proceeding to M0.** |
| Auth: cross-service wiring or SWA built-in? | **Resolved: SWA Easy Auth (built-in).** GitHub + Entra OOTB, zero app code. |

### Read path — resolved: Table Storage mirroring

SB round-trip reads (~2–5s) work for single-item checks but break for list views:
populating a task list would require N round trips or one slow "fetch all." Table Storage mirroring:

- Dispatcher writes a task state snapshot to Table Storage on every status change
- Functions query Table Storage directly (cloud → cloud, instant, no Bridge hop)
- SB remains write-path only (commands → Dispatcher via Bridge)
- M1 requires: Table Storage account + Dispatcher mirroring code + Functions read endpoints

---

*Last updated: 2026-06-10*
*Status: all questions resolved — proceeding to M0 scaffold*