using System.Net.Http.Json;
using DaeanneFrontend.Shared;

namespace DaeanneFrontend.Client.Services;

public class DaeanneApiClient
{
    private readonly HttpClient _http;

    public DaeanneApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<(List<TaskDto> Tasks, int Total)> GetTasksAsync(int skip = 0, int take = 50, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"api/tasks?skip={skip}&take={take}", ct);
        if (!response.IsSuccessStatusCode) return ([], 0);
        var result = await response.Content.ReadFromJsonAsync<TaskListResponse>(cancellationToken: ct);
        return (result?.Tasks ?? [], result?.Total ?? 0);
    }

    public async Task<TaskDto?> GetTaskAsync(string id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"api/tasks/{id}", ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<TaskDto>(cancellationToken: ct);
    }

    public async Task<CommandResultDto?> SendCommandAsync(CommandRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("api/command", request, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<CommandResultDto>(cancellationToken: ct);
    }

    public async Task<CommandResultDto?> PollResultAsync(string correlationId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"api/result/{correlationId}", ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<CommandResultDto>(cancellationToken: ct);
    }

    public async Task<TrendHighlightDto?> GetTrendsAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("api/trends/today", ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<TrendHighlightDto>(cancellationToken: ct);
    }

    /// <summary>Returns the VAPID public key from the server, or null if not configured.</summary>
    public async Task<string?> GetVapidPublicKeyAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("api/push/vapid-public-key", ct);
            if (!response.IsSuccessStatusCode) return null;
            var result = await response.Content.ReadFromJsonAsync<VapidPublicKeyResponse>(cancellationToken: ct);
            return result?.PublicKey;
        }
        catch
        {
            return null;
        }
    }

    private record VapidPublicKeyResponse(string PublicKey);
    private record TaskListResponse(List<TaskDto> Tasks, int Total);
}
