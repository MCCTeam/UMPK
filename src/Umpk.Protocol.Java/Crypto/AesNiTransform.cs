using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using ArmAes = System.Runtime.Intrinsics.Arm.Aes;
using X86Aes = System.Runtime.Intrinsics.X86.Aes;

namespace Umpk.Protocol.Java.Crypto;

/// <summary>Hardware-accelerated AES-128 ECB single-block encryption using x86 AES-NI or ARM crypto instructions. Only the forward (encrypt) direction is needed: AES-CFB8 uses block encryption for both stream encryption and decryption.</summary>
internal sealed class AesNiTransform : IAesBlockTransform
{
    // Round keys 0..10 for encryption (128-bit AES has 11 round keys).
    private readonly Vector128<byte>[] _roundKeys;

    private AesNiTransform(Vector128<byte>[] roundKeys) => _roundKeys = roundKeys;

    public static bool IsSupported =>
        (Sse2.IsSupported && X86Aes.IsSupported) ||
        (AdvSimd.IsSupported && ArmAes.IsSupported);

    public static AesNiTransform Create(ReadOnlySpan<byte> key)
    {
        if (key.Length != 16)
            throw new ArgumentException("AES-128 requires a 16-byte key.", nameof(key));

        return new AesNiTransform(ExpandKey(key));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public void EncryptBlock(ReadOnlySpan<byte> input, Span<byte> output)
    {
        Vector128<byte> b = Unsafe.ReadUnaligned<Vector128<byte>>(
            ref MemoryMarshal.GetReference(input));
        Vector128<byte>[] k = _roundKeys;
        _ = k[10]; // hoist bounds checks

        if (X86Aes.IsSupported)
        {
            b = Sse2.Xor(b, k[0]);
            b = X86Aes.Encrypt(b, k[1]);
            b = X86Aes.Encrypt(b, k[2]);
            b = X86Aes.Encrypt(b, k[3]);
            b = X86Aes.Encrypt(b, k[4]);
            b = X86Aes.Encrypt(b, k[5]);
            b = X86Aes.Encrypt(b, k[6]);
            b = X86Aes.Encrypt(b, k[7]);
            b = X86Aes.Encrypt(b, k[8]);
            b = X86Aes.Encrypt(b, k[9]);
            b = X86Aes.EncryptLast(b, k[10]);
        }
        else
        {
            // ARM: AESE does AddRoundKey(with its arg) then SubBytes+ShiftRows; AESMC does MixColumns.
            b = ArmAes.MixColumns(ArmAes.Encrypt(b, k[0]));
            b = ArmAes.MixColumns(ArmAes.Encrypt(b, k[1]));
            b = ArmAes.MixColumns(ArmAes.Encrypt(b, k[2]));
            b = ArmAes.MixColumns(ArmAes.Encrypt(b, k[3]));
            b = ArmAes.MixColumns(ArmAes.Encrypt(b, k[4]));
            b = ArmAes.MixColumns(ArmAes.Encrypt(b, k[5]));
            b = ArmAes.MixColumns(ArmAes.Encrypt(b, k[6]));
            b = ArmAes.MixColumns(ArmAes.Encrypt(b, k[7]));
            b = ArmAes.MixColumns(ArmAes.Encrypt(b, k[8]));
            b = ArmAes.Encrypt(b, k[9]);
            b = AdvSimd.Xor(b, k[10]);
        }

        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(output), b);
    }

    private static Vector128<byte>[] ExpandKey(ReadOnlySpan<byte> key)
    {
        var keys = new Vector128<byte>[11];

        if (X86Aes.IsSupported)
        {
            keys[0] = Unsafe.ReadUnaligned<Vector128<byte>>(ref MemoryMarshal.GetReference(key));
            ExpandRoundX86(keys, 1, X86Aes.KeygenAssist(keys[0], 0x01));
            ExpandRoundX86(keys, 2, X86Aes.KeygenAssist(keys[1], 0x02));
            ExpandRoundX86(keys, 3, X86Aes.KeygenAssist(keys[2], 0x04));
            ExpandRoundX86(keys, 4, X86Aes.KeygenAssist(keys[3], 0x08));
            ExpandRoundX86(keys, 5, X86Aes.KeygenAssist(keys[4], 0x10));
            ExpandRoundX86(keys, 6, X86Aes.KeygenAssist(keys[5], 0x20));
            ExpandRoundX86(keys, 7, X86Aes.KeygenAssist(keys[6], 0x40));
            ExpandRoundX86(keys, 8, X86Aes.KeygenAssist(keys[7], 0x80));
            ExpandRoundX86(keys, 9, X86Aes.KeygenAssist(keys[8], 0x1b));
            ExpandRoundX86(keys, 10, X86Aes.KeygenAssist(keys[9], 0x36));
            return keys;
        }

        // ARM / portable schedule: compute the 176-byte schedule with the software key schedule, then load each round key as a Vector128. This keeps the round-key layout identical to the x86 path (11 round keys), which the ARM EncryptBlock consumes directly.
        Span<byte> expanded = stackalloc byte[176];
        SoftwareAes.ExpandKey(key, expanded);
        for (int i = 0; i < 11; i++)
            keys[i] = Unsafe.ReadUnaligned<Vector128<byte>>(
                ref MemoryMarshal.GetReference(expanded[(i * 16)..]));

        return keys;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ExpandRoundX86(Vector128<byte>[] keys, int i, Vector128<byte> assist)
    {
        Vector128<byte> s = keys[i - 1];
        Vector128<byte> t = Sse2.Shuffle(assist.AsUInt32(), 0xFF).AsByte();

        s = Sse2.Xor(s, Sse2.ShiftLeftLogical128BitLane(s, 4));
        s = Sse2.Xor(s, Sse2.ShiftLeftLogical128BitLane(s, 8));

        keys[i] = Sse2.Xor(s, t);
    }
}
