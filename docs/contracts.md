# Daeanne Inter-Service Contracts

> **Rule**: Every payload that crosses a service boundary is defined here.
> When you change a producer or consumer, update this document **and** all
> other sides of the contract in the same commit. See `.github/copilot-instructions.md`.

## Service Boundary Map

```
┌─────────────┐   HTTP    ┌──────────────┐  Service Bus  ┌──────────────┐   HTTP    ┌────────────┐
│  Blazor     │ ────────► │  Functions   │ ────────────► │    Bridge    │ ────────► │ Dispatcher │
│  Client     │ ◄──────── │  (SWA API)   │ ◄──────────── │              │ ◄──────── │            │
└─────────────┘           └──────────────┘               └──────────────┘           └────────────┘
  WASM app                  api/ project                  Daeanne.Bridge             Daeanne.Dispatcher
  Daeanne.Frontend.Client   Daeanne.Frontend/api          :47778                    :47777
```

---

## 1. Command Submission (Chat → Functions → SB → Bridge → Dispatcher)

### 1a. Client → Functions: `POST /api/command`

**Request body** (`CommandRequest` in `Shared/CommandRequest.cs`):
```json
{ "prompt": "string (required, max 4000)", "taskType": "string (default: Generic)" }
```

**Response** (202 Accepted):
```json
{ "correlationId": "fe-cmd-{guid}", "message": "Command submitted" }
```

### 1b. Functions → Service Bus queue `daeanne-frontend-requests`

**Message body** (`FrontendRequest` in `Daeanne.Shared/Models/FrontendRequest.cs`):
```json
{ "prompt": "string", "correlationId": "string", "taskType": "string" }
```

Serialized with `camelCase` naming policy.

### 1c. Bridge → Dispatcher: `POST /tasks`

**Request body** (`CreateTaskRequest` in `Daeanne.Shared/Requests/CreateTaskRequest.cs`):
```json
{
  "type": "string (AgentTaskType enum: Generic, Research, Email, ...)",
  "prompt": "string",
  "correlationId": "string | null"
}
```

**Response** (201 Created) — raw `AgentTask` entity:
```json
{
  "id": "guid-string",
  "type": "Generic",
  "prompt": "string",
  "contextJson": "string | null",
  "status": "Pending",
  "createdAt": "2026-06-12T01:48:23.074447",
  "updatedAt": "2026-06-12T01:48:23.074447",
  "startedAt": null,
  "completedAt": null,
  "attemptCount": 0,
  "correlationId": "string | null",
  "isScheduled": false,
  "scheduledJobId": null,
  "sessionName": null,
  "parentTaskId": null,
  "callbackAcknowledgedAt": null,
  "callbackPostedAt": null,
  "resultJson": null,
  "error": null,
  "agentReported": false,
  "promotedAt": null
}
```

**⚠ Timestamp format**: Dispatcher stores UTC but serializes **without** a trailing `Z` or offset. Consumers must treat bare timestamps as UTC.

---

## 2. Result Return (Bridge → SB → Functions → Blob → Client)

### 2a. Bridge → Service Bus queue `daeanne-frontend-results`

**Message body** (`FrontendResult` in `Daeanne.Shared/Models/FrontendResult.cs`):
```json
{ "correlationId": "string", "succeeded": true, "response": "string | null", "error": "string | null" }
```

- `response`: Human-readable text extracted from `resultJson` by `FrontendRelayWorker.ExtractResponseText()`.
  Tries fields `response`, `result`, `message`, `summary`, `output` from the JSON, falls back to raw string.

### 2b. Functions → Blob Storage (container `frontend-results`)

Stored as `{correlationId}.json` with the `FrontendResult` shape above.

### 2c. Client → Functions: `GET /api/result/{correlationId}`

**Response** (200 OK):
```json
{
  "correlationId": "string",
  "status": "pending | completed | failed",
  "succeeded": true | false | null,
  "response": "string | null",
  "error": "string | null"
}
```

Consumed by `CommandResultDto` in `Shared/CommandResultDto.cs`.

---

