#Requires -Version 5.1
# Restores user secrets into the Bridge bin appsettings.json after a dotnet build.
# Run after: dotnet build src/Daeanne.Bridge -c Release
#
# Why: dotnet build copies appsettings.json from source to bin, overwriting the live file
# that has secrets baked in. This script merges user secrets back.
param(
    [string]$RepoRoot      = (Resolve-Path (Join-Path $PSScriptRoot "..")),
    [string]$Configuration = "Release"
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$binPath = Join-Path $RepoRoot "src\Daeanne.Bridge\bin\$Configuration\net8.0\appsettings.json"
$srcPath = Join-Path $RepoRoot "src\Daeanne.Bridge\appsettings.json"

if (-not (Test-Path $binPath)) {
    Write-Error "Bin appsettings not found at $binPath — did you build first?"
}

$binJson = Get-Content $binPath -Raw | ConvertFrom-Json
$srcJson = Get-Content $srcPath -Raw | ConvertFrom-Json

# Merge user secrets (dotnet user-secrets list outputs "Key = Value" lines)
$rawSecrets = & dotnet user-secrets list --project (Join-Path $RepoRoot "src\Daeanne.Bridge") 2>&1
foreach ($line in $rawSecrets) {
    if ($line -notmatch "^(.+?)\s*=\s*(.+)$") { continue }
    $key = $Matches[1].Trim()
    $val = $Matches[2].Trim()
    $parts = $key -split ":"

    if ($parts.Count -eq 2) {
        $section = $parts[0]; $prop = $parts[1]
        if (-not $binJson.$section) {
            $binJson | Add-Member -NotePropertyName $section -NotePropertyValue ([PSCustomObject]@{}) -Force
        }
        $binJson.$section | Add-Member -NotePropertyName $prop -NotePropertyValue $val -Force
    }
    elseif ($parts.Count -eq 3) {
        $section = $parts[0]; $sub = $parts[1]; $prop = $parts[2]
        if (-not $binJson.$section) {
            $binJson | Add-Member -NotePropertyName $section -NotePropertyValue ([PSCustomObject]@{}) -Force
        }
        if (-not $binJson.$section.$sub) {
            $binJson.$section | Add-Member -NotePropertyName $sub -NotePropertyValue ([PSCustomObject]@{}) -Force
        }
        $binJson.$section.$sub | Add-Member -NotePropertyName $prop -NotePropertyValue $val -Force
    }
}

# Also restore ConnectionStrings:FrontendStorage from source (it's in source appsettings, not user-secrets)
$frontendStorage = $srcJson.ConnectionStrings.FrontendStorage
if ($frontendStorage) {
    if (-not $binJson.ConnectionStrings) {
        $binJson | Add-Member -NotePropertyName "ConnectionStrings" -NotePropertyValue ([PSCustomObject]@{}) -Force
    }
    $binJson.ConnectionStrings | Add-Member -NotePropertyName "FrontendStorage" -NotePropertyValue $frontendStorage -Force
}

$binJson | ConvertTo-Json -Depth 10 | Set-Content $binPath -Encoding UTF8

Write-Host "Bridge bin appsettings restored."
Write-Host "  Graph.ClientId:         $('*' * [Math]::Min($binJson.Graph.ClientId.Length, 8))..."
Write-Host "  Graph.RefreshToken set: $($binJson.Graph.RefreshToken.Length -gt 0)"
Write-Host "  ConnectionStrings.ServiceBus set: $($binJson.ConnectionStrings.ServiceBus.Length -gt 0)"
