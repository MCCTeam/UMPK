using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Login;

/// <summary>The last-death block inside <c>CommonPlayerSpawnInfo</c> is an optional dimension resource key and block position. The key names where the player died and need not equal the dimension they are joining.</summary>
public sealed class JoinGameLastDeathTests
{
    // A 1.20.5-1.21.1 (766/767) minecraft:login frame, authored field by field rather than produced by the encoder under test: a player joining the overworld whose last death was in the nether.
    private static readonly byte[] Frame766 =
    [
        0x00, 0x00, 0x00, 0x07,                                     // player id 7
        0x00,                                                       // hardcore false
        0x01,                                                       // dimension list: one entry
        0x13, 0x6D, 0x69, 0x6E, 0x65, 0x63, 0x72, 0x61, 0x66, 0x74, //   "minecraft:overworld"
        0x3A, 0x6F, 0x76, 0x65, 0x72, 0x77, 0x6F, 0x72, 0x6C, 0x64,
        0x14,                                                       // max players 20
        0x0A,                                                       // view distance 10
        0x0A,                                                       // simulation distance 10
        0x00,                                                       // reduced debug info false
        0x01,                                                       // show death screen true
        0x00,                                                       // do limited crafting false
        0x00,                                                       // dimension type registry id 0
        0x13, 0x6D, 0x69, 0x6E, 0x65, 0x63, 0x72, 0x61, 0x66, 0x74, // dimension "minecraft:overworld"
        0x3A, 0x6F, 0x76, 0x65, 0x72, 0x77, 0x6F, 0x72, 0x6C, 0x64,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,             // seed 0
        0x00,                                                       // game type 0 (survival)
        0xFF,                                                       // previous game type -1 (none)
        0x00,                                                       // is debug false
        0x00,                                                       // is flat false
        0x01,                                                       // last death present
        0x14, 0x6D, 0x69, 0x6E, 0x65, 0x63, 0x72, 0x61, 0x66, 0x74, //   "minecraft:the_nether"
        0x3A, 0x74, 0x68, 0x65, 0x5F, 0x6E, 0x65, 0x74, 0x68, 0x65,
        0x72,
        0x00, 0x00, 0x19, 0x3F, 0xFF, 0xF3, 0x80, 0x40,             //   packed pos x=100 y=64 z=-200
        0x00,                                                       // portal cooldown 0
        0x01,                                                       // enforces secure chat true
    ];

    // The same join, one era later (768+): the spawn info gains a trailing sea-level VarInt, and the 1.21.5 member carries no online-mode flag.
    private static readonly byte[] Frame770 =
    [
        .. Frame766.AsSpan(0, Frame766.Length - 1),                 // everything through the portal cooldown
        0x3F,                                                       // sea level 63
        0x01,                                                       // enforces secure chat true
    ];

    /// <summary>The 766/767 member. The nether key is part of the decoded value, and the frame re-encodes to the bytes it came from; writing the CURRENT dimension back would silently move a player's death marker to the overworld and change the frame.</summary>
    [Fact]
    public void V1_20_5_KeepsTheDimensionThePlayerDiedIn()
    {
        ClientboundLoginPacket decoded = CodecRoundTrip.Decode(JoinGameCodecs.V1_20_5, Frame766);

        Assert.Equal("minecraft:the_nether", decoded.SpawnInfo.LastDeathDimension);
        Assert.Equal("minecraft:overworld", decoded.SpawnInfo.Dimension);
        Assert.NotNull(decoded.SpawnInfo.LastDeathDimensionAndPos);
        Assert.Equal(Frame766, CodecRoundTrip.Encode(JoinGameCodecs.V1_20_5, decoded));
    }

    /// <summary>The 1.21.5 member, whose spawn info ends in the sea level.</summary>
    [Fact]
    public void V1_21_5_KeepsTheDimensionThePlayerDiedIn()
    {
        ClientboundLoginPacket decoded = CodecRoundTrip.Decode(JoinGameCodecs.V1_21_2, Frame770);

        Assert.Equal("minecraft:the_nether", decoded.SpawnInfo.LastDeathDimension);
        Assert.Equal(63, decoded.SpawnInfo.SeaLevel);
        Assert.Equal(Frame770, CodecRoundTrip.Encode(JoinGameCodecs.V1_21_2, decoded));
    }

    /// <summary>The cross-era clause, without which the two facts above pass on a round trip through one wrong codec. 766 opens its spawn info with a registry id and 764/765 with a resource-key string, so the 764 member reading this frame takes the 0 id as a zero-length dimension type name and every field after it slides: it must not agree with the frame it was not written for.</summary>
    [Fact]
    public void V1_20_2_RejectsThe766Frame()
    {
        bool agreed;
        try
        {
            var reader = new PacketReader(Frame766);
            ClientboundLoginPacket decoded =
                JoinGameCodecs.V1_20_2.Decode(ref reader, PacketCodecContext.Registryless);
            agreed = reader.Remaining == 0
                && decoded.SpawnInfo.Dimension == "minecraft:overworld"
                && decoded.SpawnInfo.LastDeathDimension == "minecraft:the_nether";
        }
        catch (ProtocolViolationException)
        {
            agreed = false;
        }

        Assert.False(agreed, "the 764/765 member decoded a 766 frame as if it were its own.");
    }

    /// <summary>The 1.19-1.20.1 member carries the same field with a plain long position instead of a packed block position. The key must be a valid resource location; an empty string makes a decoded frame fail re-encoding.</summary>
    [Fact]
    public void V1_20_WritesBackTheDimensionItRead()
    {
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0,
            Dimension: "minecraft:overworld",
            Seed: 0,
            GameType: 0,
            PreviousGameType: -1,
            IsDebug: false,
            IsFlat: false,
            LastDeathDimensionAndPos: 1234L,
            PortalCooldown: 0,
            SeaLevel: 0)
        {
            DimensionTypeName = "minecraft:overworld",
            LastDeathDimension = "minecraft:the_nether",
        };

        var packet = new ClientboundLoginPacket(
            PlayerId: 7, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 10, SimulationDistance: 10, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false,
            Legacy: null);

        ClientboundLoginPacket back = CodecRoundTrip.Cycle(JoinGameCodecs.V1_20, packet);

        Assert.Equal("minecraft:the_nether", back.SpawnInfo.LastDeathDimension);
    }
}