## 3. Task List (Client → Functions → Bridge → Dispatcher)

### 3a. Client → Functions: `GET /api/tasks?skip=0&take=50`

### 3b. Functions → Bridge: `GET /relay/tasks?skip=0&take=50`

### 3c. Bridge → Dispatcher: `GET /tasks?skip=0&take=50`

**Dispatcher response** — raw array of `AgentTask` entities:
```json
[
  { "id": "guid", "type": "Generic", "prompt": "...", "status": "Succeeded", "createdAt": "...", ... },
  ...
]
```

### 3d. Bridge relay transforms to frontend shape

**Bridge response** (`/relay/tasks`) — mapped wrapper:
```json
{
  "tasks": [
    {
      "id": "guid-string",
      "type": "string (AgentTaskType)",
      "topic": "string (first line of prompt, max 80 chars)",
      "status": "string (AgentTaskStatus)",
      "age": "string (e.g. '5m ago', '2h ago', '1d ago')",
      "createdAt": "ISO 8601 datetime",
      "completedAt": "ISO 8601 datetime | null",
      "resultJson": "string | null",
      "error": "string | null",
      "correlationId": "string | null"
    }
  ],
  "total": 42
}
```

Consumed by `TaskListResponse` (private record in `DaeanneApiClient.cs`) and `TaskDto` in `Shared/TaskDto.cs`.

**Field mapping** (Dispatcher → Frontend):
| Dispatcher field | Frontend field | Transformation |
|---|---|---|
| `id` (Guid) | `id` (string) | As-is |
| `type` (enum string) | `type` | As-is |
| `prompt` | `topic` | First line, truncated to 80 chars |
| `status` (enum string) | `status` | As-is |
| *(computed)* | `age` | `DateTime.UtcNow - createdAt`, formatted |
| `createdAt` | `createdAt` | Treated as UTC |
| `completedAt` | `completedAt` | Treated as UTC |
| `resultJson` | `resultJson` | As-is |
| `error` | `error` | As-is |
| `correlationId` | `correlationId` | As-is |

---

## 4. Single Task Detail

### Client → Functions → Bridge → Dispatcher: `GET /api/tasks/{id}`

Same chain as task list but for a single task.

- Dispatcher returns a single `AgentTask` JSON object (not an array).
- Bridge `MapTask()` transforms it to the same `TaskDto`-compatible shape.
- Frontend deserializes as `TaskDto`.

---

## 5. Trends

### Client → Functions → Bridge → Dispatcher: `GET /api/trends/today`

- Bridge calls `GET /tasks?type=TrendAnalyzer&status=Succeeded&take=10` on Dispatcher
- Currently passes raw response through (no transformation)
- Client expects `TrendHighlightDto`: `{ "date": "string", "highlights": ["string"] }`
- **⚠ Not yet fully wired** — the Bridge does not yet extract/format highlights

---

## Shared Types Reference

| Type | Location | Used By |
|---|---|---|
| `FrontendRequest` | `Daeanne.Shared/Models/FrontendRequest.cs` | Functions (producer), Bridge (consumer) |
| `FrontendResult` | `Daeanne.Shared/Models/FrontendResult.cs` | Bridge (producer), Functions (consumer) |
| `AgentTask` | `Daeanne.Shared/Models/AgentTask.cs` | Dispatcher (entity), Bridge (consumer via JSON) |
| `AgentTaskType` | `Daeanne.Shared/Models/AgentTaskType.cs` | Dispatcher, Bridge |
| `AgentTaskStatus` | `Daeanne.Shared/Models/AgentTaskStatus.cs` | Dispatcher, Bridge, Frontend (display) |
| `CreateTaskRequest` | `Daeanne.Shared/Requests/CreateTaskRequest.cs` | Bridge (producer), Dispatcher (consumer) |
| `TaskDto` | `Frontend/Shared/TaskDto.cs` | Client (consumer) |
| `CommandRequest` | `Frontend/Shared/CommandRequest.cs` | Client (producer), Functions (consumer) |
| `CommandResultDto` | `Frontend/Shared/CommandResultDto.cs` | Client (consumer), Functions (producer) |

