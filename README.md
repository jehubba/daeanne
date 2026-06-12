# Daeanne

Personal AI agent operating system. Daeanne is the Chief of Staff — she
orchestrates, plans, and decides. The infrastructure dispatches.

## Quick Links

- [Architecture Overview](docs/architecture/overview.md)
- [Development Roadmap](docs/roadmap.md)
- [Architecture Decisions](docs/architecture/adrs/)

## Related Repos

- [research-agent](https://github.com/jehubba/research-agent) — general-purpose
  research sub-agent (independent repo, symlinked)

## Setup

```powershell
# One-time setup — sets up Daeanne's agent symlink and Windows Services
# Run from an existing PowerShell session (not powershell -File)
. .\scripts\setup.ps1
```

Also run setup in the research-agent repo separately:
```powershell
cd ..\research-agent
. .\scripts\setup-symlinks.ps1
```

## MCP Server Wrapper (Dispatcher API)

Run the local MCP wrapper to expose Dispatcher endpoints as MCP tools:

```bash
python -m pip install -r scripts/requirements-mcp.txt
python scripts/daeanne_dispatcher_mcp.py
```

The wrapper reads the Dispatcher API key from:

- `~/.daeanne/secrets/dispatcher-api-key.txt`

Optional overrides:

- `DAEANNE_DISPATCHER_URL` (default: `http://127.0.0.1:47777`)
- `DAEANNE_DISPATCHER_KEY_FILE` (default shown above)

## Principal Preferences Memory

Daeanne maintains principal calibration preferences in:

- `%APPDATA%\daeanne\preferences.json`

The file is JSON, versioned, and safe to edit manually.

You can also update it with:

```powershell
.\scripts\Update-DaeannePreference.ps1 -Category Communication -Key tone -Value "direct, no pleasantries"
```

## Security

### Ingress — how email reaches Daeanne

The **actual** inbound path (as of 2026-06):

```
Email → daeanne-srs@outlook.com (Outlook personal account)
  → GraphMailWorker polls inbox every 60s via Microsoft Graph API (OAuth2)
  → BlockedSendersStore: two-tier filter
      Tier 1 — static config (Graph:IgnoredSenders): domain/address patterns set in appsettings
      Tier 2 — dynamic JSON (%APPDATA%\daeanne\blocked-senders.json): Daeanne-managed +
               auto-detected no-reply/notification patterns
  → POST /tasks to Kestrel Dispatcher (127.0.0.1:47777)
  → Dispatcher creates AgentTask (type=Email)
  → Daeanne agent processes it
```

> **Note:** `Daeanne.Functions/EmailIngestFunction` (ACS EventGrid path) is a stub — ACS
> inbound email is private preview and not active. The Graph polling path above is the
> live implementation.

### What is protected

| Control | Details |
|---------|---------|
| **Localhost-only Dispatcher** | Kestrel binds to `127.0.0.1:47777` — not reachable from the network |
| **No outbound network exposure** | No port forwarding, no public endpoint for the Dispatcher |
| **Blocked senders (two-tier)** | Static config patterns + dynamic agent-managed list with no-reply auto-detection |
| **Microsoft Graph auth** | OAuth2 refresh token; token persisted to `%APPDATA%\daeanne\graph-token.json` |
| **Outbound email via Graph** | Replies are sent through the same account; no unauthenticated SMTP |

### Known gaps / no mitigations yet

| Gap | Risk |
|-----|------|
| **No sender allowlist** | Any email to `daeanne-srs@outlook.com` creates an agent task (blocklist only) |
| **Prompt injection via email** | Email body is passed directly to Daeanne with no sanitization |
| **Refresh token on disk (unencrypted)** | `%APPDATA%\daeanne\graph-token.json` — readable by any process running as the user |
| **No rate limiting** | No cap on how many tasks can be created per sender per time window |
| **Dispatcher API unauthenticated** | `127.0.0.1:47777` has no auth — any local process can submit tasks or read task history |

## Repo Structure

```
daeanne/
├── docs/
│   ├── architecture/
│   │   ├── overview.md       ← full system architecture
│   │   └── adrs/             ← Architecture Decision Records
│   └── roadmap.md
├── agents/
│   └── daeanne.agent.md      ← Daeanne's Copilot agent profile
├── src/
│   ├── Daeanne.sln
│   ├── Daeanne.Dispatcher/   ← Kestrel reverse proxy + task dispatcher
│   ├── Daeanne.Bridge/       ← Service Bus ↔ HTTP bridge (Windows Service)
│   └── Daeanne.Shared/       ← shared models and API contracts
└── scripts/
    └── setup.ps1
```
