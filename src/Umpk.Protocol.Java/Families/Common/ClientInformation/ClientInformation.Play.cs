using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class PlayPackets
{
    public static partial class Serverbound
    {
        /// <summary>Client information / settings (<c>minecraft:client_information</c>, spelled <c>client_settings</c> in the 1.8-1.12 datasets). The play-phase announce exists on every version and is the only path below 1.20.2, where no configuration phase exists. Servers accept a re-announce at any point during play.</summary>
        public static readonly PacketType<ServerboundPlayClientInformationPacket> ClientInformation =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("client_information"));
    }
}

/// <summary>Client information / settings announced in the play phase. Fields are the version superset; each era codec writes only what its version carries, and the reader defaults the rest. The play announce has its own packet type even though its 1.20.2+ body is wire-identical to the configuration form.</summary>
/// <param name="Language">The locale string, for example <c>en_us</c>.</param>
/// <param name="ViewDistance">Client view distance in chunks.</param>
/// <param name="ChatVisibility">0 full, 1 system only, 2 hidden. A plain byte on 1.8, a VarInt from 1.9.</param>
/// <param name="ChatColors">Whether the client renders chat colors.</param>
/// <param name="ModelCustomisation">The displayed-skin-part bit flags.</param>
/// <param name="MainHand">0 left, 1 right. 1.9+ only.</param>
/// <param name="TextFilteringEnabled">Whether server-side text filtering is enabled. 1.17+ only.</param>
/// <param name="AllowsListing">Whether the player may appear in server listings. 1.18+ only.</param>
/// <param name="ParticleStatus">0 all, 1 decreased, 2 minimal. 1.21.2+ only.</param>
public sealed record ServerboundPlayClientInformationPacket(
    string Language,
    sbyte ViewDistance,
    int ChatVisibility,
    bool ChatColors,
    byte ModelCustomisation,
    int MainHand,
    bool TextFilteringEnabled,
    bool AllowsListing,
    int ParticleStatus) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Serverbound.ClientInformation;
}
