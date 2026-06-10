using Daeanne.Dispatcher.Services;

namespace Daeanne.Dispatcher.Endpoints;

public static class PreferencesEndpoints
{
    public static void MapPreferencesEndpoints(this WebApplication app)
    {
        // GET /preferences — return the current preferences document
        app.MapGet("/preferences", (PreferenceMemoryService prefs) =>
        {
            prefs.EnsurePreferencesFileExists();
            var json = File.ReadAllText(prefs.PreferencesPath);
            return Results.Content(json, "application/json");
        });

        // PATCH /preferences — Daeanne reports observed preference signals.
        // Body: array of { category, key, value }
        // Supported categories: "communication", "workingStyle"
        app.MapPatch("/preferences", (
            PreferenceUpdate[] updates,
            PreferenceMemoryService prefs) =>
        {
            if (updates.Length == 0)
                return Results.BadRequest("updates array must not be empty.");

            prefs.ApplyExplicit(updates);
            return Results.NoContent();
        });
    }
}
