# Tasks: Daeanne Frontend Web App

**Input**: Design documents from `/specs/001-daeanne-frontend/`

**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅, quickstart.md ✅

**Tests**: MANDATORY — all test tasks are tagged `[TDD-AGENT]` and executed by the TDD Agent before `speckit.implement` runs.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- **[TDD-AGENT]**: Test task — written by TDD Agent, not speckit.implement
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization — add dependencies, remove boilerplate, configure MudBlazor

- [ ] T001 Add MudBlazor NuGet package to src/Daeanne.Frontend/Client/DaeanneFrontend.Client.csproj
- [ ] T002 [P] Add Azure.Messaging.ServiceBus NuGet package to src/Daeanne.Frontend/api/DaeanneFrontend.Api.csproj
- [ ] T003 [P] Add project reference to src/Daeanne.Shared/Daeanne.Shared.csproj from src/Daeanne.Frontend/api/DaeanneFrontend.Api.csproj
- [ ] T004 [P] Remove boilerplate files: Client/Pages/Counter.razor, Client/Pages/FetchData.razor, Client/Shared/SurveyPrompt.razor, Shared/WeatherForecast.cs, Server/Controllers/WeatherForecastController.cs
- [ ] T005 Configure MudBlazor services in src/Daeanne.Frontend/Client/Program.cs (AddMudServices)
- [ ] T006 [P] Update src/Daeanne.Frontend/Client/\_Imports.razor with MudBlazor namespaces (@using MudBlazor)
- [ ] T007 Rework src/Daeanne.Frontend/Client/App.razor with MudThemeProvider and MudDialogProvider
- [ ] T008 Update src/Daeanne.Frontend/Client/wwwroot/index.html — replace Bootstrap with MudBlazor CSS/JS, add viewport meta for mobile

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared DTOs, message contracts, identity guard, API client, Bridge relay — MUST be complete before any user story

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Shared DTOs (Frontend ↔ API)

- [ ] T009 [P] Create TaskDto record in src/Daeanne.Frontend/Shared/TaskDto.cs per data-model.md (Id, Type, Topic, Status, Age, CreatedAt, CompletedAt, ResultJson, Error, CorrelationId)
- [ ] T010 [P] Create CommandRequest record in src/Daeanne.Frontend/Shared/CommandRequest.cs per contracts/api.md (Prompt, TaskType)
- [ ] T011 [P] Create CommandResultDto record in src/Daeanne.Frontend/Shared/CommandResultDto.cs per contracts/api.md (CorrelationId, Status, Succeeded, Response, Error)
- [ ] T012 [P] Create TrendHighlightDto record in src/Daeanne.Frontend/Shared/TrendHighlightDto.cs per data-model.md (Date, Highlights list)

### Service Bus Message Contracts (Daeanne.Shared — cross-project)

- [ ] T013 [P] Create FrontendRequest record in src/Daeanne.Shared/Models/FrontendRequest.cs per research.md R5 (Prompt, CorrelationId, TaskType)
- [ ] T014 [P] Create FrontendResult record in src/Daeanne.Shared/Models/FrontendResult.cs per research.md R5 (CorrelationId, Succeeded, Response, Error)

### API Identity Guard

- [ ] T015 Create IdentityGuard middleware in src/Daeanne.Frontend/api/Middleware/IdentityGuard.cs — decode x-ms-client-principal header, reject non-Jeffrey identities per research.md R4
- [ ] T016 Register IdentityGuard middleware in src/Daeanne.Frontend/api/Program.cs

### Client Services

- [ ] T017 [P] Create DaeanneApiClient typed HTTP client in src/Daeanne.Frontend/Client/Services/DaeanneApiClient.cs — methods: GetTasksAsync, GetTaskAsync, SendCommandAsync, PollResultAsync, GetTrendsAsync
- [ ] T018 [P] Create ConnectivityService in src/Daeanne.Frontend/Client/Services/ConnectivityService.cs — tracks online/offline state, exposes IsOnline property and OnStatusChanged event
- [ ] T019 Register DaeanneApiClient and ConnectivityService in src/Daeanne.Frontend/Client/Program.cs

### Mobile Shell Layout

- [ ] T020 Rework src/Daeanne.Frontend/Client/Shared/MainLayout.razor — MudBlazor bottom tab bar (Tasks / Chat), mobile-first app shell per spec FR-017
- [ ] T021 [P] Remove or replace src/Daeanne.Frontend/Client/Shared/NavMenu.razor (sidebar nav → bottom tabs)

