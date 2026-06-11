using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Daeanne.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace DaeanneFrontend.Api;

public class CommandFunction
{
    private readonly ServiceBusClient? _sbClient;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CommandFunction(ServiceBusClient? sbClient = null)
    {
        _sbClient = sbClient;
    }

    [Function("command")]
    public async Task<IActionResult> PostCommand(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "command")] HttpRequest req)
    {
        var body = await JsonSerializer.DeserializeAsync<CommandRequestBody>(req.Body, JsonOpts);
        if (body is null || string.IsNullOrWhiteSpace(body.Prompt))
            return new BadRequestObjectResult(new { error = "Prompt is required" });

        if (body.Prompt.Length > 4000)
            return new BadRequestObjectResult(new { error = "Prompt must be 4000 characters or fewer" });

        var correlationId = $"fe-cmd-{Guid.NewGuid()}";
        var taskType = string.IsNullOrWhiteSpace(body.TaskType) ? "Generic" : body.TaskType;

        if (_sbClient is null)
            return new StatusCodeResult(503);

        var frontendRequest = new FrontendRequest(body.Prompt, correlationId, taskType);
        var message = new ServiceBusMessage(JsonSerializer.Serialize(frontendRequest, JsonOpts));

        await using var sender = _sbClient.CreateSender("daeanne-frontend-requests");
        await sender.SendMessageAsync(message);

        return new ObjectResult(new { correlationId, message = "Command submitted" })
        {
            StatusCode = 202
        };
    }

    private record CommandRequestBody(string? Prompt, string? TaskType);
}
