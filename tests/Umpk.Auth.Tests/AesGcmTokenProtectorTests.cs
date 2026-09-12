using System.Security.Cryptography;
using System.Text;
using Umpk.Auth;
using Xunit;

namespace Umpk.Auth.Tests;

/// <summary>The at-rest protector used on every non-Windows platform: AES-256-GCM under a key file the OS keeps owner-only. Covers the round trip, that the stored bytes never carry the plaintext, that a wrong or tampered payload is rejected rather than silently accepted, and the plaintext-to-encrypted migration path for stores written before this existed.</summary>
public sealed class AesGcmTokenProtectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "umpk-aesgcm-" + Guid.NewGuid().ToString("N"));

    private string KeyPath => Path.Combine(_dir, "token-protection.key");

    private static byte[] Plain(string text = "{\"accessToken\":\"SECRET-TOKEN-VALUE\"}")
        => Encoding.UTF8.GetBytes(text);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);

    }

    [Fact]
    public async Task RoundTrip_ReturnsTheOriginalBytes()
    {
        using var protector = new AesGcmTokenProtector(KeyPath);
        byte[] plaintext = Plain();

        byte[] wrapped = await protector.ProtectAsync(plaintext, CancellationToken.None);
        byte[] recovered = await protector.UnprotectAsync(wrapped, CancellationToken.None);

        Assert.Equal(plaintext, recovered);
    }

    [Fact]
    public async Task ProtectedBytes_DoNotCarryThePlaintext()
    {
        using var protector = new AesGcmTokenProtector(KeyPath);
        byte[] wrapped = await protector.ProtectAsync(Plain(), CancellationToken.None);

        // The whole point: a token in a backup, a synced folder or an accidentally committed config directory must not be readable with `strings`.
        Assert.DoesNotContain("SECRET-TOKEN-VALUE", Encoding.UTF8.GetString(wrapped), StringComparison.Ordinal);
        Assert.NotEqual(Plain(), wrapped);
    }

    [Fact]
    public async Task ProtectingTheSameBytesTwice_ProducesDifferentCiphertext()
    {
        using var protector = new AesGcmTokenProtector(KeyPath);

        byte[] first = await protector.ProtectAsync(Plain(), CancellationToken.None);
        byte[] second = await protector.ProtectAsync(Plain(), CancellationToken.None);

        // A repeated nonce under one AES-GCM key is a total break, so this is the assertion that the nonce is drawn fresh per call rather than derived from the key or fixed.
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task ADifferentKeyFile_CannotDecrypt()
    {
        byte[] wrapped;
        using (var original = new AesGcmTokenProtector(KeyPath))
            wrapped = await original.ProtectAsync(Plain(), CancellationToken.None);

        string otherKey = Path.Combine(_dir, "other.key");
        using var other = new AesGcmTokenProtector(otherKey);

        await Assert.ThrowsAnyAsync<CryptographicException>(
            async () => await other.UnprotectAsync(wrapped, CancellationToken.None));
    }

    [Fact]
    public async Task TamperedCiphertext_IsRejected()
    {
        using var protector = new AesGcmTokenProtector(KeyPath);
        byte[] wrapped = await protector.ProtectAsync(Plain(), CancellationToken.None);

        // Flip a bit in the ciphertext body; the GCM tag has to catch it.
        wrapped[^1] ^= 0xFF;

        // AES-GCM raises AuthenticationTagMismatchException, a CryptographicException subclass, which is exactly what FileTokenStore's catch clause is written against.
        await Assert.ThrowsAnyAsync<CryptographicException>(
            async () => await protector.UnprotectAsync(wrapped, CancellationToken.None));
    }

    [Fact]
    public async Task TruncatedPayload_IsRejected()
    {
        using var protector = new AesGcmTokenProtector(KeyPath);
        byte[] wrapped = await protector.ProtectAsync(Plain(), CancellationToken.None);

        await Assert.ThrowsAnyAsync<CryptographicException>(
            async () => await protector.UnprotectAsync(wrapped[..8], CancellationToken.None));
    }

    [Fact]
    public async Task LegacyPlaintextPayload_IsReadBackUnchanged()
    {
        // A store written before this protector existed holds bare JSON. It has to keep working, or turning encryption on would sign every user out with an unreadable-cache warning.
        using var protector = new AesGcmTokenProtector(KeyPath);
        byte[] legacy = Plain();

        byte[] recovered = await protector.UnprotectAsync(legacy, CancellationToken.None);

        Assert.Equal(legacy, recovered);
    }

    [Fact]
    public async Task TheKeyFile_IsCreatedOwnerOnly()
    {
        using var protector = new AesGcmTokenProtector(KeyPath);
        await protector.ProtectAsync(Plain(), CancellationToken.None);

        Assert.True(File.Exists(KeyPath));

        if (OperatingSystem.IsWindows())
        {
            return; // Unix-only assertion.
        }

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(KeyPath));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(_dir));
    }

    [Fact]
    public async Task AnExistingKeyFile_IsReusedAcrossInstances()
    {
        byte[] wrapped;
        using (var first = new AesGcmTokenProtector(KeyPath))
            wrapped = await first.ProtectAsync(Plain(), CancellationToken.None);

        // A restart must still read what the last run wrote, which is the whole point of a token cache.
        using var second = new AesGcmTokenProtector(KeyPath);
        Assert.Equal(Plain(), await second.UnprotectAsync(wrapped, CancellationToken.None));
    }

    [Fact]
    public async Task AStoreRoundTripsThroughTheProtector()
    {
        // The seam that matters in practice: FileTokenStore over this protector, end to end.
        var store = new FileTokenStore(Path.Combine(_dir, "tokens"), new AesGcmTokenProtector(KeyPath));
        var session = new JavaSession(
            new GameProfile(Guid.NewGuid(), "Notch"), "ACCESS-TOKEN", DateTimeOffset.UtcNow.AddHours(1),
            "REFRESH-TOKEN", AuthKind.Microsoft);

        await store.SetAsync("session:notch", session, CancellationToken.None);

        string file = Directory.GetFiles(Path.Combine(_dir, "tokens"), "*.tok").Single();
        string onDisk = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file, CancellationToken.None));
        Assert.DoesNotContain("ACCESS-TOKEN", onDisk, StringComparison.Ordinal);
        Assert.DoesNotContain("REFRESH-TOKEN", onDisk, StringComparison.Ordinal);

        JavaSession? read = await store.GetAsync<JavaSession>("session:notch", CancellationToken.None);
        Assert.NotNull(read);
        Assert.Equal("ACCESS-TOKEN", read.AccessToken);
        Assert.Equal("Notch", read.Profile.Name);
    }

    [Fact]
    public async Task ALostKey_CostsOneSignIn_NotACrash()
    {
        // The failure mode a user actually hits: the key file is gone (new machine, wiped home, rotated by hand) but the token directory survived. The store must read that as a cache miss, so the next start signs in again, rather than throwing out of the auth flow.
        string tokens = Path.Combine(_dir, "tokens");
        var store = new FileTokenStore(tokens, new AesGcmTokenProtector(KeyPath));
        var session = new JavaSession(
            new GameProfile(Guid.NewGuid(), "Notch"), "ACCESS-TOKEN", DateTimeOffset.UtcNow.AddHours(1),
            "REFRESH-TOKEN", AuthKind.Microsoft);
        await store.SetAsync("session:notch", session, CancellationToken.None);

        File.Delete(KeyPath);
        var reopened = new FileTokenStore(tokens, new AesGcmTokenProtector(KeyPath));

        Assert.Null(await reopened.GetAsync<JavaSession>("session:notch", CancellationToken.None));

        // The undecryptable entry is dropped rather than left to fail on every later start.
        Assert.Empty(Directory.GetFiles(tokens, "*.tok"));
    }

    [Fact]
    public async Task ConcurrentProtectAndUnprotect_StayCorrect()
    {
        // One protector instance is shared by every write the store makes, and AesGcm is not documented as thread-safe, so the cipher has to be used under the protector's own lock. Without it this races into corrupted output or a tag mismatch.
        using var protector = new AesGcmTokenProtector(KeyPath);

        byte[][] payloads = [.. Enumerable.Range(0, 32).Select(i => Plain($"{{\"token\":\"value-{i}\"}}"))];

        await Task.WhenAll(payloads.Select(async payload =>
        {
            for (int round = 0; round < 8; round++)
            {
                byte[] wrapped = await protector.ProtectAsync(payload, CancellationToken.None);
                Assert.Equal(payload, await protector.UnprotectAsync(wrapped, CancellationToken.None));
            }
        }));
    }

    [Fact]
    public void CreateDefault_OnUnix_ProtectsAtRest()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Windows keeps DPAPI.
        }

        ITokenProtector protector = TokenProtectors.CreateDefault();
        using (protector as IDisposable)
            Assert.IsType<AesGcmTokenProtector>(protector);

    }
}
