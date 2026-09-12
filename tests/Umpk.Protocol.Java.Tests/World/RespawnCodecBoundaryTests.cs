using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.World;

/// <summary><c>minecraft:respawn</c> across its ten era shapes. This packet arrives on every death and every dimension change, so a wrong shape is session-fatal the first time either happens. The <c>CommonPlayerSpawnInfo</c> block does not exist on the wire before 1.20.2.</summary>
/// <remarks>
/// <para>Three kinds of assertion cover the distinct failure modes.</para>
/// <list type="bullet">
/// <item><b>Literal frames through the BOUND codec.</b> A round-trip test cannot catch a misbinding:
/// encode and decode through the same wrong codec agree with each other. These frames are hand-built byte by byte from the wire fields and resolved by protocol number, so the timeline itself is under test.</item>
/// <item><b>Frame LENGTH.</b> One canonical packet encoded at one protocol per band, pinned to the exact
/// byte count that era writes. A rebinding to a neighbouring era moves this even when both codecs happen to accept the same bytes.</item>
/// <item><b>Cross-era rejection.</b> Each band's frame is offered to a neighbour's codec and must fault
/// or re-encode differently.</item>
/// </list>
/// <para>The literal frames cover every distinct respawn wire shape from protocol 477 through 776.</para>
/// </remarks>
public sealed class RespawnCodecBoundaryTests
{
    /// <summary>Every supported protocol, so a band boundary cannot be moved without this noticing.</summary>
    public static readonly int[] AllProtocols =
    [
        47, 107, 108, 109, 110, 210, 315, 316, 335, 338, 340, 393, 401, 404,
        477, 480, 485, 490, 498, 573, 575, 578,
        735, 736, 751, 753, 754, 755, 756, 757, 758,
        759, 760, 761, 762, 763, 764, 765, 766, 767,
        768, 769, 770, 771, 772, 773, 774, 775, 776,
    ];

    /// <summary>The literal table's protocol column, read by <c>AllProtocolTableCoverageTests</c>.</summary>
    public static IReadOnlyList<int> Protocols() => AllProtocols;

    private const string Nether = "minecraft:the_nether";
    private const long Seed = 0x0102030405060708L;

    // Literal-frame helpers. Reusing the production writer would let a wrong codec agree with itself.

    private static byte[] VarInt(int value)
    {
        var bytes = new List<byte>();
        uint v = (uint)value;
        do
        {
            byte b = (byte)(v & 0x7F);
            v >>= 7;
            bytes.Add(v != 0 ? (byte)(b | 0x80) : b);
        }
        while (v != 0);
        return [.. bytes];
    }

    private static byte[] Str(string s)
    {
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(s);
        return [.. VarInt(utf8.Length), .. utf8];
    }

