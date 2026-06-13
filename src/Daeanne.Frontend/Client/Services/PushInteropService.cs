using Microsoft.JSInterop;

namespace DaeanneFrontend.Client.Services;

/// <summary>
/// Wraps the browser-side push-interop.js helpers so Blazor components can call
/// Web Push subscription, Badge API, and Web Share via C#.
/// </summary>
public class PushInteropService
{
    private readonly IJSRuntime _js;

    public PushInteropService(IJSRuntime js)
    {
        _js = js;
    }

    /// <summary>
    /// Subscribes the browser to Web Push using VAPID and sends the subscription to
    /// POST /api/subscribe.  Returns true on success.
    /// </summary>
    public async ValueTask<bool> SubscribeToPushAsync(string vapidPublicKey)
    {
        try
        {
            return await _js.InvokeAsync<bool>("daeannePush.subscribeToPush", vapidPublicKey);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Sets the app icon badge to <paramref name="count"/>.</summary>
    public async ValueTask SetBadgeAsync(int count)
    {
        try
        {
            await _js.InvokeVoidAsync("daeannePush.setBadge", count);
        }
        catch { /* graceful degradation */ }
    }

    /// <summary>Clears the app icon badge.</summary>
    public async ValueTask ClearBadgeAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("daeannePush.clearBadge");
        }
        catch { /* graceful degradation */ }
    }

    /// <summary>
    /// Opens the native OS share sheet via the Web Share API.
    /// Returns false if the API is unavailable or the user cancels.
    /// </summary>
    public async ValueTask<bool> ShareTaskAsync(string title, string text, string url)
    {
        try
        {
            return await _js.InvokeAsync<bool>("daeannePush.shareTask", title, text, url);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Returns true if the Web Share API is available in this browser.</summary>
    public async ValueTask<bool> IsShareSupportedAsync()
    {
        try
        {
            return await _js.InvokeAsync<bool>("daeannePush.isShareSupported");
        }
        catch
        {
            return false;
        }
    }
}
