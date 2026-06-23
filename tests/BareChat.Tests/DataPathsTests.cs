using BareChat.Storage;

namespace BareChat.Tests;

public class DataPathsTests
{
    [Fact]
    public void Null_datapath_falls_back_under_content_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "host");
        var p = DataPaths.Resolve(null, root);

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "App_Data", "barechat")), p.Root);
        Assert.Equal(Path.Combine(p.Root, "chat.db"), p.DatabaseFile);
        Assert.Equal(Path.Combine(p.Root, "blobs"), p.BlobsDirectory);
    }

    [Fact]
    public void Relative_datapath_resolves_against_content_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "host");
        var p = DataPaths.Resolve("chatdata", root);

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "chatdata")), p.Root);
    }

    [Fact]
    public void Absolute_datapath_is_used_as_is()
    {
        var abs = Path.Combine(Path.GetTempPath(), "explicit-barechat");
        var p = DataPaths.Resolve(abs, Path.GetTempPath());

        Assert.Equal(Path.GetFullPath(abs), p.Root);
    }
}
