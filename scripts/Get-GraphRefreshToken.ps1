# Get-GraphRefreshToken.ps1
#
# Device-code flow to obtain a new Graph refresh token with the full scope
# required by Daeanne Bridge (Mail + Calendar).
#
# Run this when the stored token needs to be reissued — e.g. after adding
# a new Graph permission scope:
#
#   cd C:\Users\Jeffrey\daeanne
#   .\scripts\Get-GraphRefreshToken.ps1
#
# The script will print a URL and a code. Visit the URL, enter the code,
# sign in as the Daeanne account (daeanne-srs@outlook.com), and consent.
# The script polls for completion and writes the new refresh token into
# user-secrets for Daeanne.Bridge.

$ErrorActionPreference = "Stop"

$repoRoot  = Split-Path -Parent $PSScriptRoot
$bridgeProj = Join-Path $repoRoot "src\Daeanne.Bridge"

# ── Read ClientId from user-secrets ─────────────────────────────────────────
$secrets = dotnet user-secrets list --project $bridgeProj 2>&1
$clientIdLine = $secrets | Select-String "Graph:ClientId"
if (-not $clientIdLine) {
    Write-Error "Graph:ClientId not found in user-secrets for Daeanne.Bridge."
}
$clientId = ($clientIdLine -split " = ", 2)[1].Trim()
Write-Host "Using ClientId: $clientId"

# ── Scope ────────────────────────────────────────────────────────────────────
$scope = "Mail.Read Mail.ReadWrite Mail.Send Calendars.ReadWrite offline_access"

# ── Step 1: Request device code ──────────────────────────────────────────────
$deviceResp = Invoke-RestMethod `
    -Uri "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode" `
    -Method Post `
    -Body @{ client_id = $clientId; scope = $scope } `
    -ContentType "application/x-www-form-urlencoded"

Write-Host ""
Write-Host "═══════════════════════════════════════════════════"
Write-Host " Go to: $($deviceResp.verification_uri)"
Write-Host " Enter code: $($deviceResp.user_code)"
Write-Host "═══════════════════════════════════════════════════"
Write-Host ""
Write-Host "Sign in as the Daeanne account and consent to the requested permissions."
Write-Host "Polling for completion..."

# ── Step 2: Poll until authorised ────────────────────────────────────────────
$interval = [int]$deviceResp.interval
$expires  = [int]$deviceResp.expires_in
$elapsed  = 0

$token = $null
while ($elapsed -lt $expires) {
    Start-Sleep $interval
    $elapsed += $interval

    try {
        $token = Invoke-RestMethod `
            -Uri "https://login.microsoftonline.com/consumers/oauth2/v2.0/token" `
            -Method Post `
            -Body @{
                grant_type  = "urn:ietf:params:oauth:grant-type:device_code"
                client_id   = $clientId
                device_code = $deviceResp.device_code
            } `
            -ContentType "application/x-www-form-urlencoded" `
            -ErrorAction Stop

        if ($token.refresh_token) { break }
    } catch {
        $errBody = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
        if ($errBody.error -eq "authorization_pending") {
            Write-Host -NoNewline "."
            continue
        }
        if ($errBody.error -eq "authorization_declined") {
            Write-Error "Authorization was declined by the user."
        }
        if ($errBody.error -eq "expired_token") {
            Write-Error "Device code expired. Re-run the script."
        }
        # Unexpected error — rethrow
        throw
    }
}

if (-not $token -or -not $token.refresh_token) {
    Write-Error "Did not receive a refresh token within the timeout window."
}

Write-Host ""
Write-Host "Authorization succeeded."

# ── Step 3: Store in user-secrets ────────────────────────────────────────────
dotnet user-secrets set "Graph:RefreshToken" $token.refresh_token --project $bridgeProj | Out-Null

# Also delete the cached token file so Bridge reloads from user-secrets on next start
$tokenFile = "$env:APPDATA\daeanne\graph-token.json"
if (Test-Path $tokenFile) {
    Remove-Item $tokenFile -Force
    Write-Host "Cleared cached token at $tokenFile"
}

Write-Host ""
Write-Host "✓ Refresh token saved to user-secrets (Graph:RefreshToken)."
Write-Host "Restart Bridge to pick up the new token:"
Write-Host "  Stop-Process -Id (Get-Process Daeanne.Bridge).Id"
Write-Host "  Start-Process 'src\Daeanne.Bridge\bin\Release\net8.0\Daeanne.Bridge.exe'"