### Bridge Relay (Local — read path)

- [ ] T022 [P] Create FrontendRelayEndpoints in src/Daeanne.Bridge/FrontendRelayEndpoints.cs — GET /relay/tasks, GET /relay/tasks/{id}, GET /relay/trends/today (proxy to Dispatcher per contracts/api.md)
- [ ] T023 [P] Create FrontendRelayWorker in src/Daeanne.Bridge/FrontendRelayWorker.cs — ServiceBusProcessor for daeanne-frontend-requests queue, processes commands and sends results to daeanne-frontend-results per research.md R3
- [ ] T024 Register relay endpoints and FrontendRelayWorker in src/Daeanne.Bridge/Program.cs

**Checkpoint**: Foundation ready — all shared types compiled, identity guard active, Bridge relay running, client services registered. User story implementation can now begin.

---

## Phase 3: User Story 1 — Task Status Dashboard (Priority: P1) 🎯 MVP

**Goal**: Jeffrey opens the app and immediately sees a grouped task list (Running, Pending, Blocked, recently completed). List refreshes every 30 seconds and supports pull-to-refresh.

**Independent Test**: Open app → verify task list renders with correct groupings, status badges, and auto-refresh. Delivers ambient awareness value standalone.

### Tests for User Story 1

- [ ] T025 [TDD-AGENT] [P] [US1] Write contract tests for GET /api/tasks and GET /api/tasks/{id} in src/Daeanne.Frontend/api.Tests/TasksFunctionTests.cs — verify response shape, status filtering, 24h completion window, pagination, 502 on Bridge unreachable
- [ ] T026 [TDD-AGENT] [P] [US1] Write bUnit component tests for TaskList and TaskCard in src/Daeanne.Frontend/Client.Tests/Components/Tasks/TaskListTests.cs — verify grouping by status, empty state, age display, task type icons

### Implementation for User Story 1

- [ ] T027 [US1] Implement TasksFunction in src/Daeanne.Frontend/api/TasksFunction.cs — GET /api/tasks (paginated, filtered, 24h window for completed) and GET /api/tasks/{id} (full detail) per contracts/api.md
- [ ] T028 [P] [US1] Create TaskCard component in src/Daeanne.Frontend/Client/Components/Tasks/TaskCard.razor — displays type, topic, status badge (color per data-model.md mapping), and age
- [ ] T029 [US1] Create TaskList component in src/Daeanne.Frontend/Client/Components/Tasks/TaskList.razor — groups tasks by status (Running → Pending → Blocked → Succeeded → Failed), renders TaskCards, shows empty state
- [ ] T030 [US1] Create TasksPage in src/Daeanne.Frontend/Client/Pages/TasksPage.razor — 30s polling timer (FR-003), pull-to-refresh gesture (FR-018), calls DaeanneApiClient.GetTasksAsync, default route (/)
- [ ] T031 [US1] Remove src/Daeanne.Frontend/Client/Pages/Index.razor and wire TasksPage as default route @page "/"

**Checkpoint**: Task dashboard is fully functional — grouped list, auto-refresh, pull-to-refresh, empty state. Story 1 testable independently.

---

## Phase 4: User Story 2 — Send Commands via Chat (Priority: P1)

**Goal**: Jeffrey taps Chat tab, types a command, receives an acknowledgment within 3 seconds, and sees Daeanne's response in a conversational view.

**Independent Test**: Type a command → verify ack appears promptly → verify response renders when ready. Input preserved on failure.

### Tests for User Story 2

- [ ] T032 [TDD-AGENT] [P] [US2] Write contract tests for POST /api/command and GET /api/result/{correlationId} in src/Daeanne.Frontend/api.Tests/CommandFunctionTests.cs — verify 202 with correlationId, 400 on empty prompt, pending vs completed result states, 404 on unknown correlationId
- [ ] T033 [TDD-AGENT] [P] [US2] Write bUnit component tests for ChatView and ChatBubble in src/Daeanne.Frontend/Client.Tests/Components/Chat/ChatViewTests.cs — verify message rendering, send flow, ack display, error preservation (FR-016)

### Implementation for User Story 2

