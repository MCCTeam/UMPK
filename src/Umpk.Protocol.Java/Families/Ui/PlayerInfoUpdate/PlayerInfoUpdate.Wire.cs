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
    /// <summary>Modern player info update (770/776); an 8-action fixed BitSet then per-entry fields.</summary>
    public static readonly PacketCodec<ClientboundPlayerInfoUpdatePacket> PlayerInfoUpdateV1_21_5 =
        MakePlayerInfoUpdate(PlayerInfoWire.V1_19_3, ComponentWire.V1_21_5);

    /// <summary>761-764 (1.19.3-1.20.2) player info update: the display name is a JSON-string component. The action-order array is the full modern one; actions that do not exist yet simply never have their bit set, and the prefix order is stable, so one order array serves the whole pre-1.21.2 band.</summary>
    public static readonly PacketCodec<ClientboundPlayerInfoUpdatePacket> PlayerInfoUpdateV1_19_3 =
        MakePlayerInfoUpdate(PlayerInfoWire.V1_19_3, ComponentWire.V1_8);

    /// <summary>765-767 and 769 (1.20.3-1.21, and 1.21.4) player info update: network-NBT display name, legacy click/hover interactions. 768 leaves this band only because it predates UPDATE_HAT (see <see cref="PlayerInfoUpdateV1_21_2"/>); 1.21.4 restores the 8-action order while the component era stays legacy until 1.21.5, so the band is re-entered rather than duplicated.</summary>
    public static readonly PacketCodec<ClientboundPlayerInfoUpdatePacket> PlayerInfoUpdateV1_20_3 =
        MakePlayerInfoUpdate(PlayerInfoWire.V1_19_3, ComponentWire.V1_20_3);

    /// <summary>768 (1.21.2/1.21.3) player info update: a 7-action fixed BitSet (one byte, bits 0-6) then per-entry fields; the UPDATE_HAT action does not exist yet.</summary>
    public static readonly PacketCodec<ClientboundPlayerInfoUpdatePacket> PlayerInfoUpdateV1_21_2 =
        MakePlayerInfoUpdate(PlayerInfoWire.V1_21_2, ComponentWire.V1_20_3);

    // Player-info-update body shared by the 1.21.2 (7-action) and 1.21.5 (8-action) eras. The only wire differences are the action-order array (which fields ride, and their order) and whether UPDATE_HAT exists: when <paramref name="hasHat"/> is false the action set is guarded so bit 7 never rides the wire, and UPDATE_HAT is simply absent from <paramref name="actionOrder"/> so per-entry ShowHat stays false without a separate read. Both eras write a single-byte action BitSet.
    private static PacketCodec<ClientboundPlayerInfoUpdatePacket> MakePlayerInfoUpdate(
        PlayerInfoWire era, ComponentWire text) =>
        PacketCodec<ClientboundPlayerInfoUpdatePacket>.Of(
            (ref PacketWriter w, ClientboundPlayerInfoUpdatePacket p, PacketCodecContext _) =>
            {
                if (!era.HasHat && (p.Actions & PlayerInfoActions.UpdateHat) != 0)
                    throw new ProtocolViolationException("The update-hat action does not exist on protocol 768 (1.21.2/1.21.3).");

                w.WriteByte((byte)p.Actions);
                w.WriteList(p.Entries, (ref PacketWriter ew, PlayerInfoEntry entry) =>
                {
                    ew.WriteUuid(entry.ProfileId);
                    foreach (PlayerInfoActions action in era.Order)
                    {
                        if ((p.Actions & action) == 0)
                            continue;

                        WritePlayerInfoField(ref ew, action, entry, text.Write);
                    }
                });
            },
            (ref PacketReader r, PacketCodecContext _) =>
            {
                var actions = (PlayerInfoActions)r.ReadByte();
                if (!era.HasHat && (actions & PlayerInfoActions.UpdateHat) != 0)
                    throw new ProtocolViolationException("A 768 (1.21.2/1.21.3) player-info-update frame set the undefined bit 7 (update-hat arrived at 1.21.4).");

                PlayerInfoEntry[] entries = r.ReadList((ref PacketReader er) =>
                {
                    Guid id = er.ReadUuid();
                    string? name = null;
                    IReadOnlyList<GameProfileProperty>? props = null;
                    bool hasSession = false;
                    RemoteChatSession? session = null;
                    var gameMode = GameMode.Undefined;
                    bool listed = false;
                    int latency = 0;
                    Component? display = null;
                    int listOrder = 0;
                    bool showHat = false;
                    foreach (PlayerInfoActions action in era.Order)
                    {
                        if ((actions & action) == 0)
                            continue;

                        switch (action)
                        {
                            case PlayerInfoActions.AddPlayer:
                                name = er.ReadString(16);
                                props = er.ReadList(ReadProfileProperty);
                                break;
                            case PlayerInfoActions.InitializeChat:
                                hasSession = er.ReadBool();
                                session = hasSession ? ReadRemoteChatSession(ref er) : null;
                                break;
                            case PlayerInfoActions.UpdateGameMode:
                                gameMode = (GameMode)er.ReadVarInt();
                                break;
                            case PlayerInfoActions.UpdateListed:
                                listed = er.ReadBool();
                                break;
                            case PlayerInfoActions.UpdateLatency:
                                latency = er.ReadVarInt();
                                break;
                            case PlayerInfoActions.UpdateDisplayName:
                                display = er.ReadOptional(text.Read);
                                break;
                            case PlayerInfoActions.UpdateListOrder:
                                listOrder = er.ReadVarInt();
                                break;
                            case PlayerInfoActions.UpdateHat:
                                showHat = er.ReadBool();
                                break;
                            default:
                                break;
                        }
                    }

                    return new PlayerInfoEntry(id, name, props, hasSession, session, gameMode, listed, latency, display, listOrder, showHat);
                });

                return new ClientboundPlayerInfoUpdatePacket(actions, entries);
            },
            WireShape.Of("byte,varint*(uuid,per_action_fields)", $"{era},{text.Form}"));

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclarePlayerInfoUpdate(PacketBindings bindings)
    {
        // The display name is a component, so this chain carries BOTH boundaries as well as the action-set change: JSON to 764, network NBT from 765, modern interactions from 770; and 768 separately drops out to the 7-action order because UPDATE_HAT only arrives at 1.21.4.
        bindings.Packet(UiPackets.Clientbound.PlayerInfoUpdate)
            .From(JavaProtocols.V1_19_3, PlayerListCodecs.PlayerInfoUpdateV1_19_3)
            .From(JavaProtocols.V1_20_3, PlayerListCodecs.PlayerInfoUpdateV1_20_3)
            .From(JavaProtocols.V1_21_2, PlayerListCodecs.PlayerInfoUpdateV1_21_2)
            .From(JavaProtocols.V1_21_4, PlayerListCodecs.PlayerInfoUpdateV1_20_3)
            .From(JavaProtocols.V1_21_5, PlayerListCodecs.PlayerInfoUpdateV1_21_5);
    }
}
