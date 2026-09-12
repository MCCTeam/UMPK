using Umpk;
using Umpk.Auth;
using Umpk.Auth.Persistence;
using Umpk.Protocol.Java.Signing;
using Xunit;

namespace Umpk.Auth.Tests;

public sealed class TokenStoreTests : IDisposable
{
    private static readonly DateTimeOffset When = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly string _dir;

    public TokenStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "umpk-auth-tests-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);

    }

    private static JavaSession SampleSession() =>
        new(new GameProfile(new Guid("4566e69f-c907-48ee-8d71-d7ba5aa00d20"), "Dinnerbone"),
            "ACCESS_SECRET", When + TimeSpan.FromHours(1), "REFRESH_SECRET", AuthKind.Microsoft);

    private static PlayerCertificates SampleCerts() =>
        new("PUB", "PRIV", "SIG1", "SIG2", When + TimeSpan.FromDays(30), When + TimeSpan.FromDays(15));

    [Fact]
    public async Task FileStore_RoundTripsSession_ThroughNoOpProtector()
    {
        var store = new FileTokenStore(_dir, new NoOpTokenProtector());
        JavaSession original = SampleSession();
        await store.SetAsync("session:X", original, CancellationToken.None);

        JavaSession? loaded = await store.GetAsync<JavaSession>("session:X", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(original, loaded);
    }

    [Fact]
    public async Task FileStore_RoundTripsCertificates()
    {
        var store = new FileTokenStore(_dir, new NoOpTokenProtector());
        PlayerCertificates original = SampleCerts();
        await store.SetAsync("cert:X", original, CancellationToken.None);

        PlayerCertificates? loaded = await store.GetAsync<PlayerCertificates>("cert:X", CancellationToken.None);
        Assert.Equal(original, loaded);
    }

    [Fact]
    public async Task FileStore_RoundTripsThroughXorProtector()
    {
        var protector = new XorProtector(0x5A);
        var store = new FileTokenStore(_dir, protector);
        JavaSession original = SampleSession();
        await store.SetAsync("session:X", original, CancellationToken.None);

        // The on-disk bytes must not contain the plaintext token (protector actually transformed them).
        string file = Directory.GetFiles(_dir, "*.tok").Single();
        byte[] onDisk = await File.ReadAllBytesAsync(file, CancellationToken.None);
        Assert.DoesNotContain("ACCESS_SECRET", System.Text.Encoding.UTF8.GetString(onDisk), StringComparison.Ordinal);

        JavaSession? loaded = await store.GetAsync<JavaSession>("session:X", CancellationToken.None);
        Assert.Equal(original, loaded);
    }

    [Fact]
    public async Task FileStore_CorruptedFile_IsTreatedAsMissAndRemoved()
    {
        var store = new FileTokenStore(_dir, new NoOpTokenProtector());
        await store.SetAsync("session:X", SampleSession(), CancellationToken.None);

        string file = Directory.GetFiles(_dir, "*.tok").Single();
        await File.WriteAllTextAsync(file, "not valid json at all", CancellationToken.None);

        JavaSession? loaded = await store.GetAsync<JavaSession>("session:X", CancellationToken.None);
        Assert.Null(loaded);
        Assert.False(File.Exists(file), "corrupted entry should be removed");
    }

    [Fact]
    public async Task FileStore_Remove_DeletesEntry()
    {
        var store = new FileTokenStore(_dir, new NoOpTokenProtector());
        await store.SetAsync("session:X", SampleSession(), CancellationToken.None);
        await store.RemoveAsync("session:X", CancellationToken.None);
        Assert.Null(await store.GetAsync<JavaSession>("session:X", CancellationToken.None));
    }

    [Fact]
    public async Task FileStore_UnsupportedType_Throws()
    {
        var store = new FileTokenStore(_dir, new NoOpTokenProtector());
        await Assert.ThrowsAsync<NotSupportedException>(
            () => store.SetAsync("k", "a-plain-string", CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task InMemoryStore_RoundTrips()
    {
        var store = new InMemoryTokenStore();
        JavaSession original = SampleSession();
        await store.SetAsync("s", original, CancellationToken.None);
        Assert.Equal(original, await store.GetAsync<JavaSession>("s", CancellationToken.None));
    }

    [Fact]
    public async Task InMemoryStore_UnsupportedType_Throws()
    {
        var store = new InMemoryTokenStore();
        await Assert.ThrowsAsync<NotSupportedException>(
            async () => await store.GetAsync<int>("k", CancellationToken.None));
    }

    // A trivial reversible protector so the round-trip exercises the protector seam without DPAPI.
    private sealed class XorProtector(byte key) : ITokenProtector
    {
        public ValueTask<byte[]> ProtectAsync(byte[] plaintext, CancellationToken ct) => Xor(plaintext);

        public ValueTask<byte[]> UnprotectAsync(byte[] protectedData, CancellationToken ct) => Xor(protectedData);

        private ValueTask<byte[]> Xor(byte[] input)
        {
            var output = new byte[input.Length];
            for (int i = 0; i < input.Length; i++)
                output[i] = (byte)(input[i] ^ key);

            return ValueTask.FromResult(output);
        }
    }
}
