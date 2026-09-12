namespace Umpk.Auth;

/// <summary>At-rest protection seam for the file token store. Implementations wrap and unwrap the serialized bytes (for example OS data protection). A protector never sees or logs the plaintext token strings beyond the opaque byte payload it is asked to protect.</summary>
public interface ITokenProtector
{
    /// <summary>Wraps <paramref name="plaintext"/> for storage. The result is opaque.</summary>
    ValueTask<byte[]> ProtectAsync(byte[] plaintext, CancellationToken ct);

    /// <summary>Unwraps bytes previously produced by <see cref="ProtectAsync"/>.</summary>
    ValueTask<byte[]> UnprotectAsync(byte[] protectedData, CancellationToken ct);
}
