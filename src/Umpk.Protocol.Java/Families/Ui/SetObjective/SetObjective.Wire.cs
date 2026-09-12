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
    /// <summary>Set objective, 765-769 (1.20.3-1.21.4): the display name is network NBT and the optional score number format is present, but both components use the LEGACY interaction dialect. The whole Using the modern dialect here would write <c>click_event</c> where this era requires <c>clickEvent</c>. 1.21.5 renames these fields and dispatches the click event on <c>action</c>.</summary>
    public static readonly PacketCodec<ClientboundSetObjectivePacket> SetObjectiveV1_20_3 =
        MakeSetObjectiveModern(ComponentWireEra.Legacy);

    /// <summary>Modern set-objective (770+): the same frame with the modern interaction dialect.</summary>
    public static readonly PacketCodec<ClientboundSetObjectivePacket> SetObjectiveV1_21_5 =
        MakeSetObjectiveModern(ComponentWireEra.Modern);

    private static PacketCodec<ClientboundSetObjectivePacket> MakeSetObjectiveModern(ComponentWireEra era) =>
        PacketCodec<ClientboundSetObjectivePacket>.Of(
            (ref PacketWriter w, ClientboundSetObjectivePacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.ObjectiveName);
                w.WriteByte((byte)p.Mode);
                if (p.Mode is ScoreboardObjectiveMode.Add or ScoreboardObjectiveMode.Change)
                {
                    w.WriteComponent(
                        p.DisplayName ?? throw new ProtocolViolationException("Add/change objective requires a display name."),
                        era,
                        ModernNbt);
                    w.WriteVarInt((int)p.RenderType);
                    WriteOptionalNumberFormat(ref w, p.NumberFormat, era);
                }
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                string name = r.ReadString();
                var mode = (ScoreboardObjectiveMode)r.ReadByte();
                if (mode is ScoreboardObjectiveMode.Add or ScoreboardObjectiveMode.Change)
                {
                    Component display = r.ReadComponent(era, ModernNbt);
                    var render = (ObjectiveRenderType)r.ReadVarInt();
                    ScoreNumberFormat? nf = ReadOptionalNumberFormat(ref r, era);
                    return new ClientboundSetObjectivePacket(name, mode, display, render, nf);
                }

                return new ClientboundSetObjectivePacket(name, mode, null, ObjectiveRenderType.Integer, null);
            });

    // Protocols 107-340 encode the render type as the wire names "integer" and "hearts". From 1.13, the field is an enum ordinal.
    private static string RenderTypeWire(ObjectiveRenderType type) =>
        type == ObjectiveRenderType.Hearts ? "hearts" : "integer";

    private static ObjectiveRenderType RenderTypeFromWire(string s) =>
        s == "hearts" ? ObjectiveRenderType.Hearts : ObjectiveRenderType.Integer;

    /// <summary>Set objective, 107-340 (1.9-1.12.2): objective name, method byte, and for add/change a plain display STRING and a render-type STRING. Decoded into the modern record so the scoreboard state is populated on this band exactly as it is from 1.13 on.</summary>
    /// <remarks>This is the same body 1.8 uses under the <c>minecraft:scoreboard_objective</c> identifier. The display name is a plain string here, so it round-trips through a literal text component the same way <see cref="SetPlayerTeamV1_8"/> handles the 1.8 team texts.</remarks>
    public static readonly PacketCodec<ClientboundSetObjectivePacket> SetObjectiveV1_9 =
        PacketCodec<ClientboundSetObjectivePacket>.Of(
            static (ref PacketWriter w, ClientboundSetObjectivePacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.ObjectiveName, 16);
                w.WriteByte((byte)p.Mode);
                if (p.Mode is ScoreboardObjectiveMode.Add or ScoreboardObjectiveMode.Change)
                {
                    w.WriteString((p.DisplayName ?? throw new ProtocolViolationException("Add/change objective requires a display name.")).ToPlainText(), 32);
                    w.WriteString(RenderTypeWire(p.RenderType), 16);
                }
            },
            static (ref PacketReader r, PacketCodecContext _) =>
            {
                string name = r.ReadString(16);
                var mode = (ScoreboardObjectiveMode)r.ReadByte();
                if (mode is ScoreboardObjectiveMode.Add or ScoreboardObjectiveMode.Change)
                {
                    Component display = Component.Text(r.ReadString(32));
                    return new ClientboundSetObjectivePacket(name, mode, display, RenderTypeFromWire(r.ReadString(16)), null);
                }

                return new ClientboundSetObjectivePacket(name, mode, null, ObjectiveRenderType.Integer, null);
            });

    /// <summary>Set objective, 393-756 (1.13-1.17.1): objective name capped at 16 chars, method byte, and for add/change a JSON-STRING component display name plus a VarInt render-type ordinal. The flattening turned the display name from a plain string into a component and the render type from a lowercase string into an enum ordinal, which is what separates this era from the 107-340 legacy form.</summary>
    /// <remarks>It is NOT the 1.21.5 codec: that one reads the display name as network NBT and appends an optional score number format, two fields the scoreboard only grew at 1.20.3.</remarks>
    public static readonly PacketCodec<ClientboundSetObjectivePacket> SetObjectiveV1_13 =
        MakeSetObjectiveLegacy(16);

    /// <summary>Set objective, 757-764 (1.18-1.20.2): the same JSON body with the objective name's 16-char cap removed. Still no number format and still a JSON display name: network NBT and the optional number format both arrive at 1.20.3.</summary>
    /// <remarks>The objective name is uncapped; network-NBT components and the optional number format begin at the 765 boundary.</remarks>
    public static readonly PacketCodec<ClientboundSetObjectivePacket> SetObjectiveV1_18 =
        MakeSetObjectiveLegacy(32767);

    private static PacketCodec<ClientboundSetObjectivePacket> MakeSetObjectiveLegacy(int nameCap) =>
        PacketCodec<ClientboundSetObjectivePacket>.Of(
            (ref PacketWriter w, ClientboundSetObjectivePacket p, PacketCodecContext _) =>
            {
                w.WriteString(p.ObjectiveName, nameCap);
                w.WriteByte((byte)p.Mode);
                if (p.Mode is ScoreboardObjectiveMode.Add or ScoreboardObjectiveMode.Change)
                {
                    WriteLegacyComponent(ref w, p.DisplayName ?? throw new ProtocolViolationException("Add/change objective requires a display name."));
                    w.WriteVarInt((int)p.RenderType);
                }
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                string name = r.ReadString(nameCap);
                var mode = (ScoreboardObjectiveMode)r.ReadByte();
                if (mode is ScoreboardObjectiveMode.Add or ScoreboardObjectiveMode.Change)
                {
                    Component display = ReadLegacyComponent(ref r);
                    return new ClientboundSetObjectivePacket(name, mode, display, (ObjectiveRenderType)r.ReadVarInt(), null);
                }

                return new ClientboundSetObjectivePacket(name, mode, null, ObjectiveRenderType.Integer, null);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareSetObjective(PacketBindings bindings)
    {
        // 107-340 carry the pre-flattening body (a plain display STRING and a render-type STRING); 1.13 turns those into a JSON component and a VarInt enum; 1.18 drops the objective name's 16-char cap; 1.20.3 turns the display name into network NBT and appends the optional number format the modern codec writes. All of them decode into the modern record so the scoreboard applier sees them; the 1.8 record stays reserved for the minecraft:scoreboard_objective identifier. Binding the 1.21.5 codec below protocol 765 reads the JSON display name as an NBT tag and demands a number-format bool that is absent, faulting the frame.
        bindings.Packet(UiPackets.Clientbound.SetObjective)
            .From(JavaProtocols.V1_9, ScoreboardCodecs.SetObjectiveV1_9)
            .From(JavaProtocols.V1_13, ScoreboardCodecs.SetObjectiveV1_13)
            .From(JavaProtocols.V1_18, ScoreboardCodecs.SetObjectiveV1_18)
            .From(JavaProtocols.V1_20_3, ScoreboardCodecs.SetObjectiveV1_20_3)
            .From(JavaProtocols.V1_21_5, ScoreboardCodecs.SetObjectiveV1_21_5);
    }
}
