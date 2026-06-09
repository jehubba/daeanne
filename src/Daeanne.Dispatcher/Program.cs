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
builder.Services.AddHostedService<TaskCleanupWorker>();

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

    var taskCols = db.Database.SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Tasks')").ToList();
    if (!taskCols.Contains("IsScheduled"))
        db.Database.ExecuteSqlRaw("ALTER TABLE Tasks ADD COLUMN IsScheduled INTEGER NOT NULL DEFAULT 0");
    if (!taskCols.Contains("ScheduledJobId"))
        db.Database.ExecuteSqlRaw("ALTER TABLE Tasks ADD COLUMN ScheduledJobId TEXT");
    if (!taskCols.Contains("ParentTaskId"))
        db.Database.ExecuteSqlRaw("ALTER TABLE Tasks ADD COLUMN ParentTaskId TEXT");
    if (!taskCols.Contains("SessionName"))
        db.Database.ExecuteSqlRaw("ALTER TABLE Tasks ADD COLUMN SessionName TEXT");
    if (!taskCols.Contains("CallbackAcknowledgedAt"))
        db.Database.ExecuteSqlRaw("ALTER TABLE Tasks ADD COLUMN CallbackAcknowledgedAt TEXT");
    if (!taskCols.Contains("CallbackPostedAt"))
        db.Database.ExecuteSqlRaw("ALTER TABLE Tasks ADD COLUMN CallbackPostedAt TEXT");

    var jobCols = db.Database.SqlQueryRaw<string>("SELECT name FROM pragma_table_info('ScheduledJobs')").ToList();
    if (!jobCols.Contains("SessionName"))
        db.Database.ExecuteSqlRaw("ALTER TABLE ScheduledJobs ADD COLUMN SessionName TEXT");

    // OutboxSms table for SMS outbox (Bridge sends when ACS SMS is configured)
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS OutboxSmsList (
            Id            TEXT    NOT NULL PRIMARY KEY,
            [To]          TEXT    NOT NULL,
            Body          TEXT    NOT NULL,
            Status        TEXT    NOT NULL DEFAULT 'Pending',
            CreatedAt     TEXT    NOT NULL,
            SentAt        TEXT,
            Error         TEXT,
            CorrelationId TEXT
        )
        """);
    db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_OutboxSmsList_Status ON OutboxSmsList (Status)");

    // Add ScheduledJobs table if this is an existing DB (EnsureCreated won't alter existing schemas).
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS ScheduledJobs (
            Id                   TEXT    NOT NULL PRIMARY KEY,
            Name                 TEXT    NOT NULL,
            JobType              TEXT    NOT NULL,
            TaskType             TEXT    NOT NULL,
            Prompt               TEXT    NOT NULL,
            RunAt                TEXT,
            TimeOfDay            TEXT,
            DayOfWeek            TEXT,
            IntervalMinutes      INTEGER,
            CorrelationIdTemplate TEXT,
            NextRunAt            TEXT    NOT NULL,
            LastFiredAt          TEXT,
            IsActive             INTEGER NOT NULL DEFAULT 1,
            CreatedAt            TEXT    NOT NULL
        )
        """);
    db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ScheduledJobs_NextRunAt ON ScheduledJobs (NextRunAt)");
    db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ScheduledJobs_IsActive  ON ScheduledJobs (IsActive)");

    // SmsMessages: unified inbound/outbound conversation log
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS SmsMessages (
            Id             TEXT    NOT NULL PRIMARY KEY,
            Direction      TEXT    NOT NULL,
            Phone          TEXT    NOT NULL,
            Body           TEXT    NOT NULL,
            Timestamp      TEXT    NOT NULL,
            TaskId         TEXT,
            QuoteTimestamp TEXT,
            ReferenceToken TEXT,
            OutboxSmsId    TEXT
        )
        """);
    db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_SmsMessages_Phone     ON SmsMessages (Phone)");
    db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_SmsMessages_Timestamp ON SmsMessages (Timestamp)");
    db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_SmsMessages_TaskId    ON SmsMessages (TaskId)");
}

// Seed built-in ScheduledJob records (daily summary, weekly 1:1) on first start.
var seedLogger = app.Services.GetRequiredService<ILogger<Program>>();
await SchedulerWorker.SeedBuiltInJobsAsync(app.Services, app.Configuration, seedLogger);

app.Services.GetRequiredService<PreferenceMemoryService>().EnsurePreferencesFileExists();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "Daeanne.Dispatcher",
    timestamp = DateTime.UtcNow
}));

app.MapTaskEndpoints();
app.MapOutboxEndpoints();
app.MapOutboxSmsEndpoints();
app.MapSmsConversationEndpoints();
app.MapSchedulerEndpoints();
app.MapSchedulerCronEndpoints();

app.Run();
