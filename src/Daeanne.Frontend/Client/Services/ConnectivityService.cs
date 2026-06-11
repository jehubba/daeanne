using Microsoft.JSInterop;

namespace DaeanneFrontend.Client.Services;

public class ConnectivityService : IDisposable
{
    private readonly IJSRuntime _js;
    private DotNetObjectReference<ConnectivityService>? _selfRef;

    public bool IsOnline { get; private set; } = true;

    public event Action? OnStatusChanged;

    public ConnectivityService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task InitializeAsync()
    {
        _selfRef = DotNetObjectReference.Create(this);
        try
        {
            IsOnline = await _js.InvokeAsync<bool>("navigator.onLine");
            await _js.InvokeVoidAsync("eval", $@"
                window.__connectivityRef = DotNet.createJSObjectReference({{}});
                window.addEventListener('online', () => DotNet.invokeMethodAsync('DaeanneFrontend.Client', 'OnConnectivityChanged', true));
                window.addEventListener('offline', () => DotNet.invokeMethodAsync('DaeanneFrontend.Client', 'OnConnectivityChanged', false));
            ");
        }
        catch
        {
            // JS interop may not be available during prerendering
        }
    }

    [JSInvokable("OnConnectivityChanged")]
    public static void OnConnectivityChangedStatic(bool isOnline)
    {
        // Static callback — instance notification handled via event
    }

    public void UpdateStatus(bool isOnline)
    {
        if (IsOnline == isOnline) return;
        IsOnline = isOnline;
        OnStatusChanged?.Invoke();
    }

    public void Dispose()
    {
        _selfRef?.Dispose();
    }
}
