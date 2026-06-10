#Requires -Version 5.1
param(
    [string]$RepoRoot      = (Resolve-Path (Join-Path $PSScriptRoot "..")),
    [string]$Configuration = "Release"
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$dispBin   = Join-Path $RepoRoot "src\Daeanne.Dispatcher\bin\$Configuration\net8.0"
$bridgeBin = Join-Path $RepoRoot "src\Daeanne.Bridge\bin\$Configuration\net8.0"
$trayBin   = Join-Path $RepoRoot "src\Daeanne.Tray\bin\$Configuration\net8.0-windows"
$dispExe   = Join-Path $dispBin   "Daeanne.Dispatcher.exe"
$bridgeExe = Join-Path $bridgeBin "Daeanne.Bridge.exe"
$trayExe   = Join-Path $trayBin   "Daeanne.Tray.exe"

foreach ($exe in @($dispExe, $bridgeExe, $trayExe)) {
    if (-not (Test-Path $exe)) {
        Write-Error "Binary not found: $exe -- run 'dotnet build -c $Configuration' first."
    }
}

function Register-DaeanneTask {
    param([string]$TaskName, [string]$Exe, [string]$WorkDir, [int]$DelaySeconds = 0, [string]$Description)
    Unregister-ScheduledTask -TaskName $TaskName -TaskPath "\Daeanne\" -Confirm:$false -ErrorAction SilentlyContinue
    $action    = New-ScheduledTaskAction -Execute $Exe -WorkingDirectory $WorkDir
    $trigger   = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
    if ($DelaySeconds -gt 0) { $trigger.Delay = "PT${DelaySeconds}S" }
    $settings  = New-ScheduledTaskSettingsSet -ExecutionTimeLimit 0 -MultipleInstances IgnoreNew -StartWhenAvailable
    $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
    Register-ScheduledTask -TaskName $TaskName -TaskPath "\Daeanne\" -Action $action -Trigger $trigger -Settings $settings -Principal $principal -Description $Description | Out-Null
    Write-Host "  [OK] $TaskName (delay: ${DelaySeconds}s)"
}

Write-Host ""
Write-Host "Registering Daeanne startup tasks..."
Write-Host "  Repo  : $RepoRoot"
Write-Host "  Config: $Configuration"
Write-Host ""

Register-DaeanneTask -TaskName "Daeanne.Dispatcher" -Exe $dispExe   -WorkDir $dispBin   -DelaySeconds 0 -Description "Daeanne task dispatcher"
Register-DaeanneTask -TaskName "Daeanne.Bridge"      -Exe $bridgeExe -WorkDir $bridgeBin -DelaySeconds 3 -Description "Daeanne email/SMS bridge"
Register-DaeanneTask -TaskName "Daeanne.Tray"        -Exe $trayExe   -WorkDir $trayBin   -DelaySeconds 8 -Description "Daeanne system tray"

Write-Host ""
Write-Host "Done. Tasks registered under \Daeanne\ in Task Scheduler."
Write-Host "They start automatically at next logon."
Write-Host "To start them now: .\start-services.ps1"
Write-Host ""