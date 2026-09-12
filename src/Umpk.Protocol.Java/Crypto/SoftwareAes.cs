namespace Umpk.Protocol.Java.Crypto;

/// <summary>Portable table-driven AES-128 ECB single-block encryption. Used on machines without AES intrinsics and as the forced-software path for tests that cross-check the intrinsic path.</summary>
internal sealed class SoftwareAes : IAesBlockTransform
{
    // Expanded key schedule: 11 round keys * 16 bytes = 176 bytes.
    private readonly byte[] _roundKeys;

    private SoftwareAes(byte[] roundKeys) => _roundKeys = roundKeys;

    public static SoftwareAes Create(ReadOnlySpan<byte> key)
    {
        if (key.Length != 16)
            throw new ArgumentException("AES-128 requires a 16-byte key.", nameof(key));

        var expanded = new byte[176];
        ExpandKey(key, expanded);
        return new SoftwareAes(expanded);
    }

    public void EncryptBlock(ReadOnlySpan<byte> input, Span<byte> output)
    {
        Span<byte> state = stackalloc byte[16];
        input[..16].CopyTo(state);

        ReadOnlySpan<byte> rk = _roundKeys;
        AddRoundKey(state, rk[..16]);

        for (int round = 1; round < 10; round++)
        {
            SubBytes(state);
            ShiftRows(state);
            MixColumns(state);
            AddRoundKey(state, rk.Slice(round * 16, 16));
        }

        SubBytes(state);
        ShiftRows(state);
        AddRoundKey(state, rk.Slice(160, 16));

        state.CopyTo(output);
    }

    /// <summary>Expands a 16-byte key into 176 bytes (11 round keys).</summary>
    internal static void ExpandKey(ReadOnlySpan<byte> key, Span<byte> expanded)
    {
        key[..16].CopyTo(expanded);
        int bytesGenerated = 16;
        int rconIteration = 1;
        Span<byte> temp = stackalloc byte[4];

        while (bytesGenerated < 176)
        {
            for (int i = 0; i < 4; i++)
                temp[i] = expanded[bytesGenerated - 4 + i];

            if (bytesGenerated % 16 == 0)
            {
                // Rotate word left by one byte.
                byte t = temp[0];
                temp[0] = temp[1];
                temp[1] = temp[2];
                temp[2] = temp[3];
                temp[3] = t;

                for (int i = 0; i < 4; i++)
                    temp[i] = SBox[temp[i]];

                temp[0] ^= Rcon[rconIteration];
                rconIteration++;
            }

            for (int i = 0; i < 4; i++)
            {
                expanded[bytesGenerated] = (byte)(expanded[bytesGenerated - 16] ^ temp[i]);
                bytesGenerated++;
            }
        }
    }

    private static void AddRoundKey(Span<byte> state, ReadOnlySpan<byte> roundKey)
    {
        for (int i = 0; i < 16; i++)
            state[i] ^= roundKey[i];

    }

    private static void SubBytes(Span<byte> state)
    {
        for (int i = 0; i < 16; i++)
            state[i] = SBox[state[i]];

    }

    private static void ShiftRows(Span<byte> s)
    {
        // State is column-major (s[r + 4c]). Shift row r left by r.
        Span<byte> t = stackalloc byte[16];
        s.CopyTo(t);
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                s[r + 4 * c] = t[r + 4 * ((c + r) % 4)];

    }

    private static void MixColumns(Span<byte> s)
    {
        for (int c = 0; c < 4; c++)
        {
            int i = 4 * c;
            byte a0 = s[i], a1 = s[i + 1], a2 = s[i + 2], a3 = s[i + 3];
            s[i] = (byte)(Mul2(a0) ^ Mul3(a1) ^ a2 ^ a3);
            s[i + 1] = (byte)(a0 ^ Mul2(a1) ^ Mul3(a2) ^ a3);
            s[i + 2] = (byte)(a0 ^ a1 ^ Mul2(a2) ^ Mul3(a3));
            s[i + 3] = (byte)(Mul3(a0) ^ a1 ^ a2 ^ Mul2(a3));
        }
    }

