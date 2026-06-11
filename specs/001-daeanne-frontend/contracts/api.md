# API Contracts: Daeanne Frontend

**Date**: 2026-06-11

All endpoints are SWA-managed Azure Functions, prefixed with `/api/`. All require authentication (SWA Easy Auth) and pass the identity guard (Jeffrey only).

---

## Tasks

### GET /api/tasks

Returns the task list for the dashboard.

**Query Parameters**:

| Param  | Type    | Default | Description                        |
| ------ | ------- | ------- | ---------------------------------- |
| status | string? | null    | Filter by status (comma-separated) |
| type   | string? | null    | Filter by task type                |
| skip   | int     | 0       | Pagination offset                  |
| take   | int     | 50      | Page size (max 200)                |

**Response** `200 OK`:

```json
{
  "tasks": [
    {
      "id": 42,
      "type": "Research",
      "topic": "Emerging trends in AI agent frameworks",
      "status": "Succeeded",
      "age": "2h ago",
      "createdAt": "2026-06-11T08:30:00Z",
      "completedAt": "2026-06-11T10:15:00Z",
      "correlationId": "daily-trend-2026-06-11"
    }
  ],
  "total": 12
}
```

**Error Responses**:

- `401` — Not authenticated (SWA redirects to login)
- `403` — Authenticated but not Jeffrey
- `502` — Bridge/Dispatcher unreachable

### GET /api/tasks/{id}

Returns a single task with full output.

**Response** `200 OK`:

```json
{
  "id": 42,
  "type": "Research",
  "topic": "Emerging trends in AI agent frameworks",
  "status": "Succeeded",
  "age": "2h ago",
  "createdAt": "2026-06-11T08:30:00Z",
  "completedAt": "2026-06-11T10:15:00Z",
  "resultJson": "{ full task output here }",
  "error": null,
  "correlationId": "daily-trend-2026-06-11"
}
```

**Error Responses**:

- `404` — Task not found
- `502` — Bridge/Dispatcher unreachable

---

## Commands

### POST /api/command

Sends a free-text command to Daeanne via the Service Bus async path.

**Request Body**:

```json
{
  "prompt": "Research the latest developments in MCP servers",
  "taskType": "Generic"
}
```

| Field    | Type   | Required | Description                            |
| -------- | ------ | -------- | -------------------------------------- |
| prompt   | string | Yes      | Free-text command (max 4000 chars)     |
| taskType | string | No       | Task type override (default "Generic") |

**Response** `202 Accepted`:

```json
{
  "correlationId": "fe-cmd-a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "message": "Command submitted"
}
```

**Error Responses**:

- `400` — Empty or too-long prompt
- `503` — Service Bus unavailable

### GET /api/result/{correlationId}

Polls for the result of a previously submitted command.

**Response** `200 OK` (result available):

```json
{
  "correlationId": "fe-cmd-a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "status": "completed",
  "succeeded": true,
  "response": "Here are the latest developments in MCP servers...",
  "error": null
}
```

**Response** `200 OK` (still pending):

```json
{
  "correlationId": "fe-cmd-a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "status": "pending",
  "succeeded": null,
  "response": null,
  "error": null
}
```

**Error Responses**:

- `404` — Unknown correlationId

---

## Trends

### GET /api/trends/today

Returns today's trend highlights derived from recent TrendAnalyzer task completions.

**Response** `200 OK`:

```json
{
  "date": "2026-06-11",
  "highlights": [
    "AI agent frameworks seeing rapid adoption — LangGraph, CrewAI, AutoGen leading",
    "MCP protocol gaining traction as standard for tool integration"
  ]
}
```

**Response** `200 OK` (no trends today):

```json
{
  "date": "2026-06-11",
  "highlights": []
}
```

---

## Bridge Relay Endpoints (internal — not exposed to internet)

These endpoints run on the Bridge at `127.0.0.1:47778` and are only callable by the SWA Functions (via the Functions → Bridge connection string).

### GET /relay/tasks

Proxies to Dispatcher `GET /tasks`. Passes through query parameters.

### GET /relay/tasks/{id}

Proxies to Dispatcher `GET /tasks/{id}`.

### GET /relay/trends/today

Queries Dispatcher for recent TrendAnalyzer tasks and extracts highlights.

---

## Service Bus Message Contracts

### Queue: daeanne-frontend-requests

**Direction**: Cloud → Local (Functions → Bridge)

```json
{
  "prompt": "Research the latest developments in MCP servers",
  "correlationId": "fe-cmd-a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "taskType": "Generic"
}
```

### Queue: daeanne-frontend-results

**Direction**: Local → Cloud (Bridge → Functions)

```json
{
  "correlationId": "fe-cmd-a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "succeeded": true,
  "response": "Here are the latest developments...",
  "error": null
}
```
