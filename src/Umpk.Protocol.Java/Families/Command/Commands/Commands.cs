namespace Umpk.Protocol.Java.Packets;

/// <summary>The declare-commands packet family (the command-tree half of the commands surface). One clientbound packet (<c>minecraft:commands</c>, the server's Brigadier command tree) on protocols 770/776; it does not exist on 47 (DeclareCommands is 1.13+). Kept in its own type holder so it does not touch the UI family's <c>command_suggestions</c> declarations.</summary>
public static partial class CommandsPackets
{
    /// <summary>Clientbound declare-commands packets.</summary>
    public static partial class Clientbound
    {
        /// <summary>The server command tree (<c>minecraft:commands</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundCommandsPacket> Commands =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("commands"));
    }
}

/// <summary>Declare commands (770/776): the server's full Brigadier command tree as the wire-shaped <see cref="CommandTreeData"/> node graph plus the root index. Mirrors <c>ClientboundCommandsPacket</c>: a list of node entries then the root index. The typed reconstruction into a Brigadier tree lives in <c>Umpk.Client.Commands.ServerCommandTree</c>; this record is the lossless wire form the codec produces and consumes.</summary>
public sealed record ClientboundCommandsPacket(CommandTreeData Tree) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => CommandsPackets.Clientbound.Commands;
}
