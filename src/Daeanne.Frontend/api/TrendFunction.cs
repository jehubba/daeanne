using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace DaeanneFrontend.Api;

public class TrendFunction
{
    private readonly HttpClient _httpClient;

    public TrendFunction(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    [Function("trends")]
    public async Task<IActionResult> GetTrends(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "trends/today")] HttpRequest req)
    {
        try
        {
            var response = await _httpClient.GetAsync("relay/trends/today");
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