    private static byte[] I32(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    private static byte[] I64(long v)
    {
        var b = new byte[8];
        for (int i = 0; i < 8; i++)
            b[i] = (byte)(v >> (56 - (8 * i)));

        return b;
    }

    /// <summary>1.14 block-position packing: x in the top 26 bits, z in the next 26, y in the low 12.</summary>
    private static byte[] Pos114(int x, int y, int z) =>
        I64(((long)(x & 0x3FFFFFF) << 38) | ((long)(z & 0x3FFFFFF) << 12) | (y & 0xFFFL));

    /// <summary>A named-root NBT compound carrying one string field, built byte by byte.</summary>
    private static byte[] NamedRootCompound(string key, string value)
    {
        byte[] k = System.Text.Encoding.UTF8.GetBytes(key);
        byte[] v = System.Text.Encoding.UTF8.GetBytes(value);
        var bytes = new List<byte>
        {
            0x0A, 0x00, 0x00,                                 // TAG_Compound, empty root name
            0x08, (byte)(k.Length >> 8), (byte)k.Length,       // TAG_String, key length
        };
        bytes.AddRange(k);
        bytes.Add((byte)(v.Length >> 8));
        bytes.Add((byte)v.Length);
        bytes.AddRange(v);
        bytes.Add(0x00);                                      // TAG_End
        return [.. bytes];
    }

    private static byte[] Cat(params byte[][] parts)
    {
        var all = new List<byte>();
        foreach (byte[] p in parts)
            all.AddRange(p);

        return [.. all];
    }

    private static BoundPacketCodec Respawn(int protocol) =>
        BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:respawn");

    private static ClientboundRespawnPacket Decode(int protocol, byte[] frame) =>
        Assert.IsType<ClientboundRespawnPacket>(Respawn(protocol).DecodeFrame(frame));

    /// <summary>Asserts a frame does NOT survive a codec: it either faults or re-encodes differently.</summary>
    private static void AssertRejects(int protocol, byte[] frame, string because)
    {
        BoundPacketCodec bound = Respawn(protocol);
        object decoded;
        try
        {
            decoded = bound.DecodeFrame(frame);
        }
        catch (Exception)
        {
            return; // faulted, which is the honest outcome
        }

        Assert.False(frame.SequenceEqual(bound.Encode(decoded)), because);
    }

    // Respawn is bound on every protocol, and its era codec differs across the band boundaries.

    [Theory]
    [MemberData(nameof(EveryProtocol))]
    public void Respawn_IsBound_OnEveryProtocol(int protocol) =>
        Assert.True(Respawn(protocol).IsImplemented);

    public static TheoryData<int> EveryProtocol
    {
        get
        {
            var data = new TheoryData<int>();
            foreach (int p in AllProtocols)
                data.Add(p);

            return data;
        }
    }

    // Literal frames, one per era, decoded through the bound codec and asserted field by field.

    /// <summary>47-404: int dimension, difficulty byte, gamemode byte, level-type string.</summary>
    private static byte[] Frame47() => Cat(I32(-1), [2], [1], Str("flat"));

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(340)]
    [InlineData(404)]
    public void Respawn_UsesDifficultyByte(int protocol)
    {
        byte[] frame = Frame47();
        ClientboundRespawnPacket p = Decode(protocol, frame);
        Assert.Null(p.SpawnInfo);
        Assert.Equal(-1, p.Legacy!.Dimension);
        Assert.Equal(2, p.Legacy.Difficulty);
        Assert.Equal(1, p.Legacy.GameMode);
        Assert.Equal("flat", p.Legacy.LevelType);
        Assert.Equal(frame, Respawn(protocol).Encode(p));
    }

    /// <summary>477-498: the difficulty byte is gone; the remaining prefix is int, byte, utf16.</summary>
    private static byte[] Frame477() => Cat(I32(-1), [1], Str("flat"));

    [Theory]
    [InlineData(477)]
    [InlineData(498)]
    public void Respawn_OmitsDifficultyByte(int protocol)
    {
        byte[] frame = Frame477();
        ClientboundRespawnPacket p = Decode(protocol, frame);
        Assert.Equal(-1, p.Legacy!.Dimension);
        Assert.Equal(0, p.Legacy.Difficulty);
        Assert.Equal(1, p.Legacy.GameMode);
        Assert.Equal(0L, p.Legacy.Seed);
        Assert.Equal("flat", p.Legacy.LevelType);
        Assert.Equal(frame, Respawn(protocol).Encode(p));
    }

    /// <summary>573-578: a hashed world seed follows the dimension id.</summary>
    private static byte[] Frame573() => Cat(I32(1), I64(Seed), [3], Str("largeBiomes"));

    [Theory]
    [InlineData(573)]
    [InlineData(578)]
    public void Respawn_IncludesHashedSeed(int protocol)
    {
        byte[] frame = Frame573();
        ClientboundRespawnPacket p = Decode(protocol, frame);
        Assert.Equal(1, p.Legacy!.Dimension);
        Assert.Equal(Seed, p.Legacy.Seed);
        Assert.Equal(3, p.Legacy.GameMode);
        Assert.Equal("largeBiomes", p.Legacy.LevelType);
        Assert.Equal(frame, Respawn(protocol).Encode(p));
    }

    /// <summary>735/736: two resource-key strings, seed, two gamemode bytes, three bools.</summary>
    private static byte[] Frame735() =>
        Cat(Str("minecraft:overworld"), Str(Nether), I64(Seed), [1], [0], [0], [1], [1]);