- [ ] T034 [US2] Implement CommandFunction in src/Daeanne.Frontend/api/CommandFunction.cs — POST /api/command: validate prompt, generate correlationId, send FrontendRequest to daeanne-frontend-requests SB queue, return 202 per contracts/api.md
- [ ] T035a [US2] Implement ResultReceiverFunction (SB-triggered) in src/Daeanne.Frontend/api/ResultReceiverFunction.cs — ServiceBusTrigger on daeanne-frontend-results queue, deserialize FrontendResult, store in static ConcurrentDictionary<string, FrontendResult> keyed by CorrelationId
- [ ] T035b [US2] Implement ResultFunction in src/Daeanne.Frontend/api/ResultFunction.cs — GET /api/result/{correlationId}: read from ResultReceiverFunction's shared ConcurrentDictionary, return pending if not found or completed/failed if present per contracts/api.md
- [ ] T036 [P] [US2] Create ChatBubble component in src/Daeanne.Frontend/Client/Components/Chat/ChatBubble.razor — sent vs received styling, timestamp, status indicator (Sending/Sent/Delivered/Error per data-model.md)
- [ ] T037 [US2] Create ChatView component in src/Daeanne.Frontend/Client/Components/Chat/ChatView.razor — message list, text input at bottom, send button, auto-scroll, input preservation on error (FR-016), recent exchange history (FR-007)
- [ ] T038 [US2] Create ChatPage in src/Daeanne.Frontend/Client/Pages/ChatPage.razor — route @page "/chat", orchestrates command submission via DaeanneApiClient.SendCommandAsync, polls for result via PollResultAsync, manages ChatMessage list per data-model.md command lifecycle
- [ ] T039 [P] [US2] Create ChatMessage model (Id, Direction, Content, Timestamp, Status enums) in src/Daeanne.Frontend/Client/Models/ChatMessage.cs — standalone file per data-model.md entity definition

**Checkpoint**: Chat interface is fully functional — send command, receive ack, see response. US2 testable independently.

---

## Phase 5: User Story 3 — Read Full Task Output (Priority: P2)

**Goal**: Jeffrey taps a completed task to view its full output in a scrollable overlay on mobile.

**Independent Test**: Tap completed task → verify full output renders in overlay → verify scrollable at 390px → no horizontal scroll.

### Tests for User Story 3

- [ ] T040 [TDD-AGENT] [US3] Write bUnit component tests for TaskDetailOverlay in src/Daeanne.Frontend/Client.Tests/Components/Tasks/TaskDetailOverlayTests.cs — verify overlay opens with full ResultJson, scrollable content, close behavior, error display for failed tasks

### Implementation for User Story 3

- [ ] T041 [US3] Create TaskDetailOverlay component in src/Daeanne.Frontend/Client/Components/Tasks/TaskDetailOverlay.razor — MudOverlay/MudDrawer with scrollable content, displays ResultJson or Error, close button, mobile-friendly per spec FR-008
- [ ] T042 [US3] Wire overlay trigger from TaskCard tap in src/Daeanne.Frontend/Client/Pages/TasksPage.razor — on card click, fetch full task via DaeanneApiClient.GetTaskAsync, open TaskDetailOverlay

**Checkpoint**: Task detail drill-down works — tap task → see output → scroll → close. US3 testable independently.

---

## Phase 6: User Story 4 — Trend Highlights (Priority: P2)

**Goal**: Today's trend highlights appear automatically on the task dashboard when trend data is available.

**Independent Test**: Verify dashboard shows highlights when TrendAnalyzer tasks have completed today. Verify graceful empty/hidden state when no trend data exists.

### Tests for User Story 4

- [ ] T043 [TDD-AGENT] [P] [US4] Write contract tests for GET /api/trends/today in src/Daeanne.Frontend/api.Tests/TrendFunctionTests.cs — verify response shape, empty highlights array when no data, correct date
- [ ] T044 [TDD-AGENT] [P] [US4] Write bUnit component tests for TrendHighlights in src/Daeanne.Frontend/Client.Tests/Components/Dashboard/TrendHighlightsTests.cs — verify highlights render, empty state hidden

### Implementation for User Story 4

- [ ] T045 [US4] Implement TrendFunction in src/Daeanne.Frontend/api/TrendFunction.cs — GET /api/trends/today: relay to Bridge GET /relay/trends/today, return TrendHighlightDto per contracts/api.md
- [ ] T046 [US4] Create TrendHighlights component in src/Daeanne.Frontend/Client/Components/Dashboard/TrendHighlights.razor — displays today's trend summary, hidden when no data (FR-014)
- [ ] T047 [US4] Integrate TrendHighlights into TasksPage in src/Daeanne.Frontend/Client/Pages/TasksPage.razor — render above task list, load via DaeanneApiClient.GetTrendsAsync

