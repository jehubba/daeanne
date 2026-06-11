using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace DaeanneFrontend.Api;

public class TasksFunction
{
    private readonly HttpClient _httpClient;

    public TasksFunction(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    [Function("tasks")]
    public async Task<IActionResult> GetTasks(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "tasks")] HttpRequest req)
    {
        var skip = int.TryParse(req.Query["skip"], out var s) ? s : 0;
        var take = int.TryParse(req.Query["take"], out var t) ? Math.Min(t, 200) : 50;
        var status = req.Query["status"].FirstOrDefault();
        var type = req.Query["type"].FirstOrDefault();

        var query = $"relay/tasks?skip={skip}&take={take}";
        if (!string.IsNullOrEmpty(status)) query += $"&status={Uri.EscapeDataString(status)}";
        if (!string.IsNullOrEmpty(type)) query += $"&type={Uri.EscapeDataString(type)}";

        try
        {
            var response = await _httpClient.GetAsync(query);
            if (!response.IsSuccessStatusCode)
                return new StatusCodeResult(502);

            var content = await response.Content.ReadAsStringAsync();
            return new ContentResult
            {
                Content = content,
                ContentType = "application/json",
                StatusCode = 200
            };
        }
        catch (HttpRequestException)
        {
            return new StatusCodeResult(502);
        }
    }

    [Function("taskDetail")]
    public async Task<IActionResult> GetTask(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "tasks/{id:int}")] HttpRequest req,
        int id)
    {
        try
        {
            var response = await _httpClient.GetAsync($"relay/tasks/{id}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new NotFoundResult();
            if (!response.IsSuccessStatusCode)
                return new StatusCodeResult(502);

            var content = await response.Content.ReadAsStringAsync();
            return new ContentResult
            {
                Content = content,
                ContentType = "application/json",
                StatusCode = 200
            };
        }
        catch (HttpRequestException)
        {
            return new StatusCodeResult(502);
        }
    }
}
