# Implementation Plan: Daeanne Frontend Web App

**Branch**: `feat/001-daeanne-frontend` | **Date**: 2026-06-11 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-daeanne-frontend/spec.md`

**Reference**: Detailed implementation decisions from `docs/frontend/plan.md`

## Summary

Build a mobile-first Blazor WASM Progressive Web App hosted on Azure Static Web Apps that gives Jeffrey ambient awareness of Daeanne's task state and a low-friction chat interface for sending commands. The frontend connects to the local Dispatcher via the existing Bridge/Service Bus relay — read paths use synchronous relay, write paths use async queues. Authentication is handled entirely by SWA Easy Auth (Microsoft/Entra provider). The existing `src/Daeanne.Frontend` scaffold will be reworked to match the architecture described in `docs/frontend/plan.md`.

## Technical Context

**Language/Version**: C# / .NET 8, Blazor WebAssembly

**Primary Dependencies**: MudBlazor (UI components), Azure.Messaging.ServiceBus (Bridge), Microsoft.Azure.Functions.Worker (API)

**Storage**: None new — reads from existing Dispatcher SQLite via HTTP relay

**Testing**: bUnit (Blazor component tests), xUnit (API function tests), Playwright (E2E)

**Target Platform**: iOS Safari 15+, Chrome for Android (PWA), modern desktop browsers (secondary)

**Project Type**: Web application (Blazor WASM client + Azure Functions API backend)

**Performance Goals**: Task list load < 2s on LTE, command ack < 3s, 390px minimum viewport

**Constraints**: No local port exposure, single user (Jeffrey), no new data stores, < 1MB initial bundle target for mobile

**Scale/Scope**: 1 user, ~50-200 tasks/day, 2 primary views (Tasks tab, Chat tab) + 1 drill-down (task detail overlay)

## Constitution Check

_GATE: Must pass before Phase 0 research. Re-check after Phase 1 design._

Constitution is not yet ratified (template placeholder). No gates to enforce. Proceeding.

## Project Structure

### Documentation (this feature)

```text
specs/001-daeanne-frontend/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (API contracts)
└── tasks.md             # Phase 2 output (not created by /speckit.plan)
```

### Source Code (repository root)

The existing `src/Daeanne.Frontend/` scaffold will be reworked in-place. The project already has the correct SWA structure (Client + Server + api + Shared) but contains only template boilerplate.

```text
src/
  Daeanne.Frontend/
    DaeanneFrontend.sln                # Existing — already references Client, Server, Shared, api
    staticwebapp.config.json           # Existing — auth already configured (AAD + GitHub)
    Client/                            # Blazor WASM (SWA static site)
      DaeanneFrontend.Client.csproj    # Existing — add MudBlazor dependency
      Program.cs                       # Existing — configure MudBlazor services
      App.razor                        # Existing — rework with MudThemeProvider
      _Imports.razor                   # Existing — add MudBlazor namespaces
      wwwroot/
        manifest.webmanifest           # NEW — PWA manifest
        service-worker.js              # NEW — PWA service worker
        icons/                         # NEW — app icons (192px, 512px)
      Pages/
        TasksPage.razor                # NEW — Tasks tab (replace Index.razor)
        ChatPage.razor                 # NEW — Chat tab (replace Counter.razor)
      Components/

        Tasks/
          TaskList.razor               # NEW — grouped task list with pull-to-refresh
          TaskCard.razor               # NEW — individual task entry card
          TaskDetailOverlay.razor      # NEW — drill-down for full task output
        Chat/
          ChatView.razor               # NEW — conversational message list + input
          ChatBubble.razor             # NEW — individual message bubble
        Dashboard/
          TrendHighlights.razor        # NEW — today's trend highlights section
        Shared/
          OfflineIndicator.razor       # NEW — connectivity status banner
      Services/
        DaeanneApiClient.cs            # NEW — typed HTTP client for /api/* endpoints
        ConnectivityService.cs         # NEW — tracks online/offline state
      Models/
        ChatMessage.cs                 # NEW — client-side chat message model with Direction/Status enums
    Server/                            # ASP.NET Core host (for local dev; SWA serves in prod)
      DaeanneFrontend.Server.csproj    # Existing
      Program.cs                       # Existing — minimal changes
    Shared/                            # DTOs shared between Client and api
      DaeanneFrontend.Shared.csproj    # Existing
      TaskDto.cs                       # NEW — task list/detail DTO
      CommandRequest.cs                # NEW — command submission DTO
      CommandResultDto.cs              # NEW — command result DTO
      TrendHighlightDto.cs             # NEW — trend data DTO
    api/                               # SWA-managed Azure Functions
      DaeanneFrontend.Api.csproj       # Existing — add Service Bus dependency
      Program.cs                       # Existing — add Service Bus client DI
      HealthFunction.cs                # Existing — keep as-is
      TasksFunction.cs                 # NEW — GET /api/tasks, GET /api/tasks/{id}
      CommandFunction.cs               # NEW — POST /api/command
      ResultReceiverFunction.cs        # NEW — SB-triggered, receives FrontendResult from daeanne-frontend-results queue
      ResultFunction.cs                # NEW — GET /api/result/{correlationId}, reads from ResultReceiverFunction's cache
      TrendFunction.cs                 # NEW — GET /api/trends/today
      Middleware/
        IdentityGuard.cs              # NEW — reject non-Jeffrey identities
  Daeanne.Shared/
    Models/
      FrontendRequest.cs               # NEW — SB message: Prompt, CorrelationId, TaskType
      FrontendResult.cs                # NEW — SB message: CorrelationId, Succeeded, Response, Error
  Daeanne.Bridge/
    FrontendRelayWorker.cs             # NEW — SB processor for daeanne-frontend-requests
    FrontendRelayEndpoints.cs          # NEW — GET /relay/tasks, GET /relay/tasks/{id}
```

**Structure Decision**: Reuse the existing `Daeanne.Frontend` scaffold. The project already has the correct SWA 4-project layout (Client, Server, Shared, api). We rework it in-place rather than creating a new `Daeanne.Web` directory — this avoids migration overhead and keeps the existing solution references intact. Shared message contracts (`FrontendRequest`, `FrontendResult`) go in `Daeanne.Shared` so Bridge, Functions, and the API project all share the same types at compile time.

## Complexity Tracking

No constitution violations to justify — constitution not yet ratified.
