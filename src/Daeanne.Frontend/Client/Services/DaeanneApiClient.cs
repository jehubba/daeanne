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
        var response = await _http.GetFromJsonAsync<TaskListResponse>($"api/tasks?skip={skip}&take={take}", ct);
        return (response?.Tasks ?? [], response?.Total ?? 0);
    }

    public async Task<TaskDto?> GetTaskAsync(int id, CancellationToken ct = default)
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

    private record TaskListResponse(List<TaskDto> Tasks, int Total);
}
