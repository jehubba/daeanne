# Quickstart: Daeanne Frontend Web App

**Date**: 2026-06-11

This guide describes how to validate the feature end-to-end after implementation.

## Prerequisites

- .NET 8 SDK
- Node.js 18+ (for SWA CLI)
- Azure Static Web Apps CLI (`npm install -g @azure/static-web-apps-cli`)
- Azure Service Bus namespace with queues `daeanne-frontend-requests` and `daeanne-frontend-results`
- Daeanne Dispatcher running locally at `127.0.0.1:47777`
- Daeanne Bridge running locally at `127.0.0.1:47778`
- A `local.settings.json` in the `api/` project with Service Bus connection string

## Local Development Setup

```bash
# From repo root
cd src/Daeanne.Frontend

# Restore and build
dotnet restore DaeanneFrontend.sln
dotnet build DaeanneFrontend.sln

# Start the SWA emulator (serves Client, proxies to api Functions)
swa start --app-location Client --api-location api --output-location Client/bin/Debug/net8.0/wwwroot
```

The SWA CLI starts:

- Blazor WASM client on the emulated SWA URL
- Azure Functions host for the api project
- Auth emulator at `/.auth/login/aad`

## Validation Scenarios

### V1: Task Dashboard (Story 1 — P1)

1. Ensure Dispatcher has some tasks (create a few via `POST http://127.0.0.1:47777/tasks`)
2. Open the app in a mobile-sized browser window (390px wide)
3. **Verify**: Tasks tab is the default view with a grouped task list
4. **Verify**: Each task shows type, topic, status, and age
5. **Verify**: After 30 seconds, the list refreshes automatically
6. **Verify**: Pull-to-refresh gesture triggers an immediate refresh
7. **Verify**: If no tasks exist, an empty state message appears

### V2: Chat / Command Input (Story 2 — P1)

1. Tap the Chat tab
2. Type "What tasks are running?" and tap send
3. **Verify**: An acknowledgment appears within 3 seconds
4. **Verify**: Daeanne's response appears in the conversational view once processing completes
5. **Verify**: Recent exchange history is visible as context
6. **Verify**: If the command fails, the input text is preserved and an error is shown

### V3: Task Detail (Story 3 — P2)

1. On the Tasks tab, tap a completed (Succeeded) task
2. **Verify**: A detail overlay shows the full task output
3. **Verify**: The output is scrollable and readable on a 390px viewport
4. **Verify**: No horizontal scrolling is required

### V4: Trend Highlights (Story 4 — P2)

1. Ensure a TrendAnalyzer task completed today
2. Open the Tasks tab
3. **Verify**: Trend highlights section is visible on the dashboard
4. **Verify**: If no trend data exists, the section is hidden or shows a graceful empty state

### V5: PWA Installation (Story 5 — P1)

1. Open the app in Chrome for Android or Safari on iOS
2. **Verify**: Browser offers "Add to Home Screen" / install prompt
3. Install the app
4. **Verify**: App launches without browser chrome (standalone mode)
5. **Verify**: App is fully functional on a 390px viewport

### V6: Authentication (Story 6 — P1)

1. Open the app in an incognito window
2. **Verify**: Redirected to `/.auth/login/aad`
3. Authenticate with Jeffrey's Microsoft identity
4. **Verify**: Full access to the app
5. Open in another incognito window with a different Microsoft identity
6. **Verify**: Access denied (403 from identity guard)

### V7: Offline Behavior (Edge Case)

1. Open the app while authenticated
2. Stop the Bridge service
3. **Verify**: An "offline" or "Daeanne is unreachable" indicator appears
4. **Verify**: Last-known task data remains visible
5. Type a command in the chat
6. **Verify**: Input text is preserved even if submission fails

## Running Tests

```bash
# Unit + component tests (bUnit)
cd src/Daeanne.Frontend
dotnet test

# API function tests
cd src/Daeanne.Frontend/api
dotnet test

# E2E tests (requires running SWA emulator)
# Playwright tests TBD in tasks.md
```

## Deployment

```bash
# Build for production
cd src/Daeanne.Frontend/Client
dotnet publish -c Release

# Deploy via SWA CLI or GitHub Actions
swa deploy --app-location Client/bin/Release/net8.0/publish/wwwroot --api-location api
```

See [contracts/api.md](contracts/api.md) for full API contract details.
See [data-model.md](data-model.md) for entity definitions and state transitions.
