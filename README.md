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

## Principal Preferences Memory

Daeanne maintains principal calibration preferences in:

- `%APPDATA%\daeanne\preferences.json`

The file is JSON, versioned, and safe to edit manually.

You can also update it with:

```powershell
.\scripts\Update-DaeannePreference.ps1 -Category Communication -Key tone -Value "direct, no pleasantries"
```

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
