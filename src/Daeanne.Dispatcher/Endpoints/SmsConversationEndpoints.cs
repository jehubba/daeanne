using System.Text.Json;
using System.Threading.Channels;
using Daeanne.Dispatcher.Data;
using Daeanne.Shared.Models;
using Daeanne.Shared.Requests;
using Microsoft.EntityFrameworkCore;

namespace Daeanne.Dispatcher.Endpoints;

public static class SmsConversationEndpoints
{
    public static void MapSmsConversationEndpoints(this WebApplication app)
    {
        // Bridge calls this when an inbound SMS arrives from signal-cli
        app.MapPost("/sms/messages", LogInbound);

        // Returns recent conversation history for a phone number
        // Used by Bridge to include context in the task prompt
        app.MapGet("/sms/conversation", GetConversation);
    }

    /// <summary>
    /// Receives an inbound SMS from the Bridge (signal-cli → Bridge → here).
    /// 1. Resolves quoteTimestamp → taskId (if Jeffrey quoted a Daeanne message)
    /// 2. Logs the inbound message
    /// 3. Creates an InboundSms AgentTask with conversation history as context
    /// Returns the created AgentTask.
    /// </summary>
    private static async Task<IResult> LogInbound(
        LogInboundSmsRequest request,
        DispatcherDbContext db,
        Channel<Guid> queue,
        IConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(request.From) || string.IsNullOrWhiteSpace(request.Body))
            return Results.BadRequest("From and Body are required.");

        // Resolve quoted message → task context
        Guid? quotedTaskId = null;
        if (!string.IsNullOrWhiteSpace(request.QuoteTimestamp))
        {
            var quoted = await db.SmsMessages.FirstOrDefaultAsync(m =>
                m.Direction == SmsDirection.Outbound &&
                m.QuoteTimestamp == request.QuoteTimestamp);
            quotedTaskId = quoted?.TaskId;
        }

        // Fetch recent conversation history (last 10 messages)
        var history = await db.SmsMessages
            .Where(m => m.Phone == request.From)
            .OrderByDescending(m => m.Timestamp)
            .Take(10)
            .ToListAsync();
        history.Reverse(); // chronological order

        // Log the inbound message
        var inbound = new SmsMessage
        {
            Direction      = SmsDirection.Inbound,
            Phone          = request.From,
            Body           = request.Body,
            Timestamp      = DateTime.UtcNow,
            QuoteTimestamp = request.QuoteTimestamp,
            TaskId         = null  // filled in once task is created
        };
        db.SmsMessages.Add(inbound);

        // Build context JSON for the task
        var ctx = new Dictionary<string, object?>
        {
            ["senderPhone"]          = request.From,
            ["quoteTimestamp"]       = request.QuoteTimestamp,
            ["quotedTaskId"]         = quotedTaskId?.ToString(),
            ["conversationHistory"]  = history.Select(m => new
            {
                direction      = m.Direction.ToString().ToLower(),
                body           = m.Body,
                timestamp      = m.Timestamp.ToString("O"),
                taskId         = m.TaskId?.ToString(),
                referenceToken = m.ReferenceToken
            }).ToList()
        };

        var task = new AgentTask
        {
            Type          = AgentTaskType.InboundSms,
            Prompt        = request.Body,
            ContextJson   = JsonSerializer.Serialize(ctx),
            CorrelationId = null  // SMS tasks don't use correlationId dedup — each message is distinct
        };

        db.Tasks.Add(task);

        // Link inbound log entry to the task
        inbound.TaskId = task.Id;

        await db.SaveChangesAsync();
        await queue.Writer.WriteAsync(task.Id);

        return Results.Created($"/tasks/{task.Id}", task);
    }

    /// <summary>
    /// Returns recent SMS conversation history for a given phone number.
    /// Bridge uses this to build context when creating inbound tasks.
    /// </summary>
    private static async Task<IResult> GetConversation(
        DispatcherDbContext db, string phone, int take = 10)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return Results.BadRequest("phone query parameter is required.");

        var messages = await db.SmsMessages
            .Where(m => m.Phone == phone)
            .OrderByDescending(m => m.Timestamp)
            .Take(Math.Min(take, 50))
            .ToListAsync();

        messages.Reverse();
        return Results.Ok(messages);
    }
}
