<#
.SYNOPSIS
    Configures all required Azure Static Web App settings for daeanne-frontend.

.DESCRIPTION
    SWA app settings are not stored in source control (they contain secrets).
    Run this script after:
      - Creating a new SWA resource
      - Rotating credentials
      - Rebuilding the Azure environment from scratch

    Reads connection strings from the Bridge bin appsettings.json (gitignored,
    contains live secrets written by setup.ps1 / manual config).

.EXAMPLE
    .\Set-SwaSettings.ps1
    .\Set-SwaSettings.ps1 -DryRun  # Show what would be set without applying
#>
param(
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$SwaName      = "daeanne-frontend"
$SwaRg        = "daeanne-frontend-rg"
$StorageName  = "stdaeannefrontendapi"
$SubscriptionId = "7f59a77c-9d37-40f7-a53f-1a646c949efc"

Write-Host "Reading connection strings..."

# Storage (used by both SWA Functions runtime and result blobs)
$storageConn = az storage account show-connection-string `
    --name $StorageName `
    --resource-group $SwaRg `
    --query connectionString -o tsv

# Service Bus (used by CommandFunction to publish frontend requests)
$bridgeBinSettings = "$PSScriptRoot\..\src\Daeanne.Bridge\bin\Release\net8.0\appsettings.json"
if (-not (Test-Path $bridgeBinSettings)) {
    Write-Error "Bridge bin appsettings not found at $bridgeBinSettings — build Bridge first (dotnet build -c Release)"
}
$sbConn = (Get-Content $bridgeBinSettings -Raw | ConvertFrom-Json).ConnectionStrings.ServiceBus
if ([string]::IsNullOrWhiteSpace($sbConn)) {
    Write-Error "ServiceBus connection string is empty in Bridge appsettings.json"
}

# AAD app registration (for SWA auth)
# These are stable — only change if the app registration is recreated
# Store AAD_CLIENT_SECRET in ~/.daeanne/secrets/swa-aad-client-secret.txt
$aadClientId     = "1d44d53d-0e06-4b03-8677-a5772b2a5c22"
$secretFile      = "$env:USERPROFILE\.daeanne\secrets\swa-aad-client-secret.txt"
if (-not (Test-Path $secretFile)) {
    Write-Error "AAD client secret not found at $secretFile`nCreate it with:`n  mkdir -p ~/.daeanne/secrets && echo '<secret>' > $secretFile"
}
$aadClientSecret = (Get-Content $secretFile -Raw).Trim()

$settings = @{
    # Functions runtime — required for SWA managed Functions to start
    FUNCTIONS_WORKER_RUNTIME  = "dotnet-isolated"

    # Storage — required by Azure Functions host (internal state + our blob results)
    AzureWebJobsStorage       = $storageConn

    # Service Bus — used by CommandFunction to publish requests
    ServiceBusConnection      = $sbConn

    # AAD auth — SWA custom auth via Azure AD
    AAD_CLIENT_ID             = $aadClientId
    AAD_CLIENT_SECRET         = $aadClientSecret
    ALLOWED_USER_EMAIL        = "jeffrey.hubbard@outlook.com,jehubba@outlook.com"
    ALLOWED_USER_OID          = "c8afe179-74dd-44d2-99df-f78caa9a2850"
}

Write-Host ""
Write-Host "Settings to apply:"
$settings.Keys | Sort-Object | ForEach-Object {
    $val = $settings[$_]
    $display = if ($val.Length -gt 40) { "$($val.Substring(0,40))..." } else { $val }
    Write-Host "  $_ = $display"
}

if ($DryRun) {
    Write-Host ""
    Write-Host "[DryRun] No changes applied." -ForegroundColor Yellow
    exit 0
}

Write-Host ""
Write-Host "Applying settings to $SwaName..."

$body = @{
    properties = $settings
} | ConvertTo-Json -Depth 5

$tmpFile = [System.IO.Path]::GetTempFileName()
$body | Set-Content $tmpFile -Encoding utf8

az rest --method PUT `
    --url "https://management.azure.com/subscriptions/$SubscriptionId/resourceGroups/$SwaRg/providers/Microsoft.Web/staticSites/$SwaName/config/appsettings?api-version=2023-01-01" `
    --body "@$tmpFile" `
    --output none

Remove-Item $tmpFile

Write-Host ""
Write-Host "Done. Settings applied." -ForegroundColor Green
Write-Host "Trigger a redeploy to pick up the new settings:"
Write-Host "  cd ..; git commit --allow-empty -m 'ci: force redeploy'; git push"
