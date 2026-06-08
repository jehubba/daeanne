using System.Threading.Channels;
using System.Text.Json.Serialization;
using Daeanne.Dispatcher.Data;
using Daeanne.Dispatcher.Endpoints;
using Daeanne.Dispatcher.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

// Bind to localhost only — this service is not intended for network access
builder.WebHost.UseUrls(
    builder.Configuration["Dispatcher:Url"] ?? "http://127.0.0.1:47777");

// Serialize enums as strings in request/response JSON
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var connStr = builder.Configuration.GetConnectionString("DispatcherDb");
if (string.IsNullOrWhiteSpace(connStr))
    connStr = $"Data Source={Path.Combine(AppContext.BaseDirectory, "dispatcher.db")}";
builder.Services.AddDbContext<DispatcherDbContext>(options => options.UseSqlite(connStr));

// Dispatch infrastructure
builder.Services.Configure<DispatchConfig>(builder.Configuration.GetSection("Dispatch"));
builder.Services.AddSingleton(Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true }));
builder.Services.AddSingleton<PreferenceMemoryService>();
builder.Services.AddSingleton<IAgentDispatcher, CopilotCliDispatcher>();
builder.Services.AddHostedService<DispatchWorker>();
builder.Services.AddHostedService<SchedulerWorker>();

var app = builder.Build();

// Create/verify database schema on startup.
// EnsureCreated is used here for Phase 1 simplicity — it reliably bootstraps a fresh SQLite
// database. When we need schema evolution (Phase 2+), replace with Database.Migrate() after
// deleting the generated migration and regenerating against the live schema.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DispatcherDbContext>();
    db.Database.EnsureCreated();

    // Idempotent schema evolution: add new columns that EnsureCreated won't add to existing DBs.
    // Use PRAGMA check first so we never attempt the ALTER when the column exists — avoids EF error logs.
    var cols = db.Database.SqlQueryRaw<string>("SELECT name FROM pragma_table_info('OutboxEmails')").ToList();
    if (!cols.Contains("ReplyToGraphMessageId"))
        db.Database.ExecuteSqlRaw("ALTER TABLE OutboxEmails ADD COLUMN ReplyToGraphMessageId TEXT");
}

app.Services.GetRequiredService<PreferenceMemoryService>().EnsurePreferencesFileExists();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "Daeanne.Dispatcher",
    timestamp = DateTime.UtcNow
}));

app.MapTaskEndpoints();
app.MapOutboxEndpoints();
app.MapSchedulerEndpoints();

app.Run();