    [Theory]
    [InlineData(735)]
    [InlineData(736)]
    public void Respawn_UsesStringDimensionType(int protocol)
    {
        byte[] frame = Frame735();
        ClientboundRespawnPacket p = Decode(protocol, frame);
        Assert.Equal("minecraft:overworld", p.SpawnInfo!.DimensionTypeName);
        Assert.Equal(Nether, p.SpawnInfo.Dimension);
        Assert.Equal(Seed, p.SpawnInfo.Seed);
        Assert.Equal(1, p.SpawnInfo.GameType);
        Assert.Equal(0, p.SpawnInfo.PreviousGameType);
        Assert.False(p.SpawnInfo.IsDebug);
        Assert.True(p.SpawnInfo.IsFlat);
        Assert.Equal(1, p.DataToKeep);
        Assert.Null(p.DimensionTypeNbt);
        Assert.Equal(frame, Respawn(protocol).Encode(p));
    }

    /// <summary>751-758: the dimension type becomes an inline named-root NBT compound.</summary>
    private static byte[] Frame751() =>
        Cat(
            NamedRootCompound("effects", "minecraft:the_nether"),
            Str(Nether), I64(Seed), [1], [0], [0], [1], [1]);

    [Theory]
    [InlineData(751)]
    [InlineData(754)]
    [InlineData(755)]
    [InlineData(758)]
    public void Respawn_UsesInlineDimensionTag(int protocol)
    {
        byte[] frame = Frame751();
        ClientboundRespawnPacket p = Decode(protocol, frame);
        Assert.Equal(
            "minecraft:the_nether",
            Assert.IsType<NbtCompound>(p.DimensionTypeNbt).GetString("effects"));
        Assert.Null(p.SpawnInfo!.DimensionTypeName);
        Assert.Equal(Nether, p.SpawnInfo.Dimension);
        Assert.Equal(Seed, p.SpawnInfo.Seed);
        Assert.Equal(1, p.DataToKeep);
        Assert.Equal(frame, Respawn(protocol).Encode(p));
    }

    /// <summary>759-762: string dimension type again, then the optional last-death GlobalPos.</summary>
    private static byte[] Frame759(bool hasLastDeath) =>
        hasLastDeath
            ? Cat(
                Str("minecraft:overworld"), Str(Nether), I64(Seed), [1], [0], [0], [1], [3],
                [1], Str("minecraft:overworld"), Pos114(100, 64, -200))
            : Cat(Str("minecraft:overworld"), Str(Nether), I64(Seed), [1], [0], [0], [1], [3], [0]);

    [Theory]
    [InlineData(759)]
    [InlineData(760)]
    [InlineData(761)]
    [InlineData(762)]
    public void Respawn_OmitsPortalCooldown(int protocol)
    {
        byte[] frame = Frame759(hasLastDeath: true);
        ClientboundRespawnPacket p = Decode(protocol, frame);
        Assert.Equal("minecraft:overworld", p.SpawnInfo!.DimensionTypeName);
        Assert.Equal(Nether, p.SpawnInfo.Dimension);
        Assert.Equal(3, p.DataToKeep);
        Assert.Equal("minecraft:overworld", p.SpawnInfo.LastDeathDimension);
        Assert.NotNull(p.SpawnInfo.LastDeathDimensionAndPos);
        Assert.Equal(0, p.SpawnInfo.PortalCooldown);
        Assert.Equal(frame, Respawn(protocol).Encode(p));
    }

    /// <summary>763: the 1.19 shape plus a trailing portal-cooldown VarInt.</summary>
    private static byte[] Frame763() => Cat(Frame759(hasLastDeath: false), VarInt(300));

    [Fact]
    public void Respawn_AppendsPortalCooldown()
    {
        byte[] frame = Frame763();
        ClientboundRespawnPacket p = Decode(763, frame);
        Assert.Equal(Nether, p.SpawnInfo!.Dimension);
        Assert.Equal(300, p.SpawnInfo.PortalCooldown);
        Assert.Null(p.SpawnInfo.LastDeathDimensionAndPos);
        Assert.Equal(3, p.DataToKeep);
        Assert.Equal(frame, Respawn(763).Encode(p));
    }

    /// <summary>764/765: CommonPlayerSpawnInfo (string dimension type), then the data-to-keep byte.</summary>
    private static byte[] Frame764() =>
        Cat(
            Str("minecraft:overworld"), Str(Nether), I64(Seed), [1], [0], [0], [1],
            [0], VarInt(300), [3]);

