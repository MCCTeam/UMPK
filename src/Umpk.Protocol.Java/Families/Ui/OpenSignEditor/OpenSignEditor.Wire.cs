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

public static partial class UiMiscCodecs
{
    /// <summary>Open sign editor (770/776).</summary>
    public static readonly PacketCodec<ClientboundOpenSignEditorPacket> OpenSignEditorV1_14 =
        PacketCodec<ClientboundOpenSignEditorPacket>.Of(
            static (ref PacketWriter w, ClientboundOpenSignEditorPacket p, PacketCodecContext _) =>
            {
                w.WriteBlockPos(p.Pos, BlockPosLayout.Packed114);
                w.WriteBool(p.IsFrontText);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundOpenSignEditorPacket(r.ReadBlockPos(BlockPosLayout.Packed114), r.ReadBool()));

    /// <summary>Open sign editor (47): a block position only (no front-text flag).</summary>
    public static readonly PacketCodec<ClientboundOpenSignEditorPacket> OpenSignEditorV1_8 =
        PacketCodec<ClientboundOpenSignEditorPacket>.Of(
            static (ref PacketWriter w, ClientboundOpenSignEditorPacket p, PacketCodecContext _) =>
                w.WriteBlockPos(p.Pos, BlockPosLayout.PrePacked114),
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundOpenSignEditorPacket(r.ReadBlockPos(BlockPosLayout.PrePacked114), true));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareOpenSignEditor(PacketBindings bindings)
    {
        // A bare pre-1.14 packed block position from 47 to 404; the front/back flag is a 1.20 addition and the pos packing moves at 1.14.
        bindings.Packet(UiPackets.Clientbound.OpenSignEditor)
            .From(JavaProtocols.V1_8, UiMiscCodecs.OpenSignEditorV1_8)
            .From(JavaProtocols.V1_14, UiMiscCodecs.OpenSignEditorV1_14);
    }
}
