#Requires -Version 5.1
# Starts all Daeanne services via Task Scheduler.
# Run register-startup.ps1 first if tasks are not yet registered.
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$tasks = @("Daeanne.Dispatcher", "Daeanne.Bridge", "Daeanne.Tray")
$path  = "\Daeanne\"

Write-Host ""
foreach ($name in $tasks) {
    $task = Get-ScheduledTask -TaskName $name -TaskPath $path -ErrorAction SilentlyContinue
    if ($null -eq $task) {
        Write-Host "  [skip]    $name (not registered -- run register-startup.ps1 first)"
        continue
    }
    if ($task.State -eq "Running") {
        Write-Host "  [running] $name (already up)"
        continue
    }
    Start-ScheduledTask -TaskName $name -TaskPath $path
    Write-Host "  [started] $name"
    Start-Sleep -Seconds 3
}
Write-Host ""