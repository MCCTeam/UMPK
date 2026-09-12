using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class PlayPackets
{
    public static partial class Serverbound
    {
        /// <summary>Chat session update (<c>minecraft:chat_session_update</c>, 1.19.3+): announces the client's chat session id and profile public key so the server can validate signed chat. Must be sent after joining play, before signed chat is accepted on an enforce-secure-profile server.</summary>
        public static readonly PacketType<ServerboundChatSessionUpdatePacket> ChatSessionUpdate =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("chat_session_update"));
    }
}

/// <summary>The 1.19.3+ chat session update: the client's chat session id and its profile public key. Sent once after joining play so the server can bind and validate the client's signed chat. The key payload is the DER public key, its expiry, and Mojang's v2 signature (<see cref="ProfilePublicKeyData"/>).</summary>
public sealed record ServerboundChatSessionUpdatePacket(Guid SessionId, ProfilePublicKeyData Key) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Serverbound.ChatSessionUpdate;
}
