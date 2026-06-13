using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace DaeanneFrontend.Api.Services;

/// <summary>
/// Stores and retrieves Web Push subscriptions in Azure Blob Storage
/// (container: push-subscriptions).  Each blob is keyed by a SHA256 hash of
/// the push endpoint URL so the name is safe for use as a blob name.
/// </summary>
public class PushSubscriptionStore
{
    private readonly BlobContainerClient _container;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public PushSubscriptionStore(BlobServiceClient blobService)
    {
        _container = blobService.GetBlobContainerClient("push-subscriptions");
    }

    /// <summary>Ensures the blob container exists (idempotent).</summary>
    public async Task EnsureContainerExistsAsync(CancellationToken ct = default)
    {
        await _container.CreateIfNotExistsAsync(
            publicAccessType: PublicAccessType.None,
            cancellationToken: ct);
    }

    /// <summary>
    /// Saves a push subscription.  If a subscription with the same endpoint
    /// already exists it is overwritten (handles re-subscription).
    /// </summary>
    public async Task SaveAsync(StoredPushSubscription subscription, CancellationToken ct = default)
    {
        await EnsureContainerExistsAsync(ct);
        var blobName = EndpointHash(subscription.Endpoint);
        var json = JsonSerializer.Serialize(subscription, JsonOpts);
        var blob = _container.GetBlobClient(blobName);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await blob.UploadAsync(stream, overwrite: true, cancellationToken: ct);
    }

    /// <summary>Returns all stored subscriptions.</summary>
    public async Task<List<StoredPushSubscription>> GetAllAsync(CancellationToken ct = default)
    {
        var result = new List<StoredPushSubscription>();
        await foreach (var item in _container.GetBlobsAsync(cancellationToken: ct))
        {
            var blob = _container.GetBlobClient(item.Name);
            var download = await blob.DownloadContentAsync(ct);
            var sub = JsonSerializer.Deserialize<StoredPushSubscription>(
                download.Value.Content.ToString(), JsonOpts);
            if (sub is not null)
                result.Add(sub);
        }
        return result;
    }

    /// <summary>Deletes a subscription by endpoint (used when a push returns 410 Gone).</summary>
    public async Task DeleteAsync(string endpoint, CancellationToken ct = default)
    {
        var blobName = EndpointHash(endpoint);
        var blob = _container.GetBlobClient(blobName);
        await blob.DeleteIfExistsAsync(cancellationToken: ct);
    }

    private static string EndpointHash(string endpoint)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(endpoint));
        return Convert.ToHexString(bytes).ToLowerInvariant() + ".json";
    }
}

/// <summary>A stored push subscription entry.</summary>
public record StoredPushSubscription(
    string Endpoint,
    string P256dh,
    string Auth);
