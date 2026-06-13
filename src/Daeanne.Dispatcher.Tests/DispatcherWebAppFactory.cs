using System.Text.Json;
using System.Text.Json.Serialization;
using Daeanne.Dispatcher.Data;
using Daeanne.Dispatcher.Services;
using Daeanne.Shared.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Daeanne.Dispatcher.Tests;

/// <summary>
/// Test host factory for the Dispatcher. Uses an isolated SQLite file and a no-op
/// agent dispatcher so no real Copilot CLI processes are started during tests.
/// Background workers (DispatchWorker, SchedulerWorker, TaskCleanupWorker) are
/// removed to prevent interference with the test database.
/// </summary>
public sealed class DispatcherWebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _tempRoot;

    public string WorkDir { get; }

    public JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public DispatcherWebAppFactory()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "daeanne-tests", Guid.NewGuid().ToString("N"));
        WorkDir   = Path.Combine(_tempRoot, "tasks");
        Directory.CreateDirectory(WorkDir);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var dbPath = Path.Combine(_tempRoot, "test.db");

        builder.UseEnvironment("Testing");

        // Override DB connection string and work dir before the host builds.
        // ConfigurationManager applies sources in order; the in-memory source added here
        // overrides earlier appsettings.json values because it is added last.
        builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DispatcherDb"] = $"Data Source={dbPath}",
                ["Dispatch:WorkDir"]               = WorkDir,
                ["Dispatcher:ApiKey"]              = "",   // no auth for tests
            }));

        builder.ConfigureServices(services =>
        {
            // Remove background workers so they don't enqueue or move tasks during tests.
            RemoveHostedService<DispatchWorker>(services);
            RemoveHostedService<SchedulerWorker>(services);
            RemoveHostedService<TaskCleanupWorker>(services);

            // Replace the live IAgentDispatcher with a no-op — prevents any CLI invocations.
            var dispatcherDesc = services.SingleOrDefault(d => d.ServiceType == typeof(IAgentDispatcher));
            if (dispatcherDesc is not null) services.Remove(dispatcherDesc);
            services.AddSingleton<IAgentDispatcher, NoOpAgentDispatcher>();

            // Ensure the DbContext points to the test SQLite file (belt and suspenders —
            // the ConfigureAppConfiguration override above handles the production path,
            // but explicitly replacing the options guarantees the test file is used).
            var dbOptions = services.SingleOrDefault(d =>
                d.ServiceType == typeof(DbContextOptions<DispatcherDbContext>));
            if (dbOptions is not null) services.Remove(dbOptions);
            services.AddDbContext<DispatcherDbContext>(opts =>
                opts.UseSqlite($"Data Source={dbPath}"));
        });
    }

    private static void RemoveHostedService<T>(IServiceCollection services)
        where T : class, IHostedService
    {
        var desc = services.SingleOrDefault(d =>
            d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(T));
        if (desc is not null) services.Remove(desc);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best effort */ }
    }
}

/// <summary>
/// No-op dispatcher — returned results are ignored for dormant-task close tests
/// since dormant tasks are never enqueued during these tests.
/// </summary>
internal sealed class NoOpAgentDispatcher : IAgentDispatcher
{
    public Task<DispatchResult> DispatchAsync(AgentTask task, CancellationToken ct = default) =>
        Task.FromResult(new DispatchResult(true, null, null));

    public Task<DispatchResult?> TryResumeAsync(AgentTask task, string workDir, CancellationToken ct = default) =>
        Task.FromResult<DispatchResult?>(null);
}
