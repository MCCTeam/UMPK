using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Auth.Persistence;

namespace Umpk.Auth;

/// <summary>The default persistent <see cref="ITokenStore"/>: one file per key under a directory, each file holding the protected serialized value. Serialization is closed-type through the source-generated context; unknown types throw. At rest the bytes are wrapped by an <see cref="ITokenProtector"/> and, on Unix, the directory and files are restricted to owner read/write (chmod 600 / 700). A corrupted or unreadable file is treated as a cache miss and removed on the next write.</summary>
public sealed class FileTokenStore : ITokenStore
{
    private readonly string _directory;
    private readonly ITokenProtector _protector;
    private readonly ILogger _logger;

    public FileTokenStore(string directory, ITokenProtector protector, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(protector);
        _directory = directory;
        _protector = protector;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public async ValueTask<T?> GetAsync<T>(string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!PersistedJson.IsSupported<T>())
            throw new NotSupportedException(
                "FileTokenStore persists only Umpk.Auth's own types; implement a custom ITokenStore for others.");

        string path = PathFor(key);
        if (!File.Exists(path))
            return default;

        try
        {
            byte[] wrapped = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            byte[] plaintext = await _protector.UnprotectAsync(wrapped, ct).ConfigureAwait(false);
            return PersistedJson.Deserialize<T>(plaintext);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or System.Security.Cryptography.CryptographicException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(ex, "Discarding unreadable token cache entry for key {Key}.", key);
            TryDelete(path);
            return default;
        }
    }

    /// <inheritdoc />
    public async ValueTask SetAsync<T>(string key, T value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        EnsureDirectory();

        byte[] plaintext = PersistedJson.Serialize(value);
        byte[] wrapped = await _protector.ProtectAsync(plaintext, ct).ConfigureAwait(false);

        string path = PathFor(key);
        string temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, wrapped, ct).ConfigureAwait(false);
        TokenStoragePermissions.RestrictFile(temp);
        File.Move(temp, path, overwrite: true);
    }

    /// <inheritdoc />
    public ValueTask RemoveAsync(string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ct.ThrowIfCancellationRequested();
        TryDelete(PathFor(key));
        return ValueTask.CompletedTask;
    }

    private string PathFor(string key)
    {
        // Keys are hashed to a filesystem-safe name so arbitrary key strings cannot escape the directory.
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        string name = Convert.ToHexStringLower(hash);
        return Path.Combine(_directory, name + ".tok");
    }

    private void EnsureDirectory()
    {
        if (Directory.Exists(_directory))
            return;

        Directory.CreateDirectory(_directory);
        TokenStoragePermissions.RestrictDirectory(_directory);
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);

        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Could not delete token cache file {Path}.", path);
        }
    }

}
