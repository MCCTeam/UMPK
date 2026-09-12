using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>Serverbound chat-command codecs (<c>minecraft:chat_command</c> and, from 1.20.5, <c>minecraft:chat_command_signed</c>). A command only EXECUTES when it arrives on one of these packets: from 1.19 (protocol 759) onward ordinary chat broadcasts text rather than dispatching a command.</summary>
/// <remarks>
/// <para>Era boundaries:</para>
/// <list type="bullet">
/// <item><description>
/// 759 (1.19): a 256-character command string, timestamp, salt and a VarInt-counted map of UTF-8 names to variable-width signatures, then the signed-preview boolean. The salt lives inside the argument-signature block and there is no last-seen window.
/// </description></item>
/// <item><description>
/// 760 (1.19.1/1.19.2): the salt moves onto the packet, the signature map becomes a collection (limit 8) of (utf16 name, VarInt-prefixed signature), and a trailing v2 last-seen update (collection of (uuid, signature) plus an optional last-received entry) is appended after the preview flag.
/// </description></item>
/// <item><description>
/// 761-765 (1.19.3-1.20.4): the preview flag is gone, argument signatures become fixed 256-byte signature values, and the window becomes a VarInt offset plus a fixed 20-bit bitset.
/// </description></item>
/// <item><description>
/// 766-769 (1.20.5-1.21.4): the packet SPLITS. <c>chat_command</c> keeps only the command string (and widens it from 256 to 32767 characters), while the signed payload moves to <c>chat_command_signed</c>. 1.21/1.21.4 still have no ack checksum.
/// </description></item>
/// <item><description>
/// 770-776 (1.21.5-26.2): identical, except the last-seen update gains its trailing checksum byte. 26.1/26.2 are byte-identical to 1.21.5.
/// </description></item>
/// </list>
/// </remarks>
public static partial class ChatCommandCodecs
{
    /// <summary>The 1.20.5+ default command-string cap.</summary>
    private const int MaxCommandChars = 32767;

    /// <summary>Maximum command-argument name length.</summary>
    private const int MaxArgumentNameChars = 16;

    /// <summary>The fixed signature width from 1.19.3 onward.</summary>
    private const int SignatureBytes = 256;

    /// <summary>The 20-bit last-seen acknowledgement bitset, ceil(20/8) = 3 bytes on the wire.</summary>
    private const int AckBitsetBytes = 3;

    // From 1.19.3, each signature is a bare fixed 256-byte block with no length prefix.
    private static void WriteFixedWidthArguments(ref PacketWriter w, IReadOnlyList<SignedCommandArgument> arguments)
    {
        w.WriteVarInt(arguments.Count);
        foreach (SignedCommandArgument argument in arguments)
        {
            if (argument.Signature.Length != SignatureBytes)
                throw new ProtocolViolationException("A command argument signature must be exactly 256 bytes.");

            w.WriteString(argument.Name, MaxArgumentNameChars);
            w.WriteBytes(argument.Signature);
        }
    }

    private static SignedCommandArgument[] ReadFixedWidthArguments(ref PacketReader r)
    {
        int count = r.ReadVarInt();
        var arguments = new SignedCommandArgument[count];
        for (int i = 0; i < count; i++)
        {
            string name = r.ReadString(MaxArgumentNameChars);
            arguments[i] = new SignedCommandArgument(name, r.ReadBytes(SignatureBytes).ToArray());
        }

        return arguments;
    }

    private static void WriteAck(ref PacketWriter w, LastSeenMessagesUpdate lastSeen, bool hasChecksum)
    {
        w.WriteVarInt(lastSeen.Offset);
        if (lastSeen.Acknowledged.Length != AckBitsetBytes)
            throw new ProtocolViolationException("The last-seen ack bitset must be 3 bytes (20 bits).");

        w.WriteBytes(lastSeen.Acknowledged);
        if (hasChecksum)
            w.WriteByte(lastSeen.Checksum);

    }

    private static LastSeenMessagesUpdate ReadAck(ref PacketReader r, bool hasChecksum)
    {
        int offset = r.ReadVarInt();
        byte[] acknowledged = r.ReadBytes(AckBitsetBytes).ToArray();
        byte checksum = hasChecksum ? r.ReadByte() : (byte)0;
        return new LastSeenMessagesUpdate(offset, acknowledged, checksum);
    }
}