---

## 6. Push Notifications (Web Push + VAPID)

### 6a. Client → Functions: `POST /api/subscribe`

Stores a browser `PushSubscription` so the server can fan out push notifications.
Called by `push-interop.js` after `PushManager.subscribe()` succeeds.

**Request body** (standard browser `PushSubscription.toJSON()` shape):
```json
{
  "endpoint": "https://fcm.googleapis.com/...",
  "keys": { "p256dh": "Base64url-string", "auth": "Base64url-string" }
}
```

**Response** (200 OK):
```json
{ "message": "Subscription saved" }
```

Subscriptions are stored in Azure Blob Storage, container `push-subscriptions`, one blob per endpoint (named by SHA-256 hash of the endpoint URL).

### 6b. Bridge → Functions: `POST /api/notify` (internal)

Called by `Daeanne.Bridge.FrontendRelayWorker` after a task reaches a terminal status.
Secured by `X-Internal-Key` header matching the `NOTIFY_INTERNAL_KEY` environment variable.
If `Bridge:FrontendApiUrl` is empty, the call is silently skipped.

**Request header**: `X-Internal-Key: {NOTIFY_INTERNAL_KEY}`

**Request body**:
```json
{
  "type": "task_complete | escalation | alert | summary",
  "title": "string",
  "body":  "string",
  "taskId": "guid-string | null",
  "url":   "/tasks/{id}"
}
```

**Response** (200 OK):
```json
{ "sent": 3, "failed": 0 }
```

Fan-out uses VAPID Web Push (library: `WebPush` 1.0.13).
Stale subscriptions (HTTP 410 Gone) are removed automatically.

### 6c. Client → Functions: `GET /api/push/vapid-public-key`

Returns the VAPID public key so the client can call `PushManager.subscribe()`.

**Response** (200 OK):
```json
{ "publicKey": "Base64url-encoded ECDSA P-256 public key" }
```

Returns 404 if `VAPID_PUBLIC_KEY` is not configured.

### Required environment variables (SWA app settings)

| Variable | Description |
|---|---|
| `VAPID_PUBLIC_KEY` | Base64url ECDSA P-256 public key (safe to expose to client) |
| `VAPID_PRIVATE_KEY` | Base64url ECDSA P-256 private key (**secret — never commit**) |
| `VAPID_SUBJECT` | `mailto:` or `https:` claim for VAPID JWT |
| `NOTIFY_INTERNAL_KEY` | Shared secret for Bridge → Functions call (**secret**) |

### Shared Bridge configuration keys

| Key | Description |
|---|---|
| `Bridge:FrontendApiUrl` | Base URL of the SWA Functions (e.g., `https://daeanne.azurestaticapps.net/api`) |
| `Bridge:FrontendInternalKey` | Must match `NOTIFY_INTERNAL_KEY` (**secret**) |

### Client-side behavior

- **Push subscription**: `PushInteropService.SubscribeToPushAsync(vapidPublicKey)` (called on Tasks page load)
- **Badge API**: `PushInteropService.SetBadgeAsync(n)` / `ClearBadgeAsync()` (cleared on Tasks page load)
- **Web Share**: `PushInteropService.ShareTaskAsync(title, text, url)` (share button in TaskDetailOverlay)
- **iOS requirement**: Web Push requires iOS 16.4+ in standalone PWA mode

---

## Serialization Conventions

- **Service Bus messages**: `camelCase`, case-insensitive deserialization.
- **Dispatcher API**: `camelCase` (ASP.NET default). Enums serialized as strings.
- **Bridge relay**: `camelCase`, case-insensitive. Anonymous objects mirror `TaskDto` field names.
- **Functions API**: `camelCase`. Passthrough for task data, explicit anonymous objects for result data.
- **Blazor Client**: System.Text.Json defaults (`camelCase` for request, case-insensitive for response).

**⚠ Timestamp convention**: The Dispatcher stores `DateTime.UtcNow` but serializes without timezone suffix.
All consumers must treat bare timestamps (no `Z`, no offset) from the Dispatcher as UTC.
