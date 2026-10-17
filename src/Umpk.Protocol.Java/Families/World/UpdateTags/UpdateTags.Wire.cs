using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

internal static class WorldTagCodecs
{
    private static readonly PacketCodec<ClientboundUpdateTagsPacket> TagsV1_13 = Bind(RegistryTagCodecs.FixedThree);
    private static readonly PacketCodec<ClientboundUpdateTagsPacket> TagsV1_14 = Bind(RegistryTagCodecs.FixedFour);
    private static readonly PacketCodec<ClientboundUpdateTagsPacket> TagsV1_17 = Bind(RegistryTagCodecs.Keyed);

    private static PacketCodec<ClientboundUpdateTagsPacket> Bind(PacketCodec<IReadOnlyList<TagRegistry>> payload)
        => payload.Map(static registries => new ClientboundUpdateTagsPacket(registries), static packet => packet.Registries);

    internal static void DeclareUpdateTags(PacketBindings bindings)
        => bindings.Packet(WorldPackets.Clientbound.UpdateTags)
            .From(JavaProtocols.V1_13, WorldTagCodecs.TagsV1_13)
            .From(JavaProtocols.V1_14, WorldTagCodecs.TagsV1_14)
            .From(JavaProtocols.V1_17, WorldTagCodecs.TagsV1_17);
}