    [Theory]
    [InlineData(764)]
    [InlineData(765)]
    public void Respawn_PlacesKeepDataAtTail(int protocol)
    {
        byte[] frame = Frame764();
        ClientboundRespawnPacket p = Decode(protocol, frame);
        Assert.Equal("minecraft:overworld", p.SpawnInfo!.DimensionTypeName);
        Assert.Equal(-1, p.SpawnInfo.DimensionTypeId);
        Assert.Equal(Nether, p.SpawnInfo.Dimension);
        Assert.Equal(300, p.SpawnInfo.PortalCooldown);
        Assert.Equal(0, p.SpawnInfo.SeaLevel);
        Assert.Equal(3, p.DataToKeep);
        Assert.Equal(frame, Respawn(protocol).Encode(p));
    }

    /// <summary>766/767: the dimension type becomes a plain VarInt registry id, still no sea level.</summary>
    private static byte[] Frame766(int dimensionTypeId) =>
        Cat(VarInt(dimensionTypeId), Str(Nether), I64(Seed), [1], [0], [0], [1], [0], VarInt(300), [3]);

    [Theory]
    [InlineData(766)]
    [InlineData(767)]
    public void Respawn_UsesVariableDimensionTypeWithoutSeaLevel(int protocol)
    {
        byte[] frame = Frame766(4);
        ClientboundRespawnPacket p = Decode(protocol, frame);
        Assert.Equal(4, p.SpawnInfo!.DimensionTypeId);
        Assert.Null(p.SpawnInfo.DimensionTypeName);
        Assert.Equal(Nether, p.SpawnInfo.Dimension);
        Assert.Equal(300, p.SpawnInfo.PortalCooldown);
        Assert.Equal(0, p.SpawnInfo.SeaLevel);
        Assert.Equal(3, p.DataToKeep);
        Assert.Equal(frame, Respawn(protocol).Encode(p));
    }

    /// <summary>768-776: the 766 shape plus the trailing sea-level VarInt.</summary>
    private static byte[] Frame768(int dimensionTypeId) =>
        Cat(
            VarInt(dimensionTypeId), Str(Nether), I64(Seed), [1], [0], [0], [1], [0],
            VarInt(300), VarInt(63), [3]);

