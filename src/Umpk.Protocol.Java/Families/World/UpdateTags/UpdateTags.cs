namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Play-phase registry tag synchronization (<c>minecraft:update_tags</c>).</summary>
        public static readonly PacketType<ClientboundUpdateTagsPacket> UpdateTags =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("update_tags"));
    }
}

/// <summary>Registry tag payloads sent during play, including tag changes after a server data-pack reload.</summary>
public sealed record ClientboundUpdateTagsPacket(IReadOnlyList<TagRegistry> Registries) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.UpdateTags;
}
