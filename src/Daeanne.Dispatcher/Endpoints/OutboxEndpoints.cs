using Daeanne.Dispatcher.Data;
using Daeanne.Shared.Models;
using Daeanne.Shared.Requests;
using Microsoft.EntityFrameworkCore;

namespace Daeanne.Dispatcher.Endpoints;

public static class OutboxEndpoints
{
    public static void MapOutboxEndpoints(this WebApplication app)
    {
        app.MapGet("/outbox/email", GetOutbox);
        app.MapGet("/outbox/email/{id:guid}", GetEmail);
        app.MapPost("/outbox/email", QueueEmail);
        app.MapPatch("/outbox/email/{id:guid}/status", UpdateEmailStatus);
        app.MapPost("/outbox/email/{id:guid}/retry", RetryEmail);
    }

    private static async Task<IResult> GetOutbox(
        DispatcherDbContext db,
        string? status,
        int take = 50,
        int skip = 0)
    {
        var query = db.OutboxEmails.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<OutboxEmailStatus>(status, ignoreCase: true, out var parsed))
        {
            query = query.Where(e => e.Status == parsed);
        }

        var emails = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip(skip)
            .Take(Math.Min(take, 200))
            .ToListAsync();

        return Results.Ok(emails);
    }

    private static async Task<IResult> GetEmail(Guid id, DispatcherDbContext db)
    {
        var email = await db.OutboxEmails.FindAsync(id);
        return email is null ? Results.NotFound() : Results.Ok(email);
    }

    private static async Task<IResult> QueueEmail(
        OutboxEmailRequest request,
        DispatcherDbContext db)
    {
        if (string.IsNullOrWhiteSpace(request.To))
            return Results.BadRequest("'to' is required.");
        if (string.IsNullOrWhiteSpace(request.Subject))
            return Results.BadRequest("'subject' is required.");

        var email = new OutboxEmail
        {
            To = request.To,
            Subject = request.Subject,
            Body = request.Body,
            CorrelationId = request.CorrelationId,
            ReplyToGraphMessageId = request.ReplyToGraphMessageId
        };

        db.OutboxEmails.Add(email);
        await db.SaveChangesAsync();

        return Results.Created($"/outbox/email/{email.Id}", email);
    }

    private static async Task<IResult> UpdateEmailStatus(
        Guid id,
        UpdateOutboxStatusRequest request,
        DispatcherDbContext db)
    {
        var email = await db.OutboxEmails.FindAsync(id);
        if (email is null) return Results.NotFound();

        if (!Enum.TryParse<OutboxEmailStatus>(request.Status, ignoreCase: true, out var newStatus))
            return Results.BadRequest($"Unknown status '{request.Status}'.");

        // Prevent rolling back from terminal states
        if (email.Status is OutboxEmailStatus.Sent or OutboxEmailStatus.Failed)
            return Results.Conflict($"Email {id} is already in terminal state '{email.Status}'.");

        email.Status = newStatus;
        if (newStatus == OutboxEmailStatus.Sent)
            email.SentAt = DateTime.UtcNow;
        if (request.Error is not null)
            email.Error = request.Error;

        await db.SaveChangesAsync();
        return Results.Ok(email);
    }

    /// <summary>
    /// Resets a Failed email back to Pending so the Bridge will retry it.
    /// No-op if the email is already Sent or in a non-terminal state.
    /// </summary>
    private static async Task<IResult> RetryEmail(Guid id, DispatcherDbContext db)
    {
        var email = await db.OutboxEmails.FindAsync(id);
        if (email is null) return Results.NotFound();

        if (email.Status == OutboxEmailStatus.Sent)
            return Results.Conflict($"Email {id} is already Sent — no retry needed.");

        email.Status = OutboxEmailStatus.Pending;
        email.Error  = null;
        await db.SaveChangesAsync();

        return Results.Ok(email);
    }
}

