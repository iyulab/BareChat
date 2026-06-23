using System.Collections.Concurrent;
using BareChat.Core;
using BareChat.Core.Domain;

namespace BareChat.Storage;

/// <summary>In-memory <see cref="IBlobStore"/> for tests and demos.</summary>
public sealed class InMemoryBlobStore : IBlobStore
{
    private readonly ConcurrentDictionary<string, (BlobInfo Info, byte[] Bytes)> _blobs = new(StringComparer.Ordinal);

    public async Task<BlobInfo> SaveAsync(Stream content, string contentType, string? fileName = null, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct).ConfigureAwait(false);
        var bytes = ms.ToArray();
        var info = new BlobInfo
        {
            BlobId = Guid.NewGuid().ToString("N"),
            ContentType = contentType,
            Size = bytes.LongLength,
            FileName = fileName
        };
        _blobs[info.BlobId] = (info, bytes);
        return info;
    }

    public Task<Stream?> OpenReadAsync(string blobId, CancellationToken ct = default)
        => Task.FromResult<Stream?>(_blobs.TryGetValue(blobId, out var b) ? new MemoryStream(b.Bytes, writable: false) : null);

    public Task<BlobInfo?> GetInfoAsync(string blobId, CancellationToken ct = default)
        => Task.FromResult<BlobInfo?>(_blobs.TryGetValue(blobId, out var b) ? b.Info : null);
}
