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

public static partial class CommandSuggestionCodecs
{
    /// <summary>Legacy 1.8 tab complete (47).</summary>
    public static readonly PacketCodec<ClientboundLegacyTabCompletePacket> LegacyTabCompleteV1_8 =
        PacketCodec<ClientboundLegacyTabCompletePacket>.Of(
            static (ref PacketWriter w, ClientboundLegacyTabCompletePacket p, PacketCodecContext _) =>
                w.WriteList(p.Matches, static (ref PacketWriter sw, string s) => sw.WriteString(s)),
            static (ref PacketReader r, PacketCodecContext _) =>
                new ClientboundLegacyTabCompletePacket(r.ReadList(static (ref PacketReader sr) => sr.ReadString())));

    /// <summary>Legacy 1.8 tab complete request (47).</summary>
    public static readonly PacketCodec<ServerboundLegacyTabCompletePacket> ServerLegacyTabCompleteV1_8 =
        PacketCodec<ServerboundLegacyTabCompletePacket>.Of(
            static (ref PacketWriter w, ServerboundLegacyTabCompletePacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Text);
                w.WriteOptionalStruct(p.LookedAtBlock, static (ref PacketWriter sw, BlockPos pos) => sw.WriteBlockPos(pos, BlockPosLayout.PrePacked114));
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                string text = r.ReadString();
                BlockPos? block = r.ReadBool() ? r.ReadBlockPos(BlockPosLayout.PrePacked114) : null;
                return new ServerboundLegacyTabCompletePacket(text, block, AssumeCommand: null);
            });

    /// <summary>Legacy tab complete request, 107-340 (1.9-1.12.2): the 1.8 body with an <c>assumeCommand</c> BOOL inserted between the text and the optional looked-at block.</summary>
    /// <remarks>The 1.9 form inserts <c>assumeCommand</c> between the text and the optional block; 1.8.4 goes straight from the text to the has-block bool. Sending the 1.8 form here makes the server read the has-block bool as <c>assumeCommand</c> and then run off the end of the frame.</remarks>
    public static readonly PacketCodec<ServerboundLegacyTabCompletePacket> ServerLegacyTabCompleteV1_9 =
        PacketCodec<ServerboundLegacyTabCompletePacket>.Of(
            static (ref PacketWriter w, ServerboundLegacyTabCompletePacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.Text);
                w.WriteBool(p.AssumeCommand ?? throw new ProtocolViolationException("A 1.9-1.12.2 tab-complete request requires the assume-command flag."));
                w.WriteOptionalStruct(p.LookedAtBlock, static (ref PacketWriter sw, BlockPos pos) => sw.WriteBlockPos(pos, BlockPosLayout.PrePacked114));
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                string text = r.ReadString();
                bool assumeCommand = r.ReadBool();
                BlockPos? block = r.ReadBool() ? r.ReadBlockPos(BlockPosLayout.PrePacked114) : null;
                return new ServerboundLegacyTabCompletePacket(text, block, assumeCommand);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareTabComplete(PacketBindings bindings)
    {
        bindings.Packet(UiPackets.Clientbound.LegacyTabComplete)
            .From(JavaProtocols.V1_8, CommandSuggestionCodecs.LegacyTabCompleteV1_8);

        bindings.Packet(UiPackets.Serverbound.LegacyTabComplete)
            .From(JavaProtocols.V1_8, CommandSuggestionCodecs.ServerLegacyTabCompleteV1_8);
    }
}
