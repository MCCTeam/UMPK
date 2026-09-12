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
    /// <summary>Command suggestion request (770/776).</summary>
    public static readonly PacketCodec<ServerboundCommandSuggestionPacket> ServerCommandSuggestionV1_13 =
        PacketCodec<ServerboundCommandSuggestionPacket>.Of(
            static (ref PacketWriter w, ServerboundCommandSuggestionPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.TransactionId);
                w.WriteString(p.Command, 32500);
            },
            static (ref PacketReader r, PacketCodecContext _) =>
                new ServerboundCommandSuggestionPacket(r.ReadVarInt(), r.ReadString(32500)));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareCommandSuggestion(PacketBindings bindings)
    {
        // Mirror of the clientbound split: 107-340 send the pre-Brigadier request (text, assume-command bool, optional looked-at block) under the modern identifier, and 393+ send the Brigadier (transaction id, command) pair. The 1.13 body is already the modern one: the 1.13.2 serverbound packet reads readVarInt() then readUtf(32500), the same two fields and the same cap the 1.21.5 codec uses.
        bindings.Packet(UiPackets.Serverbound.CommandSuggestion)
            .FromAs(JavaEras.Combat, UiPackets.Serverbound.LegacyTabComplete, CommandSuggestionCodecs.ServerLegacyTabCompleteV1_9)
            .From(JavaEras.Flattening, CommandSuggestionCodecs.ServerCommandSuggestionV1_13);
    }
}