**Checkpoint**: Trend highlights visible on dashboard when data exists. US4 testable independently.

---

## Phase 7: User Story 5 — PWA Installation and Mobile Experience (Priority: P1)

**Goal**: App installable to home screen, launches standalone (no browser chrome), fully functional at 390px.

**Independent Test**: Visit in mobile browser → install to home screen → verify standalone mode → verify all features work at 390px viewport.

### Tests for User Story 5

- [ ] T048 [TDD-AGENT] [US5] Write Playwright E2E tests for PWA installability and responsive viewport in src/Daeanne.Frontend/E2E.Tests/PwaTests.cs — verify manifest presence, service worker registration, 390px viewport usability

### Implementation for User Story 5

- [ ] T049 [P] [US5] Create manifest.webmanifest in src/Daeanne.Frontend/Client/wwwroot/manifest.webmanifest — name "Daeanne", display "standalone", theme color, start URL "/", icons 192px + 512px
- [ ] T050 [P] [US5] Create service-worker.js in src/Daeanne.Frontend/Client/wwwroot/service-worker.js — cache app shell assets for offline launch
- [ ] T051 [P] [US5] Add PWA icons (192px, 512px PNG) to src/Daeanne.Frontend/Client/wwwroot/icons/
- [ ] T052 [US5] Update src/Daeanne.Frontend/Client/wwwroot/index.html — add manifest link, service-worker registration script, apple-touch-icon meta tags
- [ ] T053 [US5] Configure PWA service worker in src/Daeanne.Frontend/Client/DaeanneFrontend.Client.csproj — add ServiceWorkerAssetsManifest property

**Checkpoint**: App installs as PWA, launches standalone, fully usable at 390px. US5 testable independently.

---

## Phase 8: User Story 6 — Authentication and Security (Priority: P1)

**Goal**: All routes and API endpoints require authentication. Only Jeffrey's identity is permitted. No local ports exposed.

**Independent Test**: Unauthenticated access → redirect to login. Auth as Jeffrey → full access. Auth as other identity → 403. Inspect network config → no exposed ports.

### Tests for User Story 6

- [ ] T054 [TDD-AGENT] [US6] Write integration tests for IdentityGuard in src/Daeanne.Frontend/api.Tests/IdentityGuardTests.cs — verify rejection of missing header, invalid principal, non-Jeffrey identity; verify acceptance of Jeffrey's identity

### Implementation for User Story 6

- [ ] T055 [US6] Harden src/Daeanne.Frontend/staticwebapp.config.json — restrict /api/\* routes to authenticated users (remove anonymous), verify response overrides redirect 401 → /.auth/login/aad
- [ ] T056 [US6] Add auth state detection to Blazor client — login/logout awareness in src/Daeanne.Frontend/Client/Program.cs or a shared auth state service, display user greeting in MainLayout

**Checkpoint**: Auth gates all access. Only Jeffrey permitted. No port exposure. US6 testable independently.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories — offline indicator, error states, validation

- [ ] T057 [P] Create OfflineIndicator component in src/Daeanne.Frontend/Client/Components/Shared/OfflineIndicator.razor — banner shows when ConnectivityService.IsOnline is false (FR-015)
- [ ] T058 Wire OfflineIndicator into MainLayout in src/Daeanne.Frontend/Client/Shared/MainLayout.razor — persistent banner below app bar
- [ ] T059 [P] Add empty-state messages to TasksPage (no tasks), ChatPage (no messages), TrendHighlights (no trends) per edge cases in spec.md
- [ ] T060 Verify input text preservation on network failure in ChatView per FR-016 and data-model.md error flow
- [ ] T061 Run quickstart.md validation scenarios V1–V7 end-to-end
- [ ] T062 [US1] Add pagination support to TasksPage — "load more" button or infinite scroll using skip/take query params from DaeanneApiClient.GetTasksAsync per contracts/api.md pagination parameters
- [ ] T063 Verify published Blazor WASM bundle size < 1MB — run `dotnet publish -c Release` on Client project and check total size of `wwwroot/_framework/*.dll` per plan.md constraint

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — **BLOCKS all user stories**
- **US1 (Phase 3)**: Depends on Foundational (Phase 2) — no other story dependencies
- **US2 (Phase 4)**: Depends on Foundational (Phase 2) — no other story dependencies
- **US3 (Phase 5)**: Depends on US1 (Phase 3) — needs TaskCard + TasksPage for drill-down trigger
- **US4 (Phase 6)**: Depends on Foundational (Phase 2) — integrates into TasksPage but only adds a section
- **US5 (Phase 7)**: Depends on Setup (Phase 1) — can proceed in parallel with user stories (pure static assets + config)
- **US6 (Phase 8)**: Depends on Foundational (Phase 2) — IdentityGuard is in Foundational, this phase hardens and tests
- **Polish (Phase 9)**: Depends on all desired user stories being complete

