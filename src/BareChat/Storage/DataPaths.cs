namespace BareChat.Storage;

/// <summary>
/// Resolves the single <c>DataPath</c> setting into concrete file locations.
/// Relative paths resolve against the host content root; null/empty falls back to <c>App_Data/barechat</c>.
/// </summary>
public sealed class DataPaths
{
    public string Root { get; }
    public string DatabaseFile { get; }
    public string BlobsDirectory { get; }

    private DataPaths(string root)
    {
        Root = root;
        DatabaseFile = Path.Combine(root, "chat.db");
        BlobsDirectory = Path.Combine(root, "blobs");
    }

    public static DataPaths Resolve(string? configuredDataPath, string contentRoot)
    {
        var path = string.IsNullOrWhiteSpace(configuredDataPath)
            ? Path.Combine("App_Data", "barechat")
            : configuredDataPath;

        var root = Path.IsPathRooted(path) ? path : Path.Combine(contentRoot, path);
        return new DataPaths(Path.GetFullPath(root));
    }
}
