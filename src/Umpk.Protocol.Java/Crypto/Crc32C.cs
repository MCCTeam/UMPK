using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Umpk.Protocol.Java.Crypto;

/// <summary>CRC-32C (Castagnoli, polynomial 0x1EDC6F41) for hashed-slot encoding. No BCL type computes this polynomial: <c>System.IO.Hashing.Crc32</c> uses the IEEE polynomial. Uses the SSE4.2 <c>crc32</c> or ARM <c>crc32c</c> intrinsics when present, with a table-driven software fallback that can be forced for tests.</summary>
public static class Crc32C
{
    private static readonly uint[] Table = BuildTable();

    /// <summary>Computes the CRC-32C of <paramref name="data"/>, using intrinsics if available.</summary>
    public static uint Compute(ReadOnlySpan<byte> data) => Append(0u, data, forceSoftware: false);

    /// <summary>Computes the CRC-32C of <paramref name="data"/>. <paramref name="forceSoftware"/> selects the table-driven path even when hardware intrinsics are available (test cross-check).</summary>
    public static uint Compute(ReadOnlySpan<byte> data, bool forceSoftware) =>
        Append(0u, data, forceSoftware);

    /// <summary>Continues a running CRC-32C given the previous result. Pass the raw previous return value; this method handles the internal bit inversion. Enables chunked/streaming hashing.</summary>
    public static uint Append(uint previousCrc, ReadOnlySpan<byte> data, bool forceSoftware)
    {
        uint crc = ~previousCrc;

        if (!forceSoftware && Sse42.IsSupported)
            crc = ComputeSse42(crc, data);

        else if (!forceSoftware && Crc32.IsSupported)
            crc = ComputeArm(crc, data);

        else
            crc = ComputeTable(crc, data);

        return ~crc;
    }

    /// <summary>True when a hardware CRC-32C path is available on this machine.</summary>
    public static bool IsHardwareAccelerated => Sse42.IsSupported || Crc32.IsSupported;

    private static uint ComputeSse42(uint crc, ReadOnlySpan<byte> data)
    {
        int i = 0;
        if (Sse42.X64.IsSupported)
        {
            ulong crc64 = crc;
            while (i + 8 <= data.Length)
            {
                ulong chunk = BitConverterReadUInt64(data[i..]);
                crc64 = Sse42.X64.Crc32(crc64, chunk);
                i += 8;
            }

            crc = (uint)crc64;
        }

        for (; i < data.Length; i++)
            crc = Sse42.Crc32(crc, data[i]);

        return crc;
    }

    private static uint ComputeArm(uint crc, ReadOnlySpan<byte> data)
    {
        int i = 0;
        if (Crc32.Arm64.IsSupported)
            while (i + 8 <= data.Length)
            {
                ulong chunk = BitConverterReadUInt64(data[i..]);
                crc = Crc32.Arm64.ComputeCrc32C(crc, chunk);
                i += 8;
            }

        for (; i < data.Length; i++)
            crc = Crc32.ComputeCrc32C(crc, data[i]);

        return crc;
    }

    private static uint ComputeTable(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
            crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];

        return crc;
    }

    private static ulong BitConverterReadUInt64(ReadOnlySpan<byte> span) =>
        System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(span);

    private static uint[] BuildTable()
    {
        const uint poly = 0x82F63B78; // reflected 0x1EDC6F41
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ poly : crc >> 1;

            table[i] = crc;
        }

        return table;
    }
}
