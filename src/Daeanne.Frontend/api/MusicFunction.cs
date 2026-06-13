using System.Text;
using System.Text.Json;
using DaeanneFrontend.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace DaeanneFrontend.Api;

public class MusicFunction
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions SerializeOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions DeserializeOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string SystemPrompt = """
        You are a guitar chord chart assistant. For the requested song, return a JSON object.

        Required JSON structure:
        {
          "title": "song title",
          "artist": "artist name",
          "key": "key signature (e.g. G major)",
          "tempo": "BPM string or null",
          "source": "known or generated",
          "chords": [
            { "name": "G", "frets": "320003", "fingers": "210003" }
          ],
          "sections": [
            {
              "label": "Verse 1",
              "lines": [
                { "chords": ["G", "Em"], "lyrics": "lyric text or null" }
              ]
            }
          ]
        }

        Frets format: exactly 6 characters, strings 6-1 (low E to high e).
        Use 'x' for muted, '0' for open, '1'-'9' for fret numbers.
        Fingers format: 6 characters, finger numbers 1-4 (0 = open or muted).
        Use "source": "known" if you have reliable training-data chord knowledge of this song.
        Use "source": "generated" if you are generating a reasonable approximation.
        Include realistic lyrics for each line if known; otherwise set "lyrics" to null.
        Output only valid JSON — no markdown fences, no explanation, no other text.
        """;

    public MusicFunction(HttpClient http)
    {
        _http = http;
    }

    [Function("musicSearch")]
    public async Task<IActionResult> Search(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "music/search")] HttpRequest req)
    {
        var query = req.Query["q"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(query))
            return new BadRequestObjectResult(new { error = "Query parameter 'q' is required" });

        var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        var key = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
        var deployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "gpt-4o";

        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(key))
        {
            var unconfigured = new MusicSearchResultDto(
                query, null, null, null, null, "error", [], [],
                "Music search requires AZURE_OPENAI_ENDPOINT and AZURE_OPENAI_KEY.");
            return Respond(unconfigured);
        }

        try
        {
            var requestPayload = new
            {
                messages = new[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = $"Song: {query}" }
                },
                temperature = 0.3,
                max_tokens = 2000
            };

            var url = $"{endpoint.TrimEnd('/')}/openai/deployments/{deployment}" +
                      "/chat/completions?api-version=2024-08-01-preview";

            using var httpReq = new HttpRequestMessage(HttpMethod.Post, url);
            httpReq.Headers.Add("api-key", key);
            httpReq.Content = new StringContent(
                JsonSerializer.Serialize(requestPayload, SerializeOpts),
                Encoding.UTF8,
                "application/json");

            var httpResp = await _http.SendAsync(httpReq);
            if (!httpResp.IsSuccessStatusCode)
            {
                var errBody = await httpResp.Content.ReadAsStringAsync();
                var snippet = errBody.Length > 200 ? errBody[..200] : errBody;
                return Respond(new MusicSearchResultDto(
                    query, null, null, null, null, "error", [], [],
                    $"OpenAI request failed ({(int)httpResp.StatusCode}): {snippet}"));
            }

            var responseBody = await httpResp.Content.ReadAsStringAsync();
            var openAiResp = JsonSerializer.Deserialize<OpenAiChatResponse>(responseBody, DeserializeOpts);
            var content = openAiResp?.Choices?.FirstOrDefault()?.Message?.Content ?? "";

            // Strip markdown code fences if the model ignores instructions
            content = content.Trim();
            if (content.StartsWith("```"))
            {
                var start = content.IndexOf('\n') + 1;
                var end = content.LastIndexOf("```");
                content = end > start ? content[start..end].Trim() : content;
            }

            var raw = JsonSerializer.Deserialize<MusicSearchRaw>(content, DeserializeOpts);
            if (raw is null)
            {
                return Respond(new MusicSearchResultDto(
                    query, null, null, null, null, "error", [], [],
                    "Failed to parse chord chart from model response."));
            }

            var result = new MusicSearchResultDto(
                query,
                raw.Title,
                raw.Artist,
                raw.Key,
                raw.Tempo,
                raw.Source ?? "generated",
                raw.Chords?.Select(c => new ChordDto(
                    c.Name ?? "?",
                    c.Frets ?? "xxxxxx",
                    c.Fingers ?? "000000")).ToList() ?? [],
                raw.Sections?.Select(s => new SongSectionDto(
                    s.Label ?? "Section",
                    s.Lines?.Select(l => new SongLineDto(l.Chords ?? [], l.Lyrics)).ToList() ?? []
                )).ToList() ?? []);

            return Respond(result);
        }
        catch (Exception ex)
        {
            return Respond(new MusicSearchResultDto(
                query, null, null, null, null, "error", [], [],
                $"Search failed: {ex.Message}"));
        }
    }

    private ContentResult Respond(MusicSearchResultDto dto) => new()
    {
        Content = JsonSerializer.Serialize(dto, SerializeOpts),
        ContentType = "application/json",
        StatusCode = 200
    };

    // Internal types for deserializing the OpenAI REST response
    private record OpenAiChatResponse(List<OpenAiChoice>? Choices);
    private record OpenAiChoice(OpenAiMessage? Message);
    private record OpenAiMessage(string? Role, string? Content);

    // Internal types for deserializing the model's chord-chart JSON payload
    private record MusicSearchRaw(
        string? Title, string? Artist, string? Key, string? Tempo, string? Source,
        List<ChordRaw>? Chords, List<SectionRaw>? Sections);
    private record ChordRaw(string? Name, string? Frets, string? Fingers);
    private record SectionRaw(string? Label, List<LineRaw>? Lines);
    private record LineRaw(List<string>? Chords, string? Lyrics);
}
