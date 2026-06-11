using System.Text.Json;
using Azure.Storage.Blobs;
using Daeanne.Shared.Models;

namespace DaeanneFrontend.Api.Services;

public class ResultStore
{
    private readonly BlobContainerClient _container;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ResultStore(BlobServiceClient blobService)
    {
        _container = blobService.GetBlobContainerClient("frontend-results");
        _container.CreateIfNotExists();
    }

    public async Task SaveAsync(FrontendResult result)
    {
        var blob = _container.GetBlobClient($"{result.CorrelationId}.json");
        var json = JsonSerializer.Serialize(result, JsonOpts);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await blob.UploadAsync(stream, overwrite: true);
    }

    public async Task<FrontendResult?> GetAsync(string correlationId)
    {
        var blob = _container.GetBlobClient($"{correlationId}.json");
        if (!await blob.ExistsAsync()) return null;

        var response = await blob.DownloadContentAsync();
        return JsonSerializer.Deserialize<FrontendResult>(
            response.Value.Content.ToString(), JsonOpts);
    }
}
