# Research: Daeanne Frontend Web App

**Date**: 2026-06-11

## R1: Blazor WASM PWA on Azure Static Web Apps

**Decision**: Use Blazor WASM hosted on Azure Static Web Apps with SWA-managed Azure Functions as the API backend.

**Rationale**:

- The existing codebase is entirely .NET (C#). Blazor WASM avoids a JavaScript context switch.
- SWA provides free-tier hosting with built-in auth (Easy Auth), custom domain support, and a managed Functions API layer.
- Blazor WASM supports PWA out of the box (`manifest.webmanifest` + service worker).
- The existing `Daeanne.Frontend` scaffold already has the correct SWA 4-project structure (Client, Server, Shared, api).

**Alternatives considered**:

- React/Next.js — would require JavaScript tooling and a second language in the repo
- Blazor Server — requires persistent WebSocket; not suitable for SWA hosting
- MAUI Hybrid — overkill for a single-user web+mobile app; deployment complexity

## R2: MudBlazor for Mobile-First UI

**Decision**: Use MudBlazor as the component library.

**Rationale**:

- Material Design components optimized for responsive layouts (built-in breakpoint system, drawer/app bar/bottom nav).
- `MudTabs` provides the bottom tab bar pattern. `MudOverlay` / `MudDrawer` support the task detail drill-down.
- Pull-to-refresh can be implemented via `MudPullToRefresh` or a lightweight JS interop.
- Active maintenance, large community, good documentation.

**Alternatives considered**:

- Radzen Blazor — capable but heavier; Material Design is a better fit for mobile-first
- Custom CSS — too much effort for responsive layout primitives that MudBlazor provides OOTB

## R3: Connectivity — Bridge Relay for Read Paths, Service Bus for Write Paths

**Decision**: Read paths (task list, task detail, trends) use synchronous HTTP relay through Functions → Bridge → Dispatcher. Write paths (send command) use async Service Bus queues.

**Rationale**:

- The Bridge already has an HTTP server at `127.0.0.1:47778` and an established pattern for calling the Dispatcher HTTP API.
- Adding relay endpoints to the Bridge (`GET /relay/tasks`, `GET /relay/tasks/{id}`) is minimal code — it's a pass-through proxy.
- Read paths don't need queue durability — a synchronous relay is simpler and lower-latency.
- Write paths benefit from queue durability: if the Bridge is temporarily offline, the command is buffered in Service Bus.
- The Bridge already processes `daeanne-inbox` via `ServiceBusProcessor` — adding `daeanne-frontend-requests` follows the identical pattern.

**Alternatives considered**:

- All paths through Service Bus — adds unnecessary latency to reads (SB round-trip) and complexity (polling for read results)
- Direct tunnel (ngrok/Cloudflare Tunnel) — violates spec constraint FR-013 (no local port exposure)

## R4: Authentication — SWA Easy Auth with Identity Guard

**Decision**: Use SWA built-in auth (Microsoft/Entra provider) with an API-level identity guard that restricts access to Jeffrey's specific identity.

**Rationale**:

- The existing `staticwebapp.config.json` already configures AAD auth with `AAD_CLIENT_ID` / `AAD_CLIENT_SECRET`.
- SWA injects `x-ms-client-principal` header into all Function invocations — no SDK or token handling needed.
- An `IdentityGuard` middleware in the API Functions decodes the client principal and rejects any identity that doesn't match Jeffrey's known Entra object ID or GitHub login.
- This satisfies FR-011 (auth required) and FR-012 (Jeffrey only) with zero application-level auth code in the Blazor client.

**Alternatives considered**:

- MSAL in Blazor — unnecessary complexity when SWA handles auth at the edge
- Shared secret / API key — doesn't satisfy "durable identity" requirement from spec

## R5: Shared Message Contracts in Daeanne.Shared

**Decision**: Place `FrontendRequest` and `FrontendResult` records in `Daeanne.Shared.Models`.

**Rationale**:

- Bridge, Functions (api project), and the Shared project all need these types.
- Placing them in `Daeanne.Shared` ensures compile-time contract enforcement across all consumers.
- The existing codebase already uses `Daeanne.Shared` for `CreateTaskRequest`, `AgentTask`, etc.
- The Frontend api project will need a project reference to `Daeanne.Shared` (currently it only references `DaeanneFrontend.Shared`).

**Alternatives considered**:

- Duplicate the types in each project — schema drift risk
- NuGet package — overkill for a monorepo

## R6: Polling Strategy for Task List

**Decision**: 30-second timed polling via `Timer` in the Blazor client, plus manual pull-to-refresh gesture.

**Rationale**:

- Spec clarification explicitly chose this approach over real-time push.
- 30 seconds is frequent enough for "ambient awareness" without excessive API calls.
- Pull-to-refresh gives Jeffrey immediate control when he wants fresh data.
- No WebSocket/SSE infrastructure needed — keeps the SWA Functions model simple.

**Alternatives considered**:

- SignalR — requires persistent connection infrastructure not available in SWA managed Functions
- Shorter polling (5s) — unnecessary API load for a single user; 30s was explicitly chosen

## R7: Bridge Relay Endpoint Design

**Decision**: Add HTTP relay endpoints to the Bridge at `GET /relay/tasks` and `GET /relay/tasks/{id}` that proxy to the Dispatcher's existing `GET /tasks` and `GET /tasks/{id}` endpoints.

**Rationale**:

- The Bridge already runs an HTTP server at port 47778 with a health endpoint.
- The Dispatcher's task endpoints already support pagination (`skip`, `take`), status filtering, and type filtering.
- The relay endpoints are thin pass-through proxies — they forward the request and return the response.
- The SWA Functions call the Bridge relay; the Bridge calls the Dispatcher. This preserves the existing security boundary (only local services talk to the Dispatcher).

**Alternatives considered**:

- Functions calling Dispatcher directly — would require the Dispatcher to be internet-accessible (violates FR-013)
- Dedicated relay service — unnecessary when Bridge already runs an HTTP server

## R8: Command Result Delivery

**Decision**: Use a correlation-based polling pattern. The API Function sends the command via SB queue with a `correlationId`, then the Blazor client polls `GET /api/result/{correlationId}` until the result is available.

**Rationale**:

- The Bridge processes the command and writes the result to the `daeanne-frontend-results` SB queue.
- The API Function polls or receives from this queue and caches results keyed by correlationId.
- The Blazor client polls the API endpoint — this is consistent with the overall polling-based architecture.
- CorrelationId is already a first-class concept in `AgentTask` and `CreateTaskRequest`.

**Alternatives considered**:

- WebSocket push from Functions — not supported in SWA managed Functions
- Long polling — adds complexity without meaningful UX improvement for a single user
