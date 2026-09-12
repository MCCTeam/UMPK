using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ScoreboardCodecs
{
    /// <summary>Display objective, 107-756 (1.9-1.17.1): a display-slot BYTE then the objective name, capped at 16 chars. The slot only widens to a VarInt at 1.20.2 (764), where vanilla replaced the raw int with the <c>DisplaySlot</c> enum and <c>readById</c>; slot ids never leave 0..18, which is why the two forms agree byte for byte in practice and the mis-binding would have been quiet rather than loud.</summary>
    /// <remarks>The capped form is a byte slot followed by a 16-character string.</remarks>
    public static readonly PacketCodec<ClientboundSetDisplayObjectivePacket> SetDisplayObjectiveV1_9 =
        MakeSetDisplayObjective(16);

    /// <summary>Display objective, 757-763 (1.18-1.20.1): the same byte slot, but the objective name loses its 16-char cap. A longer name is what separates this era from the 1.9 member; the bytes are otherwise identical.</summary>
    /// <remarks>The uncapped form remains a byte slot followed by an uncapped string through 1.20.1; the <c>DisplaySlot</c> enum begins at 1.20.2.</remarks>
    public static readonly PacketCodec<ClientboundSetDisplayObjectivePacket> SetDisplayObjectiveV1_18 =
        MakeSetDisplayObjective(32767);

    private static PacketCodec<ClientboundSetDisplayObjectivePacket> MakeSetDisplayObjective(int nameCap) =>
        PacketCodec<ClientboundSetDisplayObjectivePacket>.Of(
            (ref PacketWriter w, ClientboundSetDisplayObjectivePacket p, PacketCodecContext _) =>
            {
                w.WriteByte((byte)p.Slot);
                w.WriteString(p.ObjectiveName, nameCap);
            },
            (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundSetDisplayObjectivePacket(r.ReadByte(), r.ReadString(nameCap)));

    /// <summary>Modern display objective (770/776).</summary>
    public static readonly PacketCodec<ClientboundSetDisplayObjectivePacket> SetDisplayObjectiveV1_20_2 =
        PacketCodec<ClientboundSetDisplayObjectivePacket>.Of(
            static (ref PacketWriter w, ClientboundSetDisplayObjectivePacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.Slot);
                w.WriteString(p.ObjectiveName);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundSetDisplayObjectivePacket(r.ReadVarInt(), r.ReadString()));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetDisplayObjective(PacketBindings bindings)
    {
        // 107-763 write a display-slot BYTE, not the VarInt the modern codec uses: the DisplaySlot enum whose id 764 writes with readById/writeById does not exist in any jar before 1.20.2 (the BELOW_NAME constant first appears there), and 1.19 still reads a plain byte. Quiet, because slot ids never leave 0..18 and a VarInt in that range is one byte. The name cap is the second boundary: 16 chars through 1.17.1, uncapped from 1.18.
        bindings.Packet(UiPackets.Clientbound.SetDisplayObjective)
            .From(JavaProtocols.V1_9, ScoreboardCodecs.SetDisplayObjectiveV1_9)
            .From(JavaProtocols.V1_18, ScoreboardCodecs.SetDisplayObjectiveV1_18)
            .From(JavaProtocols.V1_20_2, ScoreboardCodecs.SetDisplayObjectiveV1_20_2);
    }
}
