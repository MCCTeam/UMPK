using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class LoginFamilyPackets
{
    public static partial class Login
    {
        /// <summary>Login plugin response (<c>minecraft:custom_query_answer</c>), serverbound.</summary>
        public static readonly PacketType<ServerboundLoginCustomQueryAnswerPacket> CustomQueryAnswer =
            new(ProtocolPhase.Login, PacketFlow.Serverbound, Identifier.Minecraft("custom_query_answer"));
    }
}

/// <summary>Login plugin response (custom_query_answer): the transaction id and an optional payload. A null payload means "not understood" (the vanilla default answer for unclaimed channels).</summary>
public sealed record ServerboundLoginCustomQueryAnswerPacket(int TransactionId, byte[]? Data) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => LoginFamilyPackets.Login.CustomQueryAnswer;
}
