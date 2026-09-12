using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Umpk.Auth;

/// <summary>A Windows <see cref="ITokenProtector"/> that wraps bytes with DPAPI (current-user scope) via <c>crypt32!CryptProtectData</c>. Uses classic <c>DllImport</c> P/Invoke (rather than the source-generated <c>LibraryImport</c> attribute, which would require enabling unsafe code, or the out-of-box <c>ProtectedData</c> package, which would add a dependency). Only usable on Windows; construct through <see cref="TokenProtectors.CreateDefault"/> which selects the right protector per platform.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiTokenProtector : ITokenProtector
{
    /// <inheritdoc />
    public ValueTask<byte[]> ProtectAsync(byte[] plaintext, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Transform(plaintext, protect: true));
    }

    /// <inheritdoc />
    public ValueTask<byte[]> UnprotectAsync(byte[] protectedData, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(protectedData);
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Transform(protectedData, protect: false));
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inBlob = new DataBlob();
        var outBlob = new DataBlob();
        GCHandle pinned = GCHandle.Alloc(input, GCHandleType.Pinned);
        try
        {
            inBlob.DataSize = input.Length;
            inBlob.DataPtr = pinned.AddrOfPinnedObject();

            bool ok = protect
                ? CryptProtectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref outBlob);

            if (!ok)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                var result = new byte[outBlob.DataSize];
                Marshal.Copy(outBlob.DataPtr, result, 0, outBlob.DataSize);
                return result;
            }
            finally
            {
                if (outBlob.DataPtr != IntPtr.Zero)
                    LocalFree(outBlob.DataPtr);

            }
        }
        finally
        {
            pinned.Free();
        }
    }

    private const int CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int DataSize;
        public IntPtr DataPtr;
    }

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn, IntPtr description, IntPtr optionalEntropy, IntPtr reserved,
        IntPtr promptStruct, int flags, ref DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn, IntPtr description, IntPtr optionalEntropy, IntPtr reserved,
        IntPtr promptStruct, int flags, ref DataBlob dataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr handle);
}
