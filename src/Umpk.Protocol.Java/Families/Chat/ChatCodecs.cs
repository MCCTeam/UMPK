using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>Chat codecs across all eras. 1.8-1.18.2 (ServerLegacy/ClientLegacy): serverbound is a bare string, clientbound is a JSON component plus a position byte, gaining a sender UUID at 1.16. The serverbound signing era then lands in three generations (SignedV*): v1 = 1.19, v2 = 1.19.1/1.19.2, v3 = 1.19.3 onward (nullable fixed signature + offset/bitset ack, with the checksum byte added at 1.21.5). The same three generations shape the clientbound <c>minecraft:player_chat</c> (protocols 759-763); components are JSON strings across that range and the signing payloads are built and verified session-side (<see cref="Signing.SignedChatVerifier"/>), so these codecs only move the packet on and off the wire.</summary>
public static partial class ChatCodecs
{
    /// <summary>The v2 (1.19.1/1.19.2) serverbound acknowledgment block, shared by <c>chat</c> and <c>chat_command</c>: <c>LastSeenMessages</c> (VarInt count, then per entry a big-endian uuid and a VarInt-prefixed signature) followed by an optional last-received entry of the same shape.</summary>
    internal static void WriteLegacyAcknowledgment(
        ref PacketWriter w, IReadOnlyList<LastSeenMessageEntry> lastSeen, LastSeenMessageEntry? lastReceived)
    {
        w.WriteVarInt(lastSeen.Count);
        foreach (LastSeenMessageEntry entry in lastSeen)
        {
            w.WriteUuid(entry.ProfileId);
            w.WriteByteArray(entry.Signature);
        }

        w.WriteBool(lastReceived is not null);
        if (lastReceived is not null)
        {
            w.WriteUuid(lastReceived.ProfileId);
            w.WriteByteArray(lastReceived.Signature);
        }
    }

    /// <summary>Reads the v2 acknowledgment block written by <see cref="WriteLegacyAcknowledgment"/>.</summary>
    internal static (LastSeenMessageEntry[] LastSeen, LastSeenMessageEntry? LastReceived) ReadLegacyAcknowledgment(
        ref PacketReader r)
    {
        int count = r.ReadVarInt();
        var entries = new LastSeenMessageEntry[count];
        for (int i = 0; i < count; i++)
        {
            Guid profileId = r.ReadUuid();
            entries[i] = new LastSeenMessageEntry(profileId, r.ReadByteArray().ToArray());
        }

        LastSeenMessageEntry? lastReceived = null;
        if (r.ReadBool())
        {
            Guid profileId = r.ReadUuid();
            lastReceived = new LastSeenMessageEntry(profileId, r.ReadByteArray().ToArray());
        }

        return (entries, lastReceived);
    }

    private const int MaxContentChars = 256;
}
