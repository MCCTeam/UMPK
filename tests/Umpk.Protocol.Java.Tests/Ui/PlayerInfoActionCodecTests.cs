using Umpk.Game.Players;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Ui;

/// <summary>Tests for the 768 (1.21.2/1.21.3) player-info-update era: seven actions, no UPDATE_HAT. The seven-action enum writes a fixed one-byte bit set. Protocol 769 appends <c>UPDATE_HAT</c> as the eighth action.</summary>
public class PlayerInfoActionCodecTests
{
    private static byte[] RandomBytes(Random rng, int count)
    {
        var b = new byte[count];
        rng.NextBytes(b);
        return b;
    }

    private const PlayerInfoActions AllV1_21_2Actions = PlayerInfoActions.AddPlayer
        | PlayerInfoActions.InitializeChat | PlayerInfoActions.UpdateGameMode
        | PlayerInfoActions.UpdateListed | PlayerInfoActions.UpdateLatency
        | PlayerInfoActions.UpdateDisplayName | PlayerInfoActions.UpdateListOrder;

    [Theory]
    [InlineData(1)]
    [InlineData(77)]
    public void PlayerInfoUpdateV1_21_2_AllSevenActions_RoundTrips(int seed)
    {
        var rng = new Random(seed);
        var session = new RemoteChatSession(Guid.NewGuid(), rng.NextInt64(),
            RandomBytes(rng, 140), RandomBytes(rng, 256));
        var entry = new PlayerInfoEntry(
            Guid.NewGuid(), "Steve",
            [new GameProfileProperty("textures", "blob", "sig")],
            HasChatSession: true, session, GameMode.Creative, Listed: true, Latency: 55,
            Component.Text("StevePvP"), ListOrder: 3, ShowHat: false);
        var packet = new ClientboundPlayerInfoUpdatePacket(AllV1_21_2Actions, [entry]);

        var decoded = CodecRoundTrip.Cycle(PlayerListCodecs.PlayerInfoUpdateV1_21_2, packet);
        Assert.Equal(AllV1_21_2Actions, decoded.Actions);
        PlayerInfoEntry d = Assert.Single(decoded.Entries);
        Assert.Equal("Steve", d.Name);
        Assert.Equal(GameMode.Creative, d.GameMode);
        Assert.True(d.Listed);
        Assert.Equal(55, d.Latency);
        Assert.Equal(3, d.ListOrder);
        Assert.False(d.ShowHat);
        Assert.True(d.HasChatSession);
        Assert.Equal(session.SessionId, d.ChatSession!.SessionId);
        Assert.Equal(session.PublicKey, d.ChatSession.PublicKey);
        Assert.Equal(session.KeySignature, d.ChatSession.KeySignature);
    }

    [Fact]
    public void PlayerInfoUpdateV1_21_2_HatlessSubset_MatchesV1_21_5_Bytes()
    {
        // Without the hat action the 7-action era and the 8-action era share the exact wire prefix (same one-byte BitSet, same per-field order), so the encodings must be byte-identical.
        var entry = new PlayerInfoEntry(
            Guid.NewGuid(), null, null, false, null, GameMode.Survival, true, 42, null, 7, false);
        var packet = new ClientboundPlayerInfoUpdatePacket(
            PlayerInfoActions.UpdateGameMode | PlayerInfoActions.UpdateListed
                | PlayerInfoActions.UpdateLatency | PlayerInfoActions.UpdateListOrder,
            [entry]);

        byte[] v2 = CodecRoundTrip.Encode(PlayerListCodecs.PlayerInfoUpdateV1_21_2, packet);
        byte[] v5 = CodecRoundTrip.Encode(PlayerListCodecs.PlayerInfoUpdateV1_21_5, packet);
        Assert.Equal(v5, v2);
    }

    [Fact]
    public void PlayerInfoUpdateV1_21_2_EmptyEntries_RoundTrips()
    {
        var packet = new ClientboundPlayerInfoUpdatePacket(PlayerInfoActions.UpdateListed, []);
        Assert.Empty(CodecRoundTrip.Cycle(PlayerListCodecs.PlayerInfoUpdateV1_21_2, packet).Entries);
    }

    [Fact]
    public void PlayerInfoUpdateV1_21_2_EncodingHatAction_Throws()
    {
        var entry = new PlayerInfoEntry(
            Guid.NewGuid(), null, null, false, null, GameMode.Undefined, false, 0, null, 0, true);
        var packet = new ClientboundPlayerInfoUpdatePacket(PlayerInfoActions.UpdateHat, [entry]);
        Assert.Throws<ProtocolViolationException>(() => CodecRoundTrip.Encode(PlayerListCodecs.PlayerInfoUpdateV1_21_2, packet));
    }

    [Fact]
    public void PlayerInfoUpdateV1_21_2_DecodingHatBit_Throws()
    {
        // Actions byte with the undefined bit 7 set and an empty entry list: bit 7 has no reader on 768 (UPDATE_HAT arrived at 1.21.4), so the era codec must reject it loudly instead of desynchronizing the per-entry field parse.
        byte[] frame = [0x80, 0x00];
        Assert.Throws<ProtocolViolationException>(() => Decode(frame));

        static ClientboundPlayerInfoUpdatePacket Decode(byte[] bytes)
        {
            var reader = new PacketReader(bytes);
            return PlayerListCodecs.PlayerInfoUpdateV1_21_2.Decode(ref reader, PacketCodecContext.Registryless);
        }
    }
}