### User Story Dependencies

- **US1 (P1)**: Independent — can start after Foundational
- **US2 (P1)**: Independent — can start after Foundational, parallel with US1
- **US3 (P2)**: Depends on US1 — needs the task list and card components to add drill-down
- **US4 (P2)**: Weakly depends on US1 — integrates into TasksPage but can be built in parallel if TasksPage shell exists
- **US5 (P1)**: Independent — PWA manifest/service worker are standalone assets
- **US6 (P1)**: Independent — auth hardening is orthogonal to feature development

### Within Each User Story

1. `[TDD-AGENT]` test tasks MUST be written and FAIL before implementation
2. API Functions before client components (client needs something to call)
3. Leaf components (Card, Bubble) before container components (List, View)
4. Container components before page components
5. Story complete before moving to next priority

### Parallel Opportunities

- **Phase 1**: T002, T003, T004, T006 can all run in parallel
- **Phase 2**: T009–T014 (all DTOs) in parallel; T017, T018 in parallel; T021–T023 in parallel
- **Phase 3 + 4**: US1 and US2 can proceed in parallel after Foundational
- **Phase 5 + 6 + 7**: US3, US4, US5 can overlap (US3 needs US1 done; US4 and US5 do not)
- **Phase 8**: US6 can proceed in parallel with any user story after Foundational

---

## Parallel Example: User Story 1

```bash
# Launch all tests for US1 together (TDD Agent writes these first):
Task T025: "Contract tests for TasksFunction in api.Tests/TasksFunctionTests.cs"
Task T026: "bUnit tests for TaskList and TaskCard in Client.Tests/Components/Tasks/TaskListTests.cs"

# After tests fail, launch implementation:
Task T027: "TasksFunction in api/TasksFunction.cs"           # API first
Task T028: "TaskCard component in Client/Components/Tasks/"   # Leaf component (parallel)

# Then sequential:
Task T029: "TaskList component" (depends on T028)
Task T030: "TasksPage" (depends on T029)
Task T031: "Wire default route" (depends on T030)
```

---

## Parallel Example: US1 + US2 in Parallel

```bash
# After Foundational (Phase 2) completes:

# Developer A (US1):                    # Developer B (US2):
T025 TDD tests for Tasks API            T032 TDD tests for Command API
T026 TDD tests for TaskList/Card        T033 TDD tests for ChatView/Bubble
T027 TasksFunction                       T034 CommandFunction
T028 TaskCard                            T035 ResultFunction
T029 TaskList                            T036 ChatBubble
T030 TasksPage                           T037 ChatView
T031 Wire default route                  T038 ChatPage + T039 ChatMessage model
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (**CRITICAL** — blocks all stories)
3. Complete Phase 3: User Story 1 (Task Dashboard)
4. **STOP and VALIDATE**: Run quickstart.md V1 scenario — task list renders, refreshes, groups correctly
5. Deploy if ready — Jeffrey gets ambient task awareness immediately

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. Add US1 (Task Dashboard) → Test V1 → **Deploy (MVP!)**
3. Add US2 (Chat) → Test V2 → Deploy (core feature complete)
4. Add US5 (PWA) + US6 (Auth hardening) → Test V5, V6 → Deploy (production-ready)
5. Add US3 (Task Output) → Test V3 → Deploy (P2 content access)
6. Add US4 (Trends) → Test V4 → Deploy (P2 ambient intelligence)
7. Polish → Test V7 → Final deploy

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [TDD-AGENT] tasks are executed by the TDD Agent, not speckit.implement
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable
- File paths are relative to repository root (`src/Daeanne.Frontend/...`)
- Bridge changes (`src/Daeanne.Bridge/...`) are in Foundational because both read and write paths depend on them
- Verify tests fail before implementing (Red → Green → Refactor)
- Commit after each task or logical group
