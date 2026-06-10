#Requires -Version 5.1
# Removes all Daeanne startup tasks from Windows Task Scheduler.
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$tasks = @("Daeanne.Dispatcher", "Daeanne.Bridge", "Daeanne.Tray")
Write-Host ""
foreach ($name in $tasks) {
    Unregister-ScheduledTask -TaskName $name -TaskPath "\Daeanne\" -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "  [removed] $name"
}
Write-Host "Done."
Write-Host ""