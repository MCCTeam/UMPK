namespace Umpk.Protocol.Java;

/// <summary>Direction of travel of a packet. A client binds <see cref="Clientbound"/> as its inbound flow; a server binds <see cref="Serverbound"/>; a proxy binds both across two connections.</summary>
public enum PacketFlow : byte
{
    /// <summary>Server to client.</summary>
    Clientbound,

    /// <summary>Client to server.</summary>
    Serverbound,
}
