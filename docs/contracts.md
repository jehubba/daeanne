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

## 6. Music Search

### Client → Functions: `GET /api/music/search?q={query}`

**Response** (200 OK) — `MusicSearchResultDto` in `Frontend/Shared/MusicSearchResultDto.cs`:
```json
{
  "query": "string",
  "title": "string | null",
  "artist": "string | null",
  "key": "string | null",
  "tempo": "string | null",
  "source": "known | generated | error",
  "chords": [
    { "name": "G", "frets": "320003", "fingers": "210003" }
  ],
  "sections": [
    {
      "label": "Verse 1",
      "lines": [
        { "chords": ["G", "Em"], "lyrics": "string | null" }
      ]
    }
  ],
  "error": "string | null"
}
```

`source` values:
- `"known"` — model has reliable training-data knowledge of the song's chords
- `"generated"` — model generated a reasonable approximation
- `"error"` — lookup failed; see `error` field

`frets` format: 6 characters, strings 6→1 (low E to high e). `x` = muted, `0` = open, `1`–`9` = fret number.

**Notes**:
- The Functions project calls Azure OpenAI directly (no Bridge relay).
- Requires `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_KEY`, and optionally `AZURE_OPENAI_DEPLOYMENT` (default: `gpt-4o`).
- Returns HTTP 200 even on soft failures (missing config, parse error) — check the `source` field.

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

## Serialization Conventions

- **Service Bus messages**: `camelCase`, case-insensitive deserialization.
- **Dispatcher API**: `camelCase` (ASP.NET default). Enums serialized as strings.
- **Bridge relay**: `camelCase`, case-insensitive. Anonymous objects mirror `TaskDto` field names.
- **Functions API**: `camelCase`. Passthrough for task data, explicit anonymous objects for result data.
- **Blazor Client**: System.Text.Json defaults (`camelCase` for request, case-insensitive for response).

**⚠ Timestamp convention**: The Dispatcher stores `DateTime.UtcNow` but serializes without timezone suffix.
All consumers must treat bare timestamps (no `Z`, no offset) from the Dispatcher as UTC.
