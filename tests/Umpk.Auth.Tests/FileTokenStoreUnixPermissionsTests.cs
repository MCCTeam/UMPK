using Umpk;
using Umpk.Auth;
using Xunit;

namespace Umpk.Auth.Tests;

// Verifies that on Unix the FileTokenStore restricts token files to 0600 and its directory to 0700.
public sealed class FileTokenStoreUnixPermissionsTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "umpk-r7-mode-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);

    }

    [Fact]
    public async Task FileStore_OnUnix_RestrictsFileAndDirectoryPermissions()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Unix-only assertion.
        }

        var store = new FileTokenStore(_dir, new NoOpTokenProtector());
        var session = new JavaSession(
            new GameProfile(Guid.NewGuid(), "Notch"), "ACCESS", DateTimeOffset.UtcNow.AddHours(1),
            "REFRESH", AuthKind.Microsoft);

        await store.SetAsync("session:notch", session, CancellationToken.None);

        UnixFileMode dirMode = File.GetUnixFileMode(_dir);
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, dirMode);

        string file = Directory.GetFiles(_dir, "*.tok").Single();
        UnixFileMode fileMode = File.GetUnixFileMode(file);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, fileMode);
    }
}
