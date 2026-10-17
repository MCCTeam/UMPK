using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.LoginConfigWire;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ConfigurationCodecs
{
    /// <summary>Configuration tag synchronization uses the keyed registry payload introduced in play at 1.17.</summary>
    public static readonly PacketCodec<ClientboundConfigUpdateTagsPacket> UpdateTags = RegistryTagCodecs.Keyed.Map(
        static registries => new ClientboundConfigUpdateTagsPacket(registries), static packet => packet.Registries);

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareUpdateTags(PacketBindings bindings)
    {
        bindings.Packet(LoginFamilyPackets.Config.UpdateTags)
            .From(JavaEras.ConfigurationPhase, ConfigurationCodecs.UpdateTags);
    }
}
