using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Ui;

/// <summary>
/// Byte-anchored tests for the legacy single <c>minecraft:player_info</c> packet, the tab-list wire form for protocols 47-760 (1.8 through 1.19.2). Two era codecs cover the band: the 1.8 form (47-758) and the 1.19 form (759/760), which appends optional profile-public-key data to the add-player payload. 1.19.3 (761) removed the packet in favour of the player-info-update / player-info-remove pair, so 760 is the last protocol that carries it.
///
/// Add-player writes name(16) + profile properties + VarInt gamemode + VarInt latency + nullable JSON component display name, and from 1.19 a trailing nullable profile public key (epoch-millis expiry + length-prefixed DER key + length-prefixed signature, the same three fields the signing-era login hello writes). The hex vectors below are hand-built from that field order, not captured from the encoder.
/// </summary>
public class PlayerInfoEntryCodecTests
{
    // 00000000-0000-0000-0000-000000000001 as the 16 big-endian uuid bytes.
    private static readonly byte[] UuidOne =
        [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1];

    private static readonly Guid GuidOne = new(
        [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1], bigEndian: true);

    [Fact]
    public void AddPlayer_1_8_MatchesHandBuiltWire()
    {
        // action=0(add), count=1, uuid, name "Bob"(len 3), properties count=0, gamemode=1, latency=42, hasDisplayName=false. No trailing profile key on 47-758.
        byte[] wire =
        [
            0x00,
            0x01,
            .. UuidOne,
            0x03, (byte)'B', (byte)'o', (byte)'b',
            0x00,
            0x01,
            0x2A,
            0x00,
        ];

        var decoded = CodecRoundTrip.Decode(PlayerListCodecs.LegacyPlayerListItemV1_8, wire);

        Assert.Equal(LegacyPlayerListAction.AddPlayer, decoded.Action);
        LegacyPlayerListEntry entry = Assert.Single(decoded.Entries);
        Assert.Equal(GuidOne, entry.ProfileId);
        Assert.Equal("Bob", entry.Name);
        Assert.Empty(entry.Properties!);
        Assert.Equal(1, entry.GameMode);
        Assert.Equal(42, entry.Latency);
        Assert.Null(entry.DisplayName);
        Assert.Null(entry.ProfileKey);
        Assert.Equal(wire, CodecRoundTrip.Encode(PlayerListCodecs.LegacyPlayerListItemV1_8, decoded));
    }

    [Fact]
    public void AddPlayer_1_19_ReadsTrailingProfileKey()
    {
        // Same prefix as the 1.8 add, then hasProfileKey=true, expiry=1 (8 bytes big-endian), key length 2 + bytes, signature length 3 + bytes.
        byte[] wire =
        [
            0x00,
            0x01,
            .. UuidOne,
            0x03, (byte)'B', (byte)'o', (byte)'b',
            0x00,
            0x01,
            0x2A,
            0x00,
            0x01,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,
            0x02, 0xAA, 0xBB,
            0x03, 0x01, 0x02, 0x03,
        ];

        var decoded = CodecRoundTrip.Decode(PlayerListCodecs.LegacyPlayerListItemV1_19, wire);

        LegacyPlayerListEntry entry = Assert.Single(decoded.Entries);
        Assert.Equal("Bob", entry.Name);
        Assert.Equal(42, entry.Latency);
        Assert.NotNull(entry.ProfileKey);
        Assert.Equal(1L, entry.ProfileKey!.ExpiresAtMillis);
        Assert.Equal<byte[]>([0xAA, 0xBB], entry.ProfileKey.KeyDer);
        Assert.Equal<byte[]>([0x01, 0x02, 0x03], entry.ProfileKey.KeySignature);
        Assert.Equal(wire, CodecRoundTrip.Encode(PlayerListCodecs.LegacyPlayerListItemV1_19, decoded));
    }

    [Fact]
    public void AddPlayer_1_19_AbsentProfileKey_IsOneTrailingZeroByte()
    {
        // The 1.19 add payload is the 1.8 payload plus exactly one false optional flag when the player carries no signing key, which is what an offline-mode server always sends.
        var packet = new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.AddPlayer,
            [new LegacyPlayerListEntry(GuidOne, "Bob", [], 1, 42, null)]);

        byte[] legacy = CodecRoundTrip.Encode(PlayerListCodecs.LegacyPlayerListItemV1_8, packet);
        byte[] signing = CodecRoundTrip.Encode(PlayerListCodecs.LegacyPlayerListItemV1_19, packet);

