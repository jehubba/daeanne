using Daeanne.Dispatcher.Data;
using Daeanne.Shared.Models;
using Daeanne.Shared.Requests;
using Microsoft.EntityFrameworkCore;

namespace Daeanne.Dispatcher.Endpoints;

public static class OutboxEndpoints
{
    public static void MapOutboxEndpoints(this WebApplication app)
    {
        app.MapGet("/outbox", GetOutbox);
        app.MapPost("/outbox/email", QueueEmail);
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
            CorrelationId = request.CorrelationId
        };

        db.OutboxEmails.Add(email);
        await db.SaveChangesAsync();

        return Results.Created($"/outbox/{email.Id}", new { email.Id, email.Status });
    }
}
