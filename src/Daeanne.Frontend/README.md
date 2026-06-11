# daeanne-frontend

Blazor WebAssembly frontend for the Daeanne AI operating system, hosted on Azure Static Web Apps with an Azure Functions backend.

## Project Structure

```
daeanne-frontend/
├── Client/                  # Blazor WASM project (static frontend)
├── Server/                  # ASP.NET Core host (local dev only; SWA replaces this in production)
├── Shared/                  # Shared models between Client and Server
├── api/                     # Azure Functions isolated worker (backend API, served at /api/*)
├── staticwebapp.config.json # SWA routing, auth, and header config
└── .github/workflows/       # CI/CD — deploys to SWA on push to main
```

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
- [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) (local Azure Storage emulator) or Azure Storage connection string
- [Static Web Apps CLI](https://azure.github.io/static-web-apps-cli/) (`npm install -g @azure/static-web-apps-cli`)

## Local Development

### Option A — SWA CLI (recommended, closest to production)

The SWA CLI emulates the full SWA environment locally: routing, auth stub, and proxying to both the Blazor app and Azure Functions.

```bash
# Terminal 1: run the Blazor dev server
dotnet run --project Server

# Terminal 2: run the Azure Functions host
cd api
func start

# Terminal 3: start the SWA emulator (proxies both)
swa start http://localhost:5000 --api-devserver-url http://localhost:7071
```

Open `http://localhost:4280` in your browser.

### Option B — Server project only (no SWA routing emulation)

```bash
dotnet run --project Server
```

Open `https://localhost:5001`.

### Azure Functions — local config

Copy `api/local.settings.json.example` to `api/local.settings.json` and fill in any required values:

```bash
cp api/local.settings.json.example api/local.settings.json
```

`local.settings.json` is excluded from source control — never commit real secrets.

## Deployment

Deployment is automated via GitHub Actions on push to `main`. The workflow builds the Blazor WASM client, publishes it, and deploys both the static app and the Azure Functions API to Azure Static Web Apps.

### Required GitHub Secrets

| Secret | Description |
|--------|-------------|
| `AZURE_STATIC_WEB_APPS_API_TOKEN` | Deployment token from the SWA resource in the Azure portal |

Set it at: **Repo → Settings → Secrets and variables → Actions → New repository secret**

### App Settings (Azure Portal)

The following application settings must be configured in the SWA resource (not committed here):

| Setting | Description |
|---------|-------------|
| `AAD_CLIENT_ID` | Azure AD app registration client ID |
| `AAD_CLIENT_SECRET` | Azure AD app registration client secret |

Replace `__TENANT_ID__` in `staticwebapp.config.json` with your Azure AD tenant ID before deployment.

## Authentication

Authentication is wired to Azure Active Directory via SWA's built-in auth. The `staticwebapp.config.json` redirects 401s to `/.auth/login/aad`. No code changes are required for basic auth — unauthenticated users are redirected to AAD login automatically.

To require authentication on all routes, change the route rules in `staticwebapp.config.json`.

## Adding API Endpoints

Add new Azure Functions in `api/`:

```csharp
[Function("myEndpoint")]
public IActionResult Run(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "my-endpoint")] HttpRequest req)
{
    // ...
}
```

The SWA config proxies all `/api/*` requests to the Functions host automatically.