        Assert.Equal(legacy.Length + 1, signing.Length);
        Assert.Equal(legacy, signing[..legacy.Length]);
        Assert.Equal(0x00, signing[^1]);
    }

    [Theory]
    [InlineData(LegacyPlayerListAction.UpdateGameMode)]
    [InlineData(LegacyPlayerListAction.UpdateLatency)]
    [InlineData(LegacyPlayerListAction.UpdateDisplayName)]
    [InlineData(LegacyPlayerListAction.RemovePlayer)]
    public void NonAddActions_AreIdenticalAcrossTheWholeBand(LegacyPlayerListAction action)
    {
        // Only add-player changed at 1.19, so every other action must encode identically on both era codecs. This is what lets one timeline cover 47-760 with a single era split.
        var packet = new ClientboundLegacyPlayerListItemPacket(
            action,
            [new LegacyPlayerListEntry(GuidOne, null, null, 2, 33,
                action == LegacyPlayerListAction.UpdateDisplayName ? Component.Text("Bobby") : null)]);

        byte[] legacy = CodecRoundTrip.Encode(PlayerListCodecs.LegacyPlayerListItemV1_8, packet);
        byte[] signing = CodecRoundTrip.Encode(PlayerListCodecs.LegacyPlayerListItemV1_19, packet);

        Assert.Equal(legacy, signing);
    }

    [Fact]
    public void RemovePlayer_1_8_MatchesHandBuiltWire()
    {
        // action=4(remove), count=1, uuid. Remove carries nothing past the uuid on any era.
        byte[] wire = [0x04, 0x01, .. UuidOne];

        var decoded = CodecRoundTrip.Decode(PlayerListCodecs.LegacyPlayerListItemV1_8, wire);

        Assert.Equal(LegacyPlayerListAction.RemovePlayer, decoded.Action);
        Assert.Equal(GuidOne, Assert.Single(decoded.Entries).ProfileId);
        Assert.Equal(wire, CodecRoundTrip.Encode(PlayerListCodecs.LegacyPlayerListItemV1_8, decoded));
    }

    [Fact]
    public void UpdateLatency_MatchesHandBuiltWire()
    {
        // action=2(latency), count=1, uuid, latency=300 as a VarInt (0xAC 0x02).
        byte[] wire = [0x02, 0x01, .. UuidOne, 0xAC, 0x02];

        var decoded = CodecRoundTrip.Decode(PlayerListCodecs.LegacyPlayerListItemV1_19, wire);

        Assert.Equal(LegacyPlayerListAction.UpdateLatency, decoded.Action);
        Assert.Equal(300, Assert.Single(decoded.Entries).Latency);
        Assert.Equal(wire, CodecRoundTrip.Encode(PlayerListCodecs.LegacyPlayerListItemV1_19, decoded));
    }

    [Fact]
    public void AddPlayer_WithPropertiesAndDisplayName_RoundTripsOnBothWireLayouts()
    {
        var entry = new LegacyPlayerListEntry(
            Guid.NewGuid(),
            "Skinned",
            [new GameProfileProperty("textures", "base64blob", "mojangsig")],
            0,
            77,
            Component.Text("[VIP] Skinned"));
        var packet = new ClientboundLegacyPlayerListItemPacket(LegacyPlayerListAction.AddPlayer, [entry]);

        foreach (PacketCodec<ClientboundLegacyPlayerListItemPacket> codec in
                 new[] { PlayerListCodecs.LegacyPlayerListItemV1_8, PlayerListCodecs.LegacyPlayerListItemV1_19 })
        {
            var decoded = CodecRoundTrip.Cycle(codec, packet);
            LegacyPlayerListEntry d = Assert.Single(decoded.Entries);
            Assert.Equal("Skinned", d.Name);
            GameProfileProperty property = Assert.Single(d.Properties!);
            Assert.Equal("textures", property.Name);
            Assert.Equal("base64blob", property.Value);
            Assert.Equal("mojangsig", property.Signature);
            Assert.Equal("[VIP] Skinned", d.DisplayName!.ToPlainText());
        }
    }

    [Fact]
    public void MultipleEntries_ShareTheSingleActionHeader()
    {
        // The legacy packet batches entries under one action; the modern split pair batches under one action bitset. Both must survive a multi-entry frame.
        var packet = new ClientboundLegacyPlayerListItemPacket(
            LegacyPlayerListAction.UpdateLatency,
            [
                new LegacyPlayerListEntry(Guid.NewGuid(), null, null, 0, 10, null),
                new LegacyPlayerListEntry(Guid.NewGuid(), null, null, 0, 20, null),
                new LegacyPlayerListEntry(Guid.NewGuid(), null, null, 0, 30, null),
            ]);

        var decoded = CodecRoundTrip.Cycle(PlayerListCodecs.LegacyPlayerListItemV1_8, packet);

        Assert.Equal([10, 20, 30], decoded.Entries.Select(static e => e.Latency));
    }
}
