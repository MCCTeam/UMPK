using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.WorldCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class WorldStateCodecs
{
    /// <summary>1.8 server difficulty: a single difficulty byte (no locked flag).</summary>
    public static readonly PacketCodec<ClientboundChangeDifficultyPacket> ChangeDifficultyV1_8 =
        PacketCodec<ClientboundChangeDifficultyPacket>.Of(
            static (ref PacketWriter w, ClientboundChangeDifficultyPacket p, PacketCodecContext _) => w.WriteByte(p.Difficulty),
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundChangeDifficultyPacket(r.ReadByte(), Locked: false));

    /// <summary>1.21.5 change difficulty: difficulty byte + locked flag.</summary>
    public static readonly PacketCodec<ClientboundChangeDifficultyPacket> ChangeDifficultyV1_14 =
        PacketCodec<ClientboundChangeDifficultyPacket>.Of(
            static (ref PacketWriter w, ClientboundChangeDifficultyPacket p, PacketCodecContext _) =>
            {
                w.WriteByte(p.Difficulty);
                w.WriteBool(p.Locked);
            },
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundChangeDifficultyPacket(r.ReadByte(), r.ReadBool()));

    /// <summary>26.2 change difficulty: difficulty VarInt followed by the locked flag.</summary>
    public static readonly PacketCodec<ClientboundChangeDifficultyPacket> ChangeDifficultyV26_2 =
        PacketCodec<ClientboundChangeDifficultyPacket>.Of(
            static (ref PacketWriter w, ClientboundChangeDifficultyPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.Difficulty);
                w.WriteBool(p.Locked);
            },
            static (ref PacketReader r, PacketCodecContext _) => new ClientboundChangeDifficultyPacket((byte)r.ReadVarInt(), r.ReadBool()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareChangeDifficulty(PacketBindings bindings)
    {
        // A bare difficulty byte from 47 all the way to 404; the locked flag is a 1.14 addition.
        bindings.Packet(WorldPackets.Clientbound.ChangeDifficulty)
            .From(JavaProtocols.V1_8, WorldStateCodecs.ChangeDifficultyV1_8)
            .From(JavaProtocols.V1_14, WorldStateCodecs.ChangeDifficultyV1_14)
            .From(JavaProtocols.V26_2, WorldStateCodecs.ChangeDifficultyV26_2)
            .AliasedAs(Identifier.Minecraft("server_difficulty"));
    }
}
