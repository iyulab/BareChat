using System.Text.Json;
using BareChat.Core;
using BareChat.Core.Domain;

namespace BareChat.Storage;

/// <summary>
/// Stores blob bytes as files under a directory (default <c>{DataPath}/blobs</c>), with a sidecar
/// <c>.meta</c> JSON per blob. Keeps bytes out of the DB (BLOB-in-DB forbidden).
/// </summary>
public sealed class FileSystemBlobStore : IBlobStore
{
    private readonly string _dir;

    public FileSystemBlobStore(string blobsDirectory)
    {
        _dir = Path.GetFullPath(blobsDirectory);
        Directory.CreateDirectory(_dir);
    }

    public async Task<BlobInfo> SaveAsync(Stream content, string contentType, string? fileName = null, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var dataPath = Path.Combine(_dir, id);
        await using (var fs = File.Create(dataPath))
            await content.CopyToAsync(fs, ct).ConfigureAwait(false);

        var info = new BlobInfo
        {
            BlobId = id,
            ContentType = contentType,
            Size = new FileInfo(dataPath).Length,
            FileName = fileName
        };
        await File.WriteAllTextAsync(MetaPath(id), JsonSerializer.Serialize(info), ct).ConfigureAwait(false);
        return info;
    }

    public Task<Stream?> OpenReadAsync(string blobId, CancellationToken ct = default)
    {
        var path = Path.Combine(_dir, SafeId(blobId));
        Stream? stream = File.Exists(path) ? File.OpenRead(path) : null;
        return Task.FromResult(stream);
    }

    public async Task<BlobInfo?> GetInfoAsync(string blobId, CancellationToken ct = default)
    {
        var meta = MetaPath(SafeId(blobId));
        if (!File.Exists(meta))
            return null;
        var json = await File.ReadAllTextAsync(meta, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<BlobInfo>(json);
    }

    private string MetaPath(string id) => Path.Combine(_dir, id + ".meta");

    // Guard against path traversal from caller-supplied ids.
    private static string SafeId(string blobId) => Path.GetFileName(blobId);
}
