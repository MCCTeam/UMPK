using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>
/// Play-phase client information / settings, the announce every version carries and the only announce path below 1.20.2. Five wire generations:
/// <list type="bullet">
/// <item>1.8: <c>writeString(lang); writeByte(view);
/// writeByte(visibility.ordinal); writeBoolean(colors); writeByte(skinParts)</c>. Chat visibility is a plain byte here and there is no main hand.</item>
/// <item>1.9: the same prefix but visibility becomes
/// <c>writeEnum</c> (a VarInt ordinal) and a trailing <c>writeEnum(mainHand)</c> appears. Both deltas land together at 1.9 and remain unchanged through 1.16.5.</item>
/// <item>1.17: adds <c>writeBoolean(textFilteringEnabled)</c>.</item>
/// <item>1.18: adds <c>writeBoolean(allowsListing)</c>.</item>
/// <item>1.21.2: adds
/// <c>writeEnum(particleStatus)</c>.</item>
/// </list>
/// Note the 1.8-to-1.9 visibility widening is invisible to a round-trip test: for the only values the enum takes (0-2) a byte and a VarInt encode to the same single byte. The era is pinned by the presence of the main-hand field instead, and by frame-decoding real bytes.
/// </summary>
public static partial class PlayClientInformationCodecs
{
    /// <summary>1.8 (protocol 47): no main hand, and chat visibility is a plain byte.</summary>
    public static PacketCodec<ServerboundPlayClientInformationPacket> V1_8 { get; } = Make(ClientInformationWire.V1_8);

    /// <summary>1.9-1.16.5 (protocols 107-754): chat visibility widens to a VarInt and main hand appears.</summary>
    public static PacketCodec<ServerboundPlayClientInformationPacket> V1_9 { get; } = Make(ClientInformationWire.V1_9);

    /// <summary>1.17-1.17.1 (protocols 755-756): adds the text-filtering flag.</summary>
    public static PacketCodec<ServerboundPlayClientInformationPacket> V1_17 { get; } = Make(ClientInformationWire.V1_17);

    /// <summary>1.18-1.21.1 (protocols 757-767): adds the allows-listing flag.</summary>
    public static PacketCodec<ServerboundPlayClientInformationPacket> V1_18 { get; } = Make(ClientInformationWire.V1_18);

    /// <summary>1.21.2+ (protocols 768+): adds the particle-status VarInt.</summary>
    public static PacketCodec<ServerboundPlayClientInformationPacket> V1_21_2 { get; } = Make(ClientInformationWire.V1_21_2);

    private static PacketCodec<ServerboundPlayClientInformationPacket> Make(ClientInformationWire wire) =>
        PacketCodec<ServerboundPlayClientInformationPacket>.Of(
            (ref PacketWriter w, ServerboundPlayClientInformationPacket p, PacketCodecContext _) =>
                CommonPayloads.WriteClientInformation(
                    ref w,
                    new ClientInformationFields(
                        p.Language, p.ViewDistance, p.ChatVisibility, p.ChatColors, p.ModelCustomisation,
                        p.MainHand, p.TextFilteringEnabled, p.AllowsListing, p.ParticleStatus),
                    wire),
            (ref PacketReader r, PacketCodecContext _) =>
            {
                ClientInformationFields f = CommonPayloads.ReadClientInformation(ref r, wire);
                return new ServerboundPlayClientInformationPacket(
                    f.Language, f.ViewDistance, f.ChatVisibility, f.ChatColors, f.ModelCustomisation,
                    f.MainHand, f.TextFilteringEnabled, f.AllowsListing, f.ParticleStatus);
            });

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareClientInformationPlay(PacketBindings bindings)
    {
        // client information (settings) The play-phase announce is present on every version and is the only announce path below 1.20.2. Wire generations: 1.8 has a byte chat visibility and no main hand; 1.9 widens visibility to a VarInt and adds main hand (both deltas land together); 1.17 adds text filtering; 1.18 adds allows-listing; 1.21.2 adds particle status.
        bindings.Packet(PlayPackets.Serverbound.ClientInformation)
            .From(JavaProtocols.V1_8, PlayClientInformationCodecs.V1_8)
            .From(JavaProtocols.V1_9, PlayClientInformationCodecs.V1_9)
            .From(JavaProtocols.V1_17, PlayClientInformationCodecs.V1_17)
            .From(JavaProtocols.V1_18, PlayClientInformationCodecs.V1_18)
            .From(JavaProtocols.V1_21_2, PlayClientInformationCodecs.V1_21_2);
    }
}