    [Theory]
    [InlineData(768)]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(773)]
    [InlineData(776)]
    public void Respawn_AppendsSeaLevel(int protocol)
    {
        byte[] frame = Frame768(4);
        ClientboundRespawnPacket p = Decode(protocol, frame);
        Assert.Equal(4, p.SpawnInfo!.DimensionTypeId);
        Assert.Equal(Nether, p.SpawnInfo.Dimension);
        Assert.Equal(300, p.SpawnInfo.PortalCooldown);
        Assert.Equal(63, p.SpawnInfo.SeaLevel);
        Assert.Equal(3, p.DataToKeep);
        Assert.Equal(frame, Respawn(protocol).Encode(p));
    }

    /// <summary>The dimension type is a PLAIN registry id, not the <c>holder(...)</c> scheme where a leading 0 introduces an inline value. The overworld's id is commonly 0, and reading that as "inline" made the decoder parse the dimension resource key's own length prefix as an NBT tag type: a respawn in the overworld reported <c>Unknown NBT tag type 19</c> and ended the session, 19 being the length of <c>minecraft:overworld</c>.</summary>
    [Theory]
    [InlineData(766)]
    [InlineData(768)]
    [InlineData(776)]
    public void ADimensionTypeIdOfZero_IsARegistryId_NotAnInlineNbtValue(int protocol)
    {
        byte[] frame = protocol < 768 ? Frame766(0) : Frame768(0);
        ClientboundRespawnPacket p = Decode(protocol, frame);
        Assert.Equal(0, p.SpawnInfo!.DimensionTypeId);
        Assert.Equal(Nether, p.SpawnInfo.Dimension);
        Assert.Equal(frame, Respawn(protocol).Encode(p));
    }

    // Frame LENGTH. One canonical packet per band, pinned to the byte count that era writes. A
    //    rebinding moves this even where two eras accept each other's bytes.

    private static readonly LegacyRespawnFields CanonicalLegacy =
        new(-1, 2, 1, "flat") { Seed = Seed };

    private static readonly CommonPlayerSpawnInfo CanonicalSpawn = new(
        DimensionTypeId: 0,
        Dimension: Nether,
        Seed: Seed,
        GameType: 1,
        PreviousGameType: 0,
        IsDebug: false,
        IsFlat: true,
        LastDeathDimensionAndPos: null,
        PortalCooldown: 300,
        SeaLevel: 63)
    {
        DimensionTypeName = "minecraft:overworld",
    };

    [Theory]
    [InlineData(47, 11)]    // i32 + difficulty + gamemode + str("flat")
    [InlineData(404, 11)]
    [InlineData(477, 10)]   // the difficulty byte is gone
    [InlineData(498, 10)]
    [InlineData(573, 18)]   // i32 + i64 seed + gamemode + str("flat")
    [InlineData(578, 18)]
    public void LegacyBands_EncodeToTheirOwnExactWidth(int protocol, int expected)
    {
        var packet = new ClientboundRespawnPacket(SpawnInfo: null, DataToKeep: 0, CanonicalLegacy);
        Assert.Equal(expected, Respawn(protocol).Encode(packet).Length);
    }

    [Theory]
    [InlineData(735, 54)]   // str(19) + str(20) + i64 + 2 bytes + 3 bools
    [InlineData(736, 54)]
    [InlineData(751, 70)]   // nbt(36) + str(20) + i64 + 2 bytes + 3 bools
    [InlineData(758, 70)]
    [InlineData(759, 55)]   // str(19) + str(20) + i64 + 2 bytes + 2 bools + keep byte + absent optional
    [InlineData(762, 55)]
    [InlineData(763, 57)]   // ... + varint(300) portal cooldown, two bytes wide
    [InlineData(764, 57)]   // str dimension type, absent optional, varint cooldown, keep byte
    [InlineData(765, 57)]
    [InlineData(766, 38)]   // varint dimension type replaces the 19-char string
    [InlineData(767, 38)]
    [InlineData(768, 39)]   // ... + varint(63) sea level
    [InlineData(776, 39)]
    public void ModernBands_EncodeToTheirOwnExactWidth(int protocol, int expected)
    {
        var nbt = new NbtCompound();
        nbt.PutString("effects", Nether);
        var packet = new ClientboundRespawnPacket(CanonicalSpawn, DataToKeep: 1, Legacy: null)
        {
            DimensionTypeNbt = nbt,
        };
        Assert.Equal(expected, Respawn(protocol).Encode(packet).Length);
    }

    // Cross-era rejection ensures no codec spans incompatible layouts.

    [Fact]
    public void TheModernCodec_CannotStandInForTheLegacyBands()
    {
        AssertRejects(768, Frame47(), "the 1.21.2 codec must not accept a 1.8 respawn frame");
        AssertRejects(768, Frame477(), "the 1.21.2 codec must not accept a 1.14 respawn frame");
        AssertRejects(768, Frame573(), "the 1.21.2 codec must not accept a 1.15 respawn frame");
        AssertRejects(768, Frame735(), "the 1.21.2 codec must not accept a 1.16 respawn frame");
        AssertRejects(768, Frame751(), "the 1.21.2 codec must not accept a 1.16.2 respawn frame");
        AssertRejects(768, Frame759(hasLastDeath: true), "the 1.21.2 codec must not accept a 1.19 respawn frame");
        AssertRejects(768, Frame763(), "the 1.21.2 codec must not accept a 1.20.1 respawn frame");
    }

    [Fact]
    public void EachBand_RejectsItsNeighboursFrame()
    {
        AssertRejects(47, Frame477(), "the 1.8 codec must not accept a 1.14 respawn frame");
        AssertRejects(477, Frame573(), "the 1.14 codec must not accept a 1.15 respawn frame");
        AssertRejects(573, Frame735(), "the 1.15 codec must not accept a 1.16 respawn frame");
        AssertRejects(735, Frame751(), "the 1.16 codec must not accept a 1.16.2 respawn frame");
        AssertRejects(751, Frame759(hasLastDeath: true), "the 1.16.2 codec must not accept a 1.19 respawn frame");
        AssertRejects(759, Frame763(), "the 1.19 codec must not accept a 1.20 respawn frame");
        AssertRejects(763, Frame764(), "the 1.20 codec must not accept a 1.20.2 respawn frame");
        AssertRejects(764, Frame766(4), "the 1.20.2 codec must not accept a 1.20.5 respawn frame");
        AssertRejects(766, Frame768(4), "the 1.20.5 codec must not accept a 1.21.2 respawn frame");
    }
}
