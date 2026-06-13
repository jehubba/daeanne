<#
.SYNOPSIS
    Configures all required Azure settings for the daeanne-frontend SWA + Function App.

.DESCRIPTION
    SWA and Function App settings are not stored in source control (they contain secrets).
    Run this script after:
      - Creating a new SWA or Function App resource
      - Rotating credentials
      - Rebuilding the Azure environment from scratch

    ALSO handles: removing WEBSITE_RUN_FROM_PACKAGE (URL form) from the Function App,
    which conflicts with the GitHub Actions zip-deploy approach.

    GITHUB SECRETS to set/refresh manually:
      - SWA_DEPLOYMENT_TOKEN   : az staticwebapp secrets list --name daeanne-frontend --resource-group daeanne-frontend-rg
      - FUNC_PUBLISH_PROFILE   : az functionapp deployment list-publishing-profiles --name daeanne-frontend-api --resource-group daeanne-frontend-rg --xml

    Reads connection strings from the Bridge bin appsettings.json (gitignored).

.EXAMPLE
    .\Set-SwaSettings.ps1
    .\Set-SwaSettings.ps1 -DryRun  # Show what would be set without applying
#>
param(
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

$SwaName        = "daeanne-frontend"
$FuncAppName    = "daeanne-frontend-api"
$Rg             = "daeanne-frontend-rg"
$StorageName    = "stdaeannefrontendapi"
$SubscriptionId = "7f59a77c-9d37-40f7-a53f-1a646c949efc"

Write-Host "Reading connection strings..."

$storageConn = az storage account show-connection-string `
    --name $StorageName --resource-group $Rg --query connectionString -o tsv

$bridgeBinSettings = "$PSScriptRoot\..\src\Daeanne.Bridge\bin\Release\net8.0\appsettings.json"
if (-not (Test-Path $bridgeBinSettings)) {
    Write-Error "Bridge bin appsettings not found at $bridgeBinSettings — build Bridge first (dotnet build -c Release)"
}
$sbConn = (Get-Content $bridgeBinSettings -Raw | ConvertFrom-Json).ConnectionStrings.ServiceBus
if ([string]::IsNullOrWhiteSpace($sbConn)) {
    Write-Error "ServiceBus connection string is empty in Bridge appsettings.json"
}

$secretFile = "$env:USERPROFILE\.daeanne\secrets\swa-aad-client-secret.txt"
if (-not (Test-Path $secretFile)) {
    Write-Error "AAD client secret not found at $secretFile`nCreate it with:`n  New-Item -Force (Split-Path $secretFile); '<secret>' | Set-Content $secretFile"
}
$aadClientSecret = (Get-Content $secretFile -Raw).Trim()
$aadClientId     = "1d44d53d-0e06-4b03-8677-a5772b2a5c22"

# ── SWA app settings ────────────────────────────────────────────────────────
$swaSettings = @{
    FUNCTIONS_WORKER_RUNTIME  = "dotnet-isolated"
    AzureWebJobsStorage       = $storageConn
    ServiceBusConnection      = $sbConn
    AAD_CLIENT_ID             = $aadClientId
    AAD_CLIENT_SECRET         = $aadClientSecret
    ALLOWED_USER_EMAIL        = "jeffrey.hubbard@outlook.com,jehubba@outlook.com"
    ALLOWED_USER_OID          = "c8afe179-74dd-44d2-99df-f78caa9a2850"
}

# ── Function App settings ───────────────────────────────────────────────────
# WEBSITE_RUN_FROM_PACKAGE must NOT be a URL — zip-deploy approach is used.
# If it gets set to a URL again (e.g. by Azure portal or Terraform), remove it.
$funcSettings = @{
    FUNCTIONS_WORKER_RUNTIME  = "dotnet-isolated"
    AzureWebJobsStorage       = $storageConn
    ServiceBusConnection      = $sbConn
    BRIDGE_BASE_URL           = "https://daeanne-bridge.your-ngrok-or-tunnel.url"  # update if Bridge is cloud-hosted
}

Write-Host "`nSWA settings:"
$swaSettings.Keys | Sort-Object | ForEach-Object {
    $v = $swaSettings[$_]
    Write-Host "  $_ = $(if ($v.Length -gt 50) { $v.Substring(0,50)+'...' } else { $v })"
}

Write-Host "`nFunction App settings:"
$funcSettings.Keys | Sort-Object | ForEach-Object {
    $v = $funcSettings[$_]
    Write-Host "  $_ = $(if ($v.Length -gt 50) { $v.Substring(0,50)+'...' } else { $v })"
}

if ($DryRun) {
    Write-Host "`n[DryRun] No changes applied." -ForegroundColor Yellow
    exit 0
}

Write-Host "`nApplying SWA settings..."
$body = @{ properties = $swaSettings } | ConvertTo-Json -Depth 5
$tmpFile = [System.IO.Path]::GetTempFileName()
$body | Set-Content $tmpFile -Encoding utf8
az rest --method PUT `
    --url "https://management.azure.com/subscriptions/$SubscriptionId/resourceGroups/$Rg/providers/Microsoft.Web/staticSites/$SwaName/config/appsettings?api-version=2023-01-01" `
    --body "@$tmpFile" --output none
Remove-Item $tmpFile

Write-Host "Applying Function App settings..."
# Remove WEBSITE_RUN_FROM_PACKAGE if it exists (conflicts with zip-deploy)
$existing = az functionapp config appsettings list --name $FuncAppName --resource-group $Rg -o json | ConvertFrom-Json
if ($existing | Where-Object { $_.name -eq "WEBSITE_RUN_FROM_PACKAGE" }) {
    Write-Host "  Removing WEBSITE_RUN_FROM_PACKAGE..."
    az functionapp config appsettings delete --name $FuncAppName --resource-group $Rg `
        --setting-names WEBSITE_RUN_FROM_PACKAGE --output none
}
$setArgs = ($funcSettings.Keys | ForEach-Object { "$_=$($funcSettings[$_])" }) -join " "
az functionapp config appsettings set --name $FuncAppName --resource-group $Rg `
    --settings @funcSettings.Keys.ForEach({ "$_=$($funcSettings[$_])" }) --output none 2>&1 | Out-Null

Write-Host "`nDone." -ForegroundColor Green
Write-Host "`nReminder: refresh GitHub secrets if credentials were rotated:"
Write-Host "  SWA token:      az staticwebapp secrets list --name $SwaName --resource-group $Rg"
Write-Host "  Publish profile: az functionapp deployment list-publishing-profiles --name $FuncAppName --resource-group $Rg --xml"
