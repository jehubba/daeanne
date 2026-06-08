using Daeanne.Dispatcher.Data;
using Daeanne.Shared.Models;
using Daeanne.Shared.Requests;
using Microsoft.EntityFrameworkCore;

namespace Daeanne.Dispatcher.Endpoints;

public static class OutboxSmsEndpoints
{
    public static void MapOutboxSmsEndpoints(this WebApplication app)
    {
        app.MapPost("/outbox/sms",                  QueueSms);
        app.MapGet("/outbox/sms/{id:guid}",          GetSms);
        app.MapGet("/outbox/sms",                    ListSms);
        app.MapPatch("/outbox/sms/{id:guid}/status", PatchStatus);
    }

    private static async Task<IResult> QueueSms(
        QueueSmsRequest request, DispatcherDbContext db)
    {
        if (string.IsNullOrWhiteSpace(request.To))
            return Results.BadRequest("To is required.");
        if (string.IsNullOrWhiteSpace(request.Body))
            return Results.BadRequest("Body is required.");
        if (request.Body.Length > 1600)
            return Results.BadRequest("Body exceeds 1600 characters (10 SMS segments).");

        // Derive reference token if a task is linked and body doesn't already contain one
        string? refToken = null;
        string body      = request.Body;
        if (request.TaskId.HasValue)
        {
            refToken = SmsMessage.TokenFromTaskId(request.TaskId.Value);
            if (!body.Contains($"[{refToken}]"))
                body = $"{body.TrimEnd()} [{refToken}]";
        }

        var sms = new OutboxSms
        {
            To            = request.To,
            Body          = body,
            CorrelationId = request.CorrelationId
        };

        db.OutboxSmsList.Add(sms);

        // Log outbound to unified conversation history
        db.SmsMessages.Add(new SmsMessage
        {
            Direction      = SmsDirection.Outbound,
            Phone          = request.To,
            Body           = body,
            Timestamp      = DateTime.UtcNow,
            TaskId         = request.TaskId,
            ReferenceToken = refToken,
            OutboxSmsId    = sms.Id
        });

        await db.SaveChangesAsync();
        return Results.Created($"/outbox/sms/{sms.Id}", sms);
    }

    private static async Task<IResult> GetSms(Guid id, DispatcherDbContext db)
    {
        var sms = await db.OutboxSmsList.FindAsync(id);
        return sms is null ? Results.NotFound() : Results.Ok(sms);
    }

    private static async Task<IResult> ListSms(
        DispatcherDbContext db, string? status = null, int take = 50)
    {
        var q = db.OutboxSmsList.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<OutboxSmsStatus>(status, ignoreCase: true, out var parsed))
            q = q.Where(s => s.Status == parsed);

        var items = await q.OrderByDescending(s => s.CreatedAt).Take(Math.Min(take, 200)).ToListAsync();
        return Results.Ok(items);
    }

    private static async Task<IResult> PatchStatus(
        Guid id, PatchSmsStatusRequest request, DispatcherDbContext db)
    {
        var sms = await db.OutboxSmsList.FindAsync(id);
        if (sms is null) return Results.NotFound();

        if (Enum.TryParse<OutboxSmsStatus>(request.Status, ignoreCase: true, out var newStatus))
            sms.Status = newStatus;

        sms.Error = request.Error;
        if (newStatus == OutboxSmsStatus.Sent)
            sms.SentAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Results.Ok(sms);
    }
}
