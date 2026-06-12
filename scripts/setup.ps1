# Setup — Daeanne
#
# One-time setup script. Run by dot-sourcing in an existing PowerShell session:
#   . .\scripts\setup.ps1
#
# What this does:
#   1. Creates ~/.copilot/agents/daeanne.agent.md symlink
#   2. Creates %APPDATA%\daeanne\memory directory for MCP memory persistence
#   3. (Phase 1+) Installs Daeanne.Dispatcher as Windows Service
#   4. (Phase 1+) Installs Daeanne.Bridge as Windows Service

$ErrorActionPreference = "Stop"

$repoRoot  = Split-Path -Parent $PSScriptRoot
$agentsDir = "$HOME\.copilot\agents"

# --- Agent symlinks ---

function Set-AgentSymlink($name) {
    $source = Join-Path $repoRoot "agents\$name.agent.md"
    $link   = Join-Path $agentsDir "$name.agent.md"

    if (-not (Test-Path $source)) {
        Write-Warning "Agent file not found, skipping: $source"
        return
    }

    New-Item -ItemType Directory -Path $agentsDir -Force | Out-Null

    if (Test-Path $link) { Remove-Item $link -Force }

    New-Item -ItemType SymbolicLink -Path $link -Target $source | Out-Null
    Write-Host "Symlink: $link -> $source"
}

Set-AgentSymlink "daeanne"

$memoryDir = Join-Path $env:APPDATA "daeanne\memory"
New-Item -ItemType Directory -Path $memoryDir -Force | Out-Null
Write-Host "MCP memory directory ready: $memoryDir"

Write-Host ""
Write-Host "Daeanne agent symlink configured."
Write-Host "MCP memory server config is in .github/mcp.json (Copilot CLI) and .vscode/mcp.json (VS Code)."
Write-Host "Also run setup in the research-agent repo if not already done:"
Write-Host "  cd ..\research-agent && . .\scripts\setup-symlinks.ps1"
Write-Host ""
Write-Host "Windows Service installation will be added here in Phase 1."