    private static byte Mul2(byte b) => (byte)((b << 1) ^ ((b >> 7) * 0x1b));

    private static byte Mul3(byte b) => (byte)(Mul2(b) ^ b);

    private static readonly byte[] Rcon =
    [
        0x00, 0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80, 0x1b, 0x36
    ];

    private static readonly byte[] SBox =
    [
        0x63, 0x7c, 0x77, 0x7b, 0xf2, 0x6b, 0x6f, 0xc5, 0x30, 0x01, 0x67, 0x2b, 0xfe, 0xd7, 0xab, 0x76,
        0xca, 0x82, 0xc9, 0x7d, 0xfa, 0x59, 0x47, 0xf0, 0xad, 0xd4, 0xa2, 0xaf, 0x9c, 0xa4, 0x72, 0xc0,
        0xb7, 0xfd, 0x93, 0x26, 0x36, 0x3f, 0xf7, 0xcc, 0x34, 0xa5, 0xe5, 0xf1, 0x71, 0xd8, 0x31, 0x15,
        0x04, 0xc7, 0x23, 0xc3, 0x18, 0x96, 0x05, 0x9a, 0x07, 0x12, 0x80, 0xe2, 0xeb, 0x27, 0xb2, 0x75,
        0x09, 0x83, 0x2c, 0x1a, 0x1b, 0x6e, 0x5a, 0xa0, 0x52, 0x3b, 0xd6, 0xb3, 0x29, 0xe3, 0x2f, 0x84,
        0x53, 0xd1, 0x00, 0xed, 0x20, 0xfc, 0xb1, 0x5b, 0x6a, 0xcb, 0xbe, 0x39, 0x4a, 0x4c, 0x58, 0xcf,
        0xd0, 0xef, 0xaa, 0xfb, 0x43, 0x4d, 0x33, 0x85, 0x45, 0xf9, 0x02, 0x7f, 0x50, 0x3c, 0x9f, 0xa8,
        0x51, 0xa3, 0x40, 0x8f, 0x92, 0x9d, 0x38, 0xf5, 0xbc, 0xb6, 0xda, 0x21, 0x10, 0xff, 0xf3, 0xd2,
        0xcd, 0x0c, 0x13, 0xec, 0x5f, 0x97, 0x44, 0x17, 0xc4, 0xa7, 0x7e, 0x3d, 0x64, 0x5d, 0x19, 0x73,
        0x60, 0x81, 0x4f, 0xdc, 0x22, 0x2a, 0x90, 0x88, 0x46, 0xee, 0xb8, 0x14, 0xde, 0x5e, 0x0b, 0xdb,
        0xe0, 0x32, 0x3a, 0x0a, 0x49, 0x06, 0x24, 0x5c, 0xc2, 0xd3, 0xac, 0x62, 0x91, 0x95, 0xe4, 0x79,
        0xe7, 0xc8, 0x37, 0x6d, 0x8d, 0xd5, 0x4e, 0xa9, 0x6c, 0x56, 0xf4, 0xea, 0x65, 0x7a, 0xae, 0x08,
        0xba, 0x78, 0x25, 0x2e, 0x1c, 0xa6, 0xb4, 0xc6, 0xe8, 0xdd, 0x74, 0x1f, 0x4b, 0xbd, 0x8b, 0x8a,
        0x70, 0x3e, 0xb5, 0x66, 0x48, 0x03, 0xf6, 0x0e, 0x61, 0x35, 0x57, 0xb9, 0x86, 0xc1, 0x1d, 0x9e,
        0xe1, 0xf8, 0x98, 0x11, 0x69, 0xd9, 0x8e, 0x94, 0x9b, 0x1e, 0x87, 0xe9, 0xce, 0x55, 0x28, 0xdf,
        0x8c, 0xa1, 0x89, 0x0d, 0xbf, 0xe6, 0x42, 0x68, 0x41, 0x99, 0x2d, 0x0f, 0xb0, 0x54, 0xbb, 0x16
    ];
}
