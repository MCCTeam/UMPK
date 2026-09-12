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
    /// <summary>Command suggestions, 765+ (1.20.3 onward): the Brigadier form whose optional tooltip is a NETWORK-NBT component. Bound from 765, which is where vanilla moved the field. Protocols 393-764 carry the JSON-string tooltip and take <see cref="CommandSuggestionsV1_13"/>.</summary>
    public static readonly PacketCodec<ClientboundCommandSuggestionsPacket> CommandSuggestionsV1_20_3 =
        PacketCodec<ClientboundCommandSuggestionsPacket>.Of(
            static (ref PacketWriter w, ClientboundCommandSuggestionsPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.TransactionId);
                w.WriteVarInt(p.RangeStart);
                w.WriteVarInt(p.RangeLength);
                w.WriteList(p.Suggestions, (ref PacketWriter sw, CommandSuggestion s) =>
                {
                    sw.WriteString(s.Text);
                    sw.WriteOptional(s.Tooltip, WriteModernComponent);
                });
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int tx = r.ReadVarInt();
                int start = r.ReadVarInt();
                int length = r.ReadVarInt();
                CommandSuggestion[] suggestions = r.ReadList((ref PacketReader sr) =>
                    new CommandSuggestion(sr.ReadString(), sr.ReadOptional(ReadModernComponent)));
                return new ClientboundCommandSuggestionsPacket(tx, start, length, suggestions);
            });

    /// <summary>Command suggestions, 393-764 (1.13 through 1.20.2): the Brigadier form arrives at the flattening, with a transaction id, the replaced range, and a list of suggestions whose optional tooltip is a JSON-STRING component (the modern codec reads network NBT there, which is why it cannot be reused).</summary>
    /// <remarks>The framing is stable through 1.20.2; only the tooltip encoding moves at 1.20.3. Protocols 107-340 instead carry the pre-Brigadier list of match strings.</remarks>
    public static readonly PacketCodec<ClientboundCommandSuggestionsPacket> CommandSuggestionsV1_13 =
        PacketCodec<ClientboundCommandSuggestionsPacket>.Of(
            static (ref PacketWriter w, ClientboundCommandSuggestionsPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt(p.TransactionId);
                w.WriteVarInt(p.RangeStart);
                w.WriteVarInt(p.RangeLength);
                w.WriteList(p.Suggestions, static (ref PacketWriter sw, CommandSuggestion s) =>
                {
                    sw.WriteString(s.Text);
                    sw.WriteOptional(s.Tooltip, WriteLegacyComponent);
                });
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                int tx = r.ReadVarInt();
                int start = r.ReadVarInt();
                int length = r.ReadVarInt();
                CommandSuggestion[] suggestions = r.ReadList(static (ref PacketReader sr) =>
                    new CommandSuggestion(sr.ReadString(), sr.ReadOptional(ReadLegacyComponent)));
                return new ClientboundCommandSuggestionsPacket(tx, start, length, suggestions);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareCommandSuggestions(PacketBindings bindings)
    {
        // Two different packets share the modern identifier across this range. 107-340 carry the PRE-BRIGADIER form (a VarInt-counted list of match strings), which is the same wire the 1.8 dataset registers under minecraft:tab_complete, so that record and codec are bound here for those ten protocols. The modern record cannot stand in: it has a transaction id and a replace range the pre-Brigadier wire simply does not carry, and the client's completion service is request/response over that transaction id, so it does not apply below 1.13 at all. 393-764 carry the Brigadier form with JSON-STRING tooltips, and 765+ the same shape with network-NBT tooltips.
        //
        // The tooltip era boundary is 1.20.3 (765), not 1.14 (477): earlier forms use JSON strings and later forms use network NBT. Binding the NBT codec from 477 made the NBT reader take a JSON string's VarInt length prefix as a tag type, so a real 1.16.5 suggestion frame carrying the tooltip {"translate":"argument.entity.selector.allPlayers"} (51 bytes, length prefix 0x33) died with "Unknown NBT tag type 51". The default DecodeFailurePolicy is FailConnection, so that did not merely lose the suggestions: it killed the session, on every protocol from 477 to 764, the first time an operator pressed Tab on an argument the server answers.
        bindings.Packet(UiPackets.Clientbound.CommandSuggestions)
            .FromAs(JavaEras.Combat, UiPackets.Clientbound.LegacyTabComplete, CommandSuggestionCodecs.LegacyTabCompleteV1_8)
            .From(JavaEras.Flattening, CommandSuggestionCodecs.CommandSuggestionsV1_13)
            .From(JavaEras.ComponentNbtTransport, CommandSuggestionCodecs.CommandSuggestionsV1_20_3);
    }
}
