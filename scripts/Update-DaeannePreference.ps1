[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Communication", "WorkingStyle", "TopicContext", "ObservedPattern")]
    [string]$Category,

    [Parameter(Mandatory = $true)]
    [string]$Key,

    [Parameter(Mandatory = $true)]
    [string]$Value,

    [switch]$Inferred,
    [switch]$Remove
)

$ErrorActionPreference = "Stop"

function New-DefaultPreferences {
    @{
        version = 1
        lastUpdated = [DateTime]::UtcNow.ToString("o")
        communication = @{
            preferredLength = "executive-summary by default, detail on request"
            format = "markdown, bullet findings for research, prose for analysis"
            tone = "direct, no pleasantries"
        }
        workingStyle = @{
            decisionStyle = "prefers options with clear tradeoffs, not open-ended questions"
            confirmationPreference = "explicit confirm only for irreversible actions"
            escalationThreshold = "escalate on ambiguity that would waste >10 min if wrong"
        }
        topicContext = @{}
        observedPatterns = @()
    }
}

$preferencesPath = Join-Path $env:APPDATA "daeanne\preferences.json"
$preferencesDir = Split-Path -Parent $preferencesPath
New-Item -ItemType Directory -Path $preferencesDir -Force | Out-Null

if (-not (Test-Path $preferencesPath)) {
    $default = New-DefaultPreferences
    $default | ConvertTo-Json -Depth 10 | Set-Content -Path $preferencesPath
}

$raw = Get-Content -Raw -Path $preferencesPath
$prefs = $raw | ConvertFrom-Json -AsHashtable

if (-not $prefs.ContainsKey("topicContext")) { $prefs["topicContext"] = @{} }
if (-not $prefs.ContainsKey("observedPatterns")) { $prefs["observedPatterns"] = @() }
if (-not $prefs.ContainsKey("communication")) { $prefs["communication"] = @{} }
if (-not $prefs.ContainsKey("workingStyle")) { $prefs["workingStyle"] = @{} }

$changed = $false

switch ($Category) {
    "Communication" {
        if ($Inferred) {
            $Category = "ObservedPattern"
        } else {
            if ($Remove) {
                $changed = $prefs.communication.Remove($Key)
            } else {
                $prefs.communication[$Key] = $Value
                $changed = $true
            }
        }
    }
    "WorkingStyle" {
        if ($Inferred) {
            $Category = "ObservedPattern"
        } else {
            if ($Remove) {
                $changed = $prefs.workingStyle.Remove($Key)
            } else {
                $prefs.workingStyle[$Key] = $Value
                $changed = $true
            }
        }
    }
    "TopicContext" {
        if ($Remove) {
            $changed = $prefs.topicContext.Remove($Key)
        } else {
            $prefs.topicContext[$Key] = @{
                value = $Value
                inferred = [bool]$Inferred
                lastUpdated = [DateTime]::UtcNow.ToString("o")
            }
            $changed = $true
        }
    }
}

if ($Category -eq "ObservedPattern") {
    $patterns = @($prefs.observedPatterns)
    $existing = $patterns | Where-Object {
        $_.category -eq "topicContext" -and $_.key -eq $Key -and $_.value -eq $Value
    } | Select-Object -First 1

    if ($null -eq $existing) {
        $patterns += @{
            category = "topicContext"
            key = $Key
            value = $Value
            inferred = $true
            occurrences = 1
            firstObserved = [DateTime]::UtcNow.ToString("o")
            lastObserved = [DateTime]::UtcNow.ToString("o")
        }
    } else {
        $existing.occurrences = [int]$existing.occurrences + 1
        $existing.lastObserved = [DateTime]::UtcNow.ToString("o")
    }

    $prefs.observedPatterns = @($patterns | Sort-Object -Property lastObserved -Descending | Select-Object -First 50)
    $changed = $true
}

if (-not $changed) {
    Write-Host "No changes applied."
    exit 0
}

$prefs.version = 1
$prefs.lastUpdated = [DateTime]::UtcNow.ToString("o")

if ($PSCmdlet.ShouldProcess($preferencesPath, "Update Daeanne preference")) {
    $prefs | ConvertTo-Json -Depth 10 | Set-Content -Path $preferencesPath
    Write-Host "Updated preferences at $preferencesPath"
}
