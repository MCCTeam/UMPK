using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Umpk.Text.Serialization;
using static Umpk.Protocol.Java.Codecs.UiCodecShared;

namespace Umpk.Protocol.Java.Codecs;

public static partial class PlayerListCodecs
{
    /// <summary>Legacy player list item, 1.8-1.18.2 wire (47-758): VarInt action, then per entry a uuid followed by only that action's fields. Add carries name(16) + profile properties + VarInt gamemode + VarInt latency + optional JSON display name; the three update actions carry their single field; remove carries nothing beyond the uuid.</summary>
    public static readonly PacketCodec<ClientboundLegacyPlayerListItemPacket> LegacyPlayerListItemV1_8 =
        MakeLegacyPlayerListItem(hasProfileKey: false);

    /// <summary>Legacy player list item, 1.19-1.19.2 wire (759/760): identical to the 1.8 form except that the add-player payload gains a trailing <c>Optional&lt;ProfilePublicKey.Data&gt;</c> (expiry epoch-millis + DER key byte array + signature byte array), matching the same optional the signing-era login hello carries. 1.19.3 dropped this packet for the split player-info pair.</summary>
    public static readonly PacketCodec<ClientboundLegacyPlayerListItemPacket> LegacyPlayerListItemV1_19 =
        MakeLegacyPlayerListItem(hasProfileKey: true);

    // Legacy single-packet player-list body shared by the 1.8-1.18.2 and 1.19-1.19.2 eras. The only wire difference across that whole band is the trailing optional profile public key the 1.19 add-player payload appended in 1.19 before the packet was removed in 1.19.3, so <paramref name="hasProfileKey"/> is the whole era switch. Components stay JSON strings for the whole band (network-NBT components arrive at 1.20.3, long after this packet is gone).
    private static PacketCodec<ClientboundLegacyPlayerListItemPacket> MakeLegacyPlayerListItem(bool hasProfileKey) =>
        PacketCodec<ClientboundLegacyPlayerListItemPacket>.Of(
            (ref PacketWriter w, ClientboundLegacyPlayerListItemPacket p, PacketCodecContext _) =>
            {
                w.WriteVarInt((int)p.Action);
                w.WriteList(p.Entries, (ref PacketWriter ew, LegacyPlayerListEntry entry) =>
                {
                    ew.WriteUuid(entry.ProfileId);
                    switch (p.Action)
                    {
                        case LegacyPlayerListAction.AddPlayer:
                            ew.WriteString(entry.Name ?? throw new ProtocolViolationException("Add-player requires a name."), 16);
                            ew.WriteList(entry.Properties ?? [], WriteProfileProperty);
                            ew.WriteVarInt(entry.GameMode);
                            ew.WriteVarInt(entry.Latency);
                            ew.WriteOptional(entry.DisplayName, WriteLegacyComponent);
                            if (hasProfileKey)
                                ew.WriteOptional(entry.ProfileKey, WriteProfilePublicKey);

                            break;
                        case LegacyPlayerListAction.UpdateGameMode:
                            ew.WriteVarInt(entry.GameMode);
                            break;
                        case LegacyPlayerListAction.UpdateLatency:
                            ew.WriteVarInt(entry.Latency);
                            break;
                        case LegacyPlayerListAction.UpdateDisplayName:
                            ew.WriteOptional(entry.DisplayName, WriteLegacyComponent);
                            break;
                        case LegacyPlayerListAction.RemovePlayer:
                            break;
                        default:
                            break;
                    }
                });
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                var action = (LegacyPlayerListAction)r.ReadVarInt();
                LegacyPlayerListEntry[] entries = r.ReadList((ref PacketReader er) =>
                {
                    Guid id = er.ReadUuid();
                    string? name = null;
                    IReadOnlyList<GameProfileProperty>? props = null;
                    int gameMode = 0;
                    int latency = 0;
                    Component? display = null;
                    ProfilePublicKeyData? profileKey = null;
                    switch (action)
                    {
                        case LegacyPlayerListAction.AddPlayer:
                            name = er.ReadString(16);
                            props = er.ReadList(ReadProfileProperty);
                            gameMode = er.ReadVarInt();
                            latency = er.ReadVarInt();
                            display = er.ReadOptional(ReadLegacyComponent);
                            if (hasProfileKey)
                                profileKey = er.ReadOptional(ReadProfilePublicKey);

                            break;
                        case LegacyPlayerListAction.UpdateGameMode:
                            gameMode = er.ReadVarInt();
                            break;
                        case LegacyPlayerListAction.UpdateLatency:
                            latency = er.ReadVarInt();
                            break;
                        case LegacyPlayerListAction.UpdateDisplayName:
                            display = er.ReadOptional(ReadLegacyComponent);
                            break;
                        case LegacyPlayerListAction.RemovePlayer:
                            break;
                        default:
                            break;
                    }

                    return new LegacyPlayerListEntry(id, name, props, gameMode, latency, display) { ProfileKey = profileKey };
                });

                return new ClientboundLegacyPlayerListItemPacket(action, entries);
            });

    // ProfilePublicKey.Data: Instant expiresAt written as epoch millis, then the DER SubjectPublicKeyInfo key bytes and the Mojang key signature, both length-prefixed byte arrays. Same three fields the signing-era login hello writes (LoginCodecs.SigningEraHello).
    private static ProfilePublicKeyData ReadProfilePublicKey(ref PacketReader r)
    {
        long expiresAt = r.ReadLong();
        byte[] keyDer = r.ReadByteArray().ToArray();
        byte[] signature = r.ReadByteArray().ToArray();
        return new ProfilePublicKeyData(expiresAt, keyDer, signature);
    }

    private static void WriteProfilePublicKey(ref PacketWriter w, ProfilePublicKeyData key)
    {
        w.WriteLong(key.ExpiresAtMillis);
        w.WriteByteArray(key.KeyDer);
        w.WriteByteArray(key.KeySignature);
    }

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePlayerInfo(PacketBindings bindings)
    {
        // The legacy single player_info packet is the tab-list wire form for the whole 47-760 band (1.8 through 1.19.2); 1.19.3 replaced it with the player_info_update / player_info_remove pair, and no protocol from 761 on registers this identifier at all. 47-758 share one shape; 759/760 append an optional profile public key to the add-player payload.
        bindings.Packet(UiPackets.Clientbound.LegacyPlayerListItem)
            .From(JavaProtocols.V1_8, PlayerListCodecs.LegacyPlayerListItemV1_8)
            .From(JavaProtocols.V1_19, PlayerListCodecs.LegacyPlayerListItemV1_19);
    }
}
